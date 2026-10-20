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
using Retail360.Application.Inventory;
using Retail360.Application.Sales;
using Retail360.Domain.Identity;
using Retail360.Domain.Inventory;
using Retail360.Domain.Sales;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Identity;
using Retail360.Infrastructure.Inventory;
using Retail360.Infrastructure.Persistence.Sql;
using Retail360.Infrastructure.Sales;
using Xunit;

namespace Retail360.Tests;

[CollectionDefinition("sales-database", DisableParallelization = true)]
public sealed class SaleDatabaseCollection : ICollectionFixture<SaleDatabaseFixture>
{
}

[Collection("sales-database")]
public sealed class SaleDatabaseTests
{
    private const string Password = "correct-horse";

    private readonly SaleDatabaseFixture _database;

    public SaleDatabaseTests(SaleDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void Guard_rejects_shop_and_other_test_databases_without_connecting()
    {
        Assert.Throws<InvalidOperationException>(() => SaleDatabaseGuard.Require("retail360"));
        Assert.Throws<InvalidOperationException>(() => SaleDatabaseGuard.Require("retail360_dev"));
        Assert.Throws<InvalidOperationException>(() => SaleDatabaseGuard.Require("retail360_catalog_test"));
        Assert.Throws<InvalidOperationException>(() => SaleDatabaseGuard.Require("retail360_inventory_test"));
        Assert.Throws<InvalidOperationException>(() => SaleDatabaseGuard.Require("retail360_identity_test"));
        Assert.Throws<InvalidOperationException>(() => SaleDatabaseGuard.Require("retail360_suppliers_test"));
        Assert.Throws<InvalidOperationException>(() => SaleDatabaseGuard.Require("retail360_purchasing_test"));
        SaleDatabaseGuard.Require(SaleDatabaseGuard.PermittedDatabase);
    }

    [Fact]
    public async Task Migration_is_recorded_once_on_the_sales_database()
    {
        await using var connection = await _database.OpenAsync();
        var name = await connection.ExecuteScalarAsync<string>("select current_database();");
        var versions = (await connection.QueryAsync<string>(
            "select version from schema_migrations order by version;")).ToArray();
        var rows = await connection.ExecuteScalarAsync<long>(
            "select count(*) from schema_migrations where version = '008_sales';");

        Assert.Equal(SaleDatabaseGuard.PermittedDatabase, name);
        Assert.Contains("008_sales", versions);
        Assert.Equal(1, rows);
        Assert.Equal(versions.Distinct(StringComparer.Ordinal).Count(), versions.Length);
    }

    [Fact]
    public async Task Complete_copies_the_catalog_price_and_a_replay_does_not_sell_again()
    {
        var sales = _database.CreateSales();
        var product = await SeedProductAsync("Tea", 0.10m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 5.000m);
        var operationId = Guid.NewGuid();
        var command = Command(operationId, product, shelf, 1.250m);

        var created = await sales.CompleteAsync(command, CancellationToken.None);
        var replay = await sales.CompleteAsync(command, CancellationToken.None);
        var changed = await Assert.ThrowsAsync<SaleRuleException>(() => sales.CompleteAsync(
            Command(operationId, product, shelf, 1.000m),
            CancellationToken.None));
        await using var connection = await _database.OpenAsync();
        var quantity = await connection.ExecuteScalarAsync<decimal>(
            "select quantity from stock_balances where product_id = @ProductId and shelf_id = @ShelfId;",
            new { ProductId = product, ShelfId = shelf });
        var movementType = await connection.ExecuteScalarAsync<string>(
            "select movement_type from stock_movements where product_id = @ProductId and movement_type = 'sale';",
            new { ProductId = product });

        Assert.Equal(0.13m, created.Total);
        Assert.Equal(0.13m, Assert.Single(created.Lines).LineTotal);
        Assert.Equal(0.10m, created.Lines[0].UnitPrice);
        Assert.Equal("Tea", created.Lines[0].ProductName);
        Assert.Equal(SaleText.Cash, created.PaymentMethod);
        Assert.Equal(created.Id, replay.Id);
        Assert.Equal(created.ReceiptNumber, replay.ReceiptNumber);
        Assert.Equal(SaleRuleFailure.Conflict, changed.Failure);
        Assert.Equal("This operation key was already used for a different stock change.", changed.Message);
        Assert.Equal(3.750m, quantity);
        Assert.Equal("sale", movementType);
    }

    [Fact]
    public async Task Inactive_product_is_rejected_and_an_inactive_shelf_can_be_sold()
    {
        var sales = _database.CreateSales();
        var inactive = await SeedProductAsync("Hidden", 2.00m, false);
        var offered = await SeedProductAsync("Offered", 2.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(offered, shelf, 2.000m);
        await using (var connection = await _database.OpenAsync())
        {
            await connection.ExecuteAsync("update shelves set is_active = false where id = @Id;", new { Id = shelf });
        }
        var removed = await SeedProductAsync("Gone", 1.00m, true);
        await using (var connection = await _database.OpenAsync())
        {
            await connection.ExecuteAsync(
                "update products set is_deleted = true where id = @Id;",
                new { Id = removed });
        }

        var hidden = await Assert.ThrowsAsync<SaleRuleException>(() => sales.CompleteAsync(
            Command(Guid.NewGuid(), inactive, shelf, 1m),
            CancellationToken.None));
        var deleted = await Assert.ThrowsAsync<SaleRuleException>(() => sales.CompleteAsync(
            Command(Guid.NewGuid(), removed, shelf, 1m),
            CancellationToken.None));
        var sold = await sales.CompleteAsync(Command(Guid.NewGuid(), offered, shelf, 1m), CancellationToken.None);

        Assert.Equal(SaleRuleFailure.Conflict, hidden.Failure);
        Assert.Equal("Product is inactive.", hidden.Message);
        Assert.Equal(SaleRuleFailure.NotFound, deleted.Failure);
        Assert.Equal(shelf, sold.Lines[0].ShelfId);
    }

    [Fact]
    public async Task Missing_product_and_insufficient_stock_leave_no_sale()
    {
        var sales = _database.CreateSales();
        var product = await SeedProductAsync("Rice", 2.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 1.000m);

        var missing = await Assert.ThrowsAsync<SaleRuleException>(() => sales.CompleteAsync(
            Command(Guid.NewGuid(), Guid.NewGuid(), shelf, 1m),
            CancellationToken.None));
        var shortStock = await Assert.ThrowsAsync<InventoryRuleException>(() => sales.CompleteAsync(
            Command(Guid.NewGuid(), product, shelf, 2m),
            CancellationToken.None));
        await using var connection = await _database.OpenAsync();
        var salesForProduct = await connection.ExecuteScalarAsync<long>(
            "select count(*) from sale_lines where product_id = @ProductId;",
            new { ProductId = product });
        var quantity = await connection.ExecuteScalarAsync<decimal>(
            "select quantity from stock_balances where product_id = @ProductId;",
            new { ProductId = product });

        Assert.Equal(SaleRuleFailure.NotFound, missing.Failure);
        Assert.Equal(InventoryRuleFailure.Conflict, shortStock.Failure);
        Assert.Equal("Shelf does not have enough stock.", shortStock.Message);
        Assert.Equal(0, salesForProduct);
        Assert.Equal(1.000m, quantity);
    }

    [Fact]
    public async Task A_later_catalog_edit_does_not_rewrite_the_receipt()
    {
        var sales = _database.CreateSales();
        var product = await SeedProductAsync("Old Tea", 3.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 2.000m);
        var created = await sales.CompleteAsync(Command(Guid.NewGuid(), product, shelf, 1m), CancellationToken.None);
        await using (var connection = await _database.OpenAsync())
        {
            await connection.ExecuteAsync(
                "update products set name = 'New Tea', selling_price = 9.00 where id = @Id;",
                new { Id = product });
        }

        var receipt = await sales.GetAsync(created.Id, CancellationToken.None);
        var listed = await sales.ListAsync(created.ReceiptNumber, 50, 0, CancellationToken.None);

        Assert.Equal("Old Tea", receipt.Lines[0].ProductName);
        Assert.Equal(3.00m, receipt.Lines[0].UnitPrice);
        Assert.Equal(created.Id, Assert.Single(listed).Id);
    }

    [Fact]
    public async Task Concurrent_sales_of_the_last_unit_post_stock_once()
    {
        var sales = _database.CreateSales();
        var product = await SeedProductAsync("Last", 1.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 1.000m);
        var firstCommand = Command(Guid.NewGuid(), product, shelf, 1m);
        var secondCommand = Command(Guid.NewGuid(), product, shelf, 1m);

        await using var holder = await _database.OpenAsync();
        await using var gate = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync("select id from products where id = @Id for update;", new { Id = product }, gate);
        var first = sales.CompleteAsync(firstCommand, CancellationToken.None);
        var second = sales.CompleteAsync(secondCommand, CancellationToken.None);
        await WaitForWaitingSessionsAsync(2, first, second);
        await gate.CommitAsync();

        var results = await Task.WhenAll(WrapAsync(first), WrapAsync(second));
        await using var connection = await _database.OpenAsync();
        var quantity = await connection.ExecuteScalarAsync<decimal>(
            "select quantity from stock_balances where product_id = @ProductId;",
            new { ProductId = product });
        var saleMovements = await connection.ExecuteScalarAsync<long>(
            "select count(*) from stock_movements where product_id = @ProductId and movement_type = 'sale';",
            new { ProductId = product });

        Assert.Equal(1, results.Count(result => result is null));
        Assert.Equal(1, results.Count(result => result is InventoryRuleException));
        Assert.Equal(0.000m, quantity);
        Assert.Equal(1, saleMovements);
    }

    [Fact]
    public async Task Anonymous_callers_are_rejected_and_inventory_staff_cannot_sell()
    {
        var cashier = await CreateUserAsync(ShopRoleNames.Cashier);
        var staff = await CreateUserAsync(ShopRoleNames.InventoryStaff);
        using var client = _database.CreateClient();
        var cashierToken = await LoginAsync(client, cashier);
        var staffToken = await LoginAsync(client, staff);
        var product = await SeedProductAsync("Counter", 2.00m, true);
        var shelf = await SeedShelfAsync(true);
        await OpenAsync(product, shelf, 3.000m);
        var body = new
        {
            operationId = Guid.NewGuid(),
            lines = new[] { new { productId = product, shelfId = shelf, quantity = 1.000m } }
        };

        var anonymous = await client.GetAsync("/api/sales");
        var cashierRead = await SendAsync(client, "/api/sales", cashierToken);
        var staffSale = await SendAsync(client, "/api/sales", staffToken, HttpMethod.Post, body);
        var cashierSale = await SendAsync(client, "/api/sales", cashierToken, HttpMethod.Post, body);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cashierRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staffSale.StatusCode);
        Assert.Equal(HttpStatusCode.Created, cashierSale.StatusCode);
    }

    private async Task<Guid> SeedProductAsync(string name, decimal sellingPrice, bool active)
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
                1.00, @SellingPrice, 0.000, @Active,
                now(), now(), false);
            """,
            new
            {
                Id = unitId,
                Name = "Unit " + unitId.ToString("N"),
                ProductId = productId,
                ProductName = name,
                SellingPrice = sellingPrice,
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

    private async Task OpenAsync(Guid productId, Guid shelfId, decimal quantity)
    {
        var transactions = new PostgresTransaction(_database.Connections);
        var ledger = new StockLedgerStore(_database.Connections, transactions);
        await ledger.PostAsync(
            new StockPosting(
                Guid.NewGuid(),
                "opening|" + Guid.NewGuid().ToString("N"),
                productId,
                [new StockLine(shelfId, quantity, StockDirection.In, StockMovementType.Opening, "Opening count", null)],
                DateTimeOffset.Parse("2026-10-20T00:00:00Z")),
            CancellationToken.None);
    }

    private static CompleteSaleCommand Command(Guid operationId, Guid productId, Guid shelfId, decimal quantity)
    {
        return new CompleteSaleCommand(operationId, [new CompleteSaleLineCommand(productId, shelfId, quantity)]);
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

    private async Task WaitForWaitingSessionsAsync(int sessionCount, params Task[] workers)
    {
        await using var watcher = await _database.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        long waiting = 0;
        while (DateTime.UtcNow < deadline)
        {
            var faulted = workers.FirstOrDefault(worker => worker.IsFaulted);
            if (faulted is not null)
            {
                await faulted;
            }

            waiting = await watcher.ExecuteScalarAsync<long>(
                """
                select count(*)
                from pg_stat_activity
                where datname = current_database()
                  and pid <> pg_backend_pid()
                  and wait_event_type = 'Lock';
                """);
            if (waiting >= sessionCount)
            {
                return;
            }

            await Task.Delay(50);
        }

        var activity = (await watcher.QueryAsync<LockWaitRow>(
            """
            select a.pid, a.state, a.wait_event_type, a.wait_event, left(coalesce(a.query, ''), 160) as query
            from pg_stat_activity a
            where a.datname = current_database()
              and a.pid <> pg_backend_pid();
            """)).ToArray();
        var detail = string.Join(
            " | ",
            activity.Select(row => $"{row.Pid} {row.State} {row.WaitEventType}/{row.WaitEvent} {row.Query}"));
        var states = string.Join(", ", workers.Select(worker => worker.Status.ToString()));
        throw new TimeoutException(
            $"Expected {sessionCount} waiting sessions but found {waiting}. Task states: {states}. {detail}");
    }

    private sealed class LockWaitRow
    {
        public int Pid { get; init; }

        public string State { get; init; } = string.Empty;

        public string? WaitEventType { get; init; }

        public string? WaitEvent { get; init; }

        public string Query { get; init; } = string.Empty;
    }

    private static async Task<Exception?> WrapAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception exception) when (exception is InventoryRuleException or SaleRuleException)
        {
            return exception;
        }
    }
}

public sealed class SaleDatabaseFixture : IAsyncLifetime
{
    public const string SigningKey = "retail360-sales-test-signing-key";

    public const string Issuer = "retail360-sales-tests";

    public const string Audience = "retail360-sales-tests";

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
            Database = SaleDatabaseGuard.PermittedDatabase
        };
        SaleDatabaseGuard.Require(builder.Database);
        Connections = new NpgsqlConnectionFactory(builder.ConnectionString);
        await using (var probe = await Connections.OpenConnectionAsync(CancellationToken.None))
        {
            SaleDatabaseGuard.Require(await probe.ExecuteScalarAsync<string>("select current_database();"));
        }

        var runner = new SqlMigrationRunner(Connections);
        await runner.ApplyAsync(CancellationToken.None);
        await runner.ApplyAsync(CancellationToken.None);
        Directory = new ShopUserDirectory(Connections, new PostgresTransaction(Connections));
        _factory = new SaleApiFactory(builder.ConnectionString);
    }

    public SaleService CreateSales()
    {
        var transactions = new PostgresTransaction(Connections);
        return new SaleService(
            new SaleStore(Connections, transactions, new StockLedgerStore(Connections, transactions)),
            new FixedClock(DateTimeOffset.Parse("2026-10-20T00:00:00Z")));
    }

    public HttpClient CreateClient()
    {
        return _factory!.CreateClient();
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = await Connections.OpenConnectionAsync(CancellationToken.None);
        SaleDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
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
            new { Name = SaleDatabaseGuard.PermittedDatabase });
        if (exists)
        {
            return;
        }

        await connection.ExecuteAsync($"create database {SaleDatabaseGuard.PermittedDatabase};");
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class SaleApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public SaleApiFactory(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            SaleDatabaseGuard.Require(builder.Database);
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
