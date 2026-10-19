using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Retail360.Application.Catalog;
using Retail360.Application.Identity;
using Retail360.Application.Purchasing;
using Retail360.Application.Suppliers;
using Retail360.Domain.Identity;
using Retail360.Domain.Inventory;
using Retail360.Domain.Purchasing;
using Retail360.Domain.Suppliers;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Identity;
using Retail360.Infrastructure.Persistence.Sql;
using Retail360.Infrastructure.Purchasing;
using Retail360.Infrastructure.Suppliers;
using Xunit;

namespace Retail360.Tests;

[CollectionDefinition("purchasing-database", DisableParallelization = true)]
public sealed class PurchaseDatabaseCollection : ICollectionFixture<PurchaseDatabaseFixture>
{
}

[Collection("purchasing-database")]
public sealed class PurchaseDatabaseTests
{
    private const string Password = "correct-horse";

    private readonly PurchaseDatabaseFixture _database;

    public PurchaseDatabaseTests(PurchaseDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void Guard_rejects_shop_and_other_test_databases_without_connecting()
    {
        Assert.Throws<InvalidOperationException>(() => PurchaseDatabaseGuard.Require("retail360"));
        Assert.Throws<InvalidOperationException>(() => PurchaseDatabaseGuard.Require("retail360_dev"));
        Assert.Throws<InvalidOperationException>(() => PurchaseDatabaseGuard.Require("retail360_catalog_test"));
        Assert.Throws<InvalidOperationException>(() => PurchaseDatabaseGuard.Require("retail360_inventory_test"));
        Assert.Throws<InvalidOperationException>(() => PurchaseDatabaseGuard.Require("retail360_identity_test"));
        Assert.Throws<InvalidOperationException>(() => PurchaseDatabaseGuard.Require("retail360_suppliers_test"));
        PurchaseDatabaseGuard.Require(PurchaseDatabaseGuard.PermittedDatabase);
    }

    [Fact]
    public async Task Migration_is_recorded_once_on_the_purchasing_database()
    {
        await using var connection = await _database.OpenAsync();
        var name = await connection.ExecuteScalarAsync<string>("select current_database();");
        var versions = (await connection.QueryAsync<string>(
            "select version from schema_migrations order by version;")).ToArray();
        var rows = await connection.ExecuteScalarAsync<long>(
            "select count(*) from schema_migrations where version = '007_purchases';");

        Assert.Equal(PurchaseDatabaseGuard.PermittedDatabase, name);
        Assert.Contains("007_purchases", versions);
        Assert.Equal(1, rows);
        Assert.Equal(versions.Distinct(StringComparer.Ordinal).Count(), versions.Length);
    }

    [Fact]
    public async Task Create_stores_the_server_total_and_rejects_missing_or_invalid_values()
    {
        var service = _database.CreatePurchases();
        var supplier = await SeedSupplierAsync(true);
        var product = await SeedProductAsync(true);
        var created = await service.CreateAsync(
            new SavePurchaseCommand(
                supplier,
                new DateOnly(2026, 10, 20),
                "  INV-1 ",
                "   ",
                [new SavePurchaseLineCommand(product, 1.250m, 0.10m)]),
            CancellationToken.None);

        var missingSupplier = await Assert.ThrowsAsync<PurchaseRuleException>(() => service.CreateAsync(
            new SavePurchaseCommand(Guid.NewGuid(), new DateOnly(2026, 10, 20), null, null, [new SavePurchaseLineCommand(product, 1m, 1m)]),
            CancellationToken.None));
        var missingProduct = await Assert.ThrowsAsync<PurchaseRuleException>(() => service.CreateAsync(
            new SavePurchaseCommand(supplier, new DateOnly(2026, 10, 20), null, null, [new SavePurchaseLineCommand(Guid.NewGuid(), 1m, 1m)]),
            CancellationToken.None));
        var quantity = await Assert.ThrowsAsync<PurchaseRuleException>(() => service.CreateAsync(
            new SavePurchaseCommand(supplier, new DateOnly(2026, 10, 20), null, null, [new SavePurchaseLineCommand(product, 1.0001m, 1m)]),
            CancellationToken.None));
        var cost = await Assert.ThrowsAsync<PurchaseRuleException>(() => service.CreateAsync(
            new SavePurchaseCommand(supplier, new DateOnly(2026, 10, 20), null, null, [new SavePurchaseLineCommand(product, 1m, 1.235m)]),
            CancellationToken.None));

        Assert.Equal(0.13m, created.Total);
        Assert.Equal(0.13m, Assert.Single(created.Lines).LineTotal);
        Assert.Equal("INV-1", created.Reference);
        Assert.Null(created.Notes);
        Assert.Equal(PurchaseRuleFailure.NotFound, missingSupplier.Failure);
        Assert.Equal(PurchaseRuleFailure.NotFound, missingProduct.Failure);
        Assert.Equal(PurchaseRuleFailure.Invalid, quantity.Failure);
        Assert.Equal(PurchaseRuleFailure.Invalid, cost.Failure);
    }

    [Fact]
    public async Task Inactive_supplier_is_rejected_and_an_inactive_product_can_be_purchased()
    {
        var service = _database.CreatePurchases();
        var inactiveSupplier = await SeedSupplierAsync(false);
        var activeSupplier = await SeedSupplierAsync(true);
        var inactiveProduct = await SeedProductAsync(false);
        var activeProduct = await SeedProductAsync(true);

        var inactive = await Assert.ThrowsAsync<PurchaseRuleException>(() => service.CreateAsync(
            Command(inactiveSupplier, activeProduct),
            CancellationToken.None));
        var created = await service.CreateAsync(Command(activeSupplier, inactiveProduct), CancellationToken.None);

        Assert.Equal(PurchaseRuleFailure.Conflict, inactive.Failure);
        Assert.Equal("Supplier is inactive.", inactive.Message);
        Assert.Equal(inactiveProduct, Assert.Single(created.Lines).ProductId);
    }

    [Fact]
    public async Task Receive_posts_one_purchase_movement_and_a_failed_line_rolls_back()
    {
        var purchases = _database.CreatePurchases();
        var supplier = await SeedSupplierAsync(true);
        var first = await SeedProductAsync(true);
        var second = await SeedProductAsync(true);
        var activeShelf = await SeedShelfAsync(true);
        var inactiveShelf = await SeedShelfAsync(false);
        var created = await purchases.CreateAsync(
            new SavePurchaseCommand(
                supplier,
                new DateOnly(2026, 10, 20),
                null,
                null,
                [
                    new SavePurchaseLineCommand(first, 2.000m, 1.50m),
                    new SavePurchaseLineCommand(second, 1.000m, 4.00m)
                ]),
            CancellationToken.None);
        var operationId = Guid.NewGuid();

        var failed = await Assert.ThrowsAsync<InventoryRuleException>(() => purchases.ReceiveAsync(
            created.Id,
            Receive(operationId, created.Lines[0].Id, activeShelf, created.Lines[1].Id, inactiveShelf),
            CancellationToken.None));
        await using (var connection = await _database.OpenAsync())
        {
            var status = await connection.ExecuteScalarAsync<string>(
                "select status from purchases where id = @Id;",
                new { created.Id });
            var movements = await connection.ExecuteScalarAsync<long>(
                "select count(*) from stock_movements where product_id = any(@Ids);",
                new { Ids = new[] { first, second } });
            Assert.Equal(PurchaseText.Recorded, status);
            Assert.Equal(0, movements);
        }

        var activeOnly = await SeedShelfAsync(true);
        var received = await purchases.ReceiveAsync(
            created.Id,
            Receive(operationId, created.Lines[0].Id, activeShelf, created.Lines[1].Id, activeOnly),
            CancellationToken.None);
        var replay = await purchases.ReceiveAsync(
            created.Id,
            Receive(operationId, created.Lines[0].Id, activeShelf, created.Lines[1].Id, activeOnly),
            CancellationToken.None);
        var again = await Assert.ThrowsAsync<PurchaseRuleException>(() => purchases.ReceiveAsync(
            created.Id,
            Receive(Guid.NewGuid(), created.Lines[0].Id, activeShelf, created.Lines[1].Id, activeOnly),
            CancellationToken.None));

        await using var check = await _database.OpenAsync();
        var firstBalance = await check.ExecuteScalarAsync<decimal>(
            "select quantity from stock_balances where product_id = @ProductId and shelf_id = @ShelfId;",
            new { ProductId = first, ShelfId = activeShelf });
        var movementType = await check.ExecuteScalarAsync<string>(
            "select movement_type from stock_movements where product_id = @ProductId;",
            new { ProductId = first });

        Assert.Contains("inactive", failed.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PurchaseText.Received, received.Status);
        Assert.Equal(7.00m, received.Total);
        Assert.Equal(activeShelf, received.Lines[0].ShelfId);
        Assert.Equal(received.ReceivedAt, replay.ReceivedAt);
        Assert.Equal(PurchaseRuleFailure.Conflict, again.Failure);
        Assert.Equal(2.000m, firstBalance);
        Assert.Equal("purchase", movementType);
    }

    [Fact]
    public async Task Concurrent_receive_requests_post_stock_once()
    {
        var purchases = _database.CreatePurchases();
        var supplier = await SeedSupplierAsync(true);
        var product = await SeedProductAsync(true);
        var shelf = await SeedShelfAsync(true);
        var created = await purchases.CreateAsync(Command(supplier, product, 2.000m), CancellationToken.None);
        var lineId = created.Lines[0].Id;
        var firstCommand = Receive(Guid.NewGuid(), lineId, shelf);
        var secondCommand = Receive(Guid.NewGuid(), lineId, shelf);

        await using var holder = await _database.OpenAsync();
        await using var gate = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "select id from purchases where id = @Id for update;",
            new { created.Id },
            gate);
        var first = RunOnOwnThread(() => purchases.ReceiveAsync(created.Id, firstCommand, CancellationToken.None));
        var second = RunOnOwnThread(() => purchases.ReceiveAsync(created.Id, secondCommand, CancellationToken.None));
        await WaitForWaitingSessionsAsync(holder, gate, 2);
        await gate.CommitAsync();

        var results = await Task.WhenAll(WrapAsync(first), WrapAsync(second));
        await using var connection = await _database.OpenAsync();
        var quantity = await connection.ExecuteScalarAsync<decimal>(
            "select quantity from stock_balances where product_id = @ProductId;",
            new { ProductId = product });
        var movementCount = await connection.ExecuteScalarAsync<long>(
            "select count(*) from stock_movements where product_id = @ProductId;",
            new { ProductId = product });

        Assert.Equal(1, results.Count(result => result is null));
        Assert.Equal(1, results.Count(result => result?.Failure == PurchaseRuleFailure.Conflict));
        Assert.Equal(2.000m, quantity);
        Assert.Equal(1, movementCount);
    }

    [Fact]
    public async Task Recorded_purchase_can_be_removed_and_a_supplier_with_history_cannot()
    {
        var purchases = _database.CreatePurchases();
        var suppliers = _database.CreateSuppliers();
        var supplier = await SeedSupplierAsync(true);
        var product = await SeedProductAsync(true);
        var created = await purchases.CreateAsync(Command(supplier, product), CancellationToken.None);
        var blocked = await Assert.ThrowsAsync<SupplierRuleException>(() => suppliers.DeleteAsync(supplier, CancellationToken.None));
        await purchases.DeleteAsync(created.Id, CancellationToken.None);
        var stillBlocked = await Assert.ThrowsAsync<SupplierRuleException>(() => suppliers.DeleteAsync(supplier, CancellationToken.None));
        var missing = await Assert.ThrowsAsync<PurchaseRuleException>(() => purchases.GetAsync(created.Id, CancellationToken.None));

        Assert.Equal(SupplierRuleFailure.Conflict, blocked.Failure);
        Assert.Equal(SupplierRuleFailure.Conflict, stillBlocked.Failure);
        Assert.Equal(PurchaseRuleFailure.NotFound, missing.Failure);
    }

    [Fact]
    public async Task Anonymous_callers_are_rejected_and_only_the_write_roles_can_change_a_purchase()
    {
        var cashier = await CreateUserAsync(ShopRoleNames.Cashier);
        var staff = await CreateUserAsync(ShopRoleNames.InventoryStaff);
        var manager = await CreateUserAsync(ShopRoleNames.Manager);
        using var client = _database.CreateClient();
        var cashierToken = await LoginAsync(client, cashier);
        var staffToken = await LoginAsync(client, staff);
        var managerToken = await LoginAsync(client, manager);
        var supplier = await SeedSupplierAsync(true);
        var product = await SeedProductAsync(true);
        var shelf = await SeedShelfAsync(true);
        var body = new
        {
            supplierId = supplier,
            purchaseDate = "2026-10-20",
            reference = (string?)null,
            notes = (string?)null,
            lines = new[] { new { productId = product, quantity = 1.000m, unitCost = 2.00m } }
        };

        var anonymous = await client.GetAsync("/api/purchases");
        var cashierRead = await SendAsync(client, "/api/purchases", cashierToken);
        var cashierWrite = await SendAsync(client, "/api/purchases", cashierToken, HttpMethod.Post, body);
        var staffWrite = await SendAsync(client, "/api/purchases", staffToken, HttpMethod.Post, body);
        var created = await SendAsync(client, "/api/purchases", managerToken, HttpMethod.Post, body);
        var createdBody = await created.Content.ReadFromJsonAsync<PurchaseResponse>();
        var receiveBody = new
        {
            operationId = Guid.NewGuid(),
            lines = new[] { new { lineId = createdBody!.Lines[0].Id, shelfId = shelf } }
        };
        var staffReceive = await SendAsync(
            client,
            $"/api/purchases/{createdBody.Id}/receive",
            staffToken,
            HttpMethod.Post,
            receiveBody);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cashierRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cashierWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staffWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, staffReceive.StatusCode);
    }

    private static SavePurchaseCommand Command(Guid supplierId, Guid productId, decimal quantity = 1.000m)
    {
        return new SavePurchaseCommand(
            supplierId,
            new DateOnly(2026, 10, 20),
            null,
            null,
            [new SavePurchaseLineCommand(productId, quantity, 2.00m)]);
    }

    private static ReceivePurchaseCommand Receive(Guid operationId, Guid lineId, Guid shelfId)
    {
        return new ReceivePurchaseCommand(operationId, [new ReceivePurchaseLineCommand(lineId, shelfId)]);
    }

    private static ReceivePurchaseCommand Receive(
        Guid operationId,
        Guid firstLine,
        Guid firstShelf,
        Guid secondLine,
        Guid secondShelf)
    {
        return new ReceivePurchaseCommand(
            operationId,
            [
                new ReceivePurchaseLineCommand(firstLine, firstShelf),
                new ReceivePurchaseLineCommand(secondLine, secondShelf)
            ]);
    }

    private async Task<Guid> SeedSupplierAsync(bool active)
    {
        var id = Guid.NewGuid();
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            """
            insert into suppliers (
                id, name, contact_person, phone, email, address, notes, is_active,
                created_at, updated_at, is_deleted)
            values (
                @Id, @Name, null, null, null, null, null, @Active,
                now(), now(), false);
            """,
            new { Id = id, Name = "Supplier " + id.ToString("N"), Active = active });
        return id;
    }

    private async Task<Guid> SeedProductAsync(bool active)
    {
        var unitId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            """
            insert into units (id, name, created_at, updated_at, is_deleted)
            values (@Id, @Name, now(), now(), false);
            insert into products (
                id, name, category_id, brand_id, unit_id,
                purchase_price, selling_price, minimum_stock, is_active,
                created_at, updated_at, is_deleted)
            values (
                @ProductId, @ProductName, null, null, @Id,
                1.00, 2.00, 0.000, @Active,
                now(), now(), false);
            """,
            new
            {
                Id = unitId,
                Name = "Unit " + unitId.ToString("N"),
                ProductId = productId,
                ProductName = "Product " + productId.ToString("N"),
                Active = active
            });
        return productId;
    }

    private async Task<Guid> SeedShelfAsync(bool active)
    {
        var areaId = Guid.NewGuid();
        var shelfId = Guid.NewGuid();
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            """
            insert into areas (id, name, is_active, created_at, updated_at, is_deleted)
            values (@AreaId, @AreaName, true, now(), now(), false);
            insert into shelves (id, area_id, name, is_active, created_at, updated_at, is_deleted)
            values (@ShelfId, @AreaId, @ShelfName, @Active, now(), now(), false);
            """,
            new
            {
                AreaId = areaId,
                AreaName = "Area " + areaId.ToString("N"),
                ShelfId = shelfId,
                ShelfName = "Shelf " + shelfId.ToString("N"),
                Active = active
            });
        return shelfId;
    }

    private async Task<string> CreateUserAsync(string role)
    {
        var userName = role.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant()
            + "-"
            + Guid.NewGuid().ToString("N");
        await _database.Directory.CreateAsync(
            new NewShopUser(Guid.NewGuid().ToString("D"), userName, _database.Passwords.Hash(Password), role),
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        return userName;
    }

    private static async Task<string> LoginAsync(HttpClient client, string userName)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password = Password });
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("Login did not return an access token.");
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string path,
        string token,
        HttpMethod? method = null,
        object? body = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request);
    }

    private static Task<T> RunOnOwnThread<T>(Func<Task<T>> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work().GetAwaiter().GetResult());
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        })
        {
            IsBackground = true
        };
        thread.Start();
        return completion.Task;
    }

    private static async Task WaitForWaitingSessionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int sessionCount)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        long waiting = 0;
        while (DateTime.UtcNow < deadline)
        {
            waiting = await connection.ExecuteScalarAsync<long>(
                """
                select count(distinct l.pid)
                from pg_locks l
                join pg_stat_activity a on a.pid = l.pid
                where l.granted = false
                  and a.datname = current_database();
                """,
                transaction: transaction);
            if (waiting >= sessionCount)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Expected {sessionCount} waiting sessions but found {waiting}.");
    }

    private static async Task<PurchaseRuleException?> WrapAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (PurchaseRuleException exception)
        {
            return exception;
        }
    }
}

public sealed class PurchaseDatabaseFixture : IAsyncLifetime
{
    public const string SigningKey = "retail360-purchase-test-signing-key";

    public const string Issuer = "retail360-purchase-tests";

    public const string Audience = "retail360-purchase-tests";

    private WebApplicationFactory<Program>? _factory;

    public NpgsqlConnectionFactory Connections { get; private set; } = null!;

    public ShopUserDirectory Directory { get; private set; } = null!;

    public ShopPasswordHasher Passwords { get; } = new();

    public async Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var admin = new NpgsqlConnectionStringBuilder(TestShopConnection.Read())
        {
            Database = "postgres"
        };
        await EnsureDatabaseAsync(admin.ConnectionString);
        var builder = new NpgsqlConnectionStringBuilder(TestShopConnection.Read())
        {
            Database = PurchaseDatabaseGuard.PermittedDatabase
        };
        PurchaseDatabaseGuard.Require(builder.Database);
        Connections = new NpgsqlConnectionFactory(builder.ConnectionString);
        await using (var probe = await Connections.OpenConnectionAsync(CancellationToken.None))
        {
            PurchaseDatabaseGuard.Require(await probe.ExecuteScalarAsync<string>("select current_database();"));
        }

        var runner = new SqlMigrationRunner(Connections);
        await runner.ApplyAsync(CancellationToken.None);
        await runner.ApplyAsync(CancellationToken.None);
        Directory = new ShopUserDirectory(Connections, new PostgresTransaction(Connections));
        _factory = new PurchaseApiFactory(builder.ConnectionString);
    }

    public PurchaseService CreatePurchases()
    {
        var transactions = new PostgresTransaction(Connections);
        return new PurchaseService(
            new PurchaseStore(
                Connections,
                transactions,
                new Retail360.Infrastructure.Inventory.StockLedgerStore(Connections, transactions)),
            new FixedClock(DateTimeOffset.Parse("2026-10-20T00:00:00Z")));
    }

    public SupplierService CreateSuppliers()
    {
        return new SupplierService(
            new SupplierStore(Connections, new PostgresTransaction(Connections)),
            new FixedClock(DateTimeOffset.Parse("2026-10-20T00:00:00Z")));
    }

    public HttpClient CreateClient()
    {
        return _factory!.CreateClient();
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = await Connections.OpenConnectionAsync(CancellationToken.None);
        PurchaseDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
        return connection;
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private static async Task EnsureDatabaseAsync(string adminConnectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString);
        if (!string.Equals(builder.Database, "postgres", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Database creation must use the postgres maintenance database.");
        }

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        var current = await connection.ExecuteScalarAsync<string>("select current_database();");
        if (!string.Equals(current, "postgres", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Database creation connected to an unexpected database.");
        }

        var exists = await connection.ExecuteScalarAsync<bool>(
            "select exists(select 1 from pg_database where datname = @Name);",
            new { Name = PurchaseDatabaseGuard.PermittedDatabase });
        if (exists)
        {
            return;
        }

        await connection.ExecuteAsync($"create database {PurchaseDatabaseGuard.PermittedDatabase};");
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class PurchaseApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public PurchaseApiFactory(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            PurchaseDatabaseGuard.Require(builder.Database);
            _connectionString = builder.ConnectionString;
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(Settings()));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings()));
        }

        private Dictionary<string, string?> Settings()
        {
            return new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Authentication:Issuer"] = Issuer,
                ["Authentication:Audience"] = Audience,
                ["Authentication:SigningKey"] = SigningKey,
                ["Authentication:AccessTokenMinutes"] = "15"
            };
        }
    }
}
