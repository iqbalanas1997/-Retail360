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
using Retail360.Application.Identity;
using Retail360.Application.Reporting;
using Retail360.Domain.Identity;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Identity;
using Retail360.Infrastructure.Persistence.Sql;
using Retail360.Infrastructure.Reporting;
using Xunit;

namespace Retail360.Tests;

[CollectionDefinition("reports-database", DisableParallelization = true)]
public sealed class ReportDatabaseCollection : ICollectionFixture<ReportDatabaseFixture>
{
}

[Collection("reports-database")]
public sealed class ReportDatabaseTests
{
    private const string Password = "correct-horse";

    private readonly ReportDatabaseFixture _database;

    public ReportDatabaseTests(ReportDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void Guard_rejects_shop_and_other_test_databases_without_connecting()
    {
        Assert.Equal("retail360_reports_test", ReportDatabaseGuard.PermittedDatabase);
        Assert.Throws<InvalidOperationException>(() => ReportDatabaseGuard.Require("retail360"));
        Assert.Throws<InvalidOperationException>(() => ReportDatabaseGuard.Require("retail360_dev"));
        Assert.Throws<InvalidOperationException>(() => ReportDatabaseGuard.Require("retail360_catalog_test"));
        Assert.Throws<InvalidOperationException>(() => ReportDatabaseGuard.Require("retail360_inventory_test"));
        Assert.Throws<InvalidOperationException>(() => ReportDatabaseGuard.Require("retail360_identity_test"));
        Assert.Throws<InvalidOperationException>(() => ReportDatabaseGuard.Require("retail360_suppliers_test"));
        Assert.Throws<InvalidOperationException>(() => ReportDatabaseGuard.Require("retail360_purchasing_test"));
        Assert.Throws<InvalidOperationException>(() => ReportDatabaseGuard.Require("retail360_sales_test"));
        Assert.Throws<InvalidOperationException>(() => ReportDatabaseGuard.Require("retail360_expenses_test"));
    }

    [Fact]
    public async Task Sales_use_persisted_totals_and_the_utc_calendar_date()
    {
        await _database.ResetAsync();
        await InsertSaleAsync(1, 10.10m, new DateTimeOffset(2026, 10, 22, 0, 30, 0, TimeSpan.Zero));
        await InsertSaleAsync(2, 0.20m, new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));
        await InsertSaleAsync(3, 99.00m, new DateTimeOffset(2026, 10, 21, 23, 30, 0, TimeSpan.Zero));
        await InsertSaleAsync(4, 10.00m, new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));
        await InsertSaleAsync(5, 0.01m, new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
        var reports = new ReportStore(_database.Connections);

        var day = await reports.DashboardAsync(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22), CancellationToken.None);
        var rounded = await reports.DashboardAsync(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 20), CancellationToken.None);
        var empty = await reports.DashboardAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), CancellationToken.None);
        await using var connection = await _database.OpenAsync();
        var movements = await connection.ExecuteScalarAsync<long>("select count(*) from stock_movements;");

        Assert.Equal(2, day.Sales.Count);
        Assert.Equal(10.30m, day.Sales.Total);
        Assert.Equal(5.15m, day.Sales.Average);
        Assert.Equal(2, day.RecentSales.Count);
        Assert.Equal(2, day.RecentSales[0].ReceiptNumber);
        Assert.Equal(1, day.RecentSales[1].ReceiptNumber);
        Assert.Equal(5.01m, rounded.Sales.Average);
        Assert.Equal(0, empty.Sales.Count);
        Assert.Equal(0m, empty.Sales.Total);
        Assert.Null(empty.Sales.Average);
        Assert.Empty(empty.RecentSales);
        Assert.Equal(0, movements);
    }

    [Fact]
    public async Task Recent_sales_stay_bounded()
    {
        await _database.ResetAsync();
        for (var receipt = 1; receipt <= 9; receipt++)
        {
            await InsertSaleAsync(receipt, 1.00m, new DateTimeOffset(2026, 10, 22, receipt, 0, 0, TimeSpan.Zero));
        }

        var reports = new ReportStore(_database.Connections);
        var day = await reports.DashboardAsync(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22), CancellationToken.None);

        Assert.Equal(9, day.Sales.Count);
        Assert.Equal(9.00m, day.Sales.Total);
        Assert.Equal(8, day.RecentSales.Count);
        Assert.Equal(9, day.RecentSales[0].ReceiptNumber);
        Assert.DoesNotContain(day.RecentSales, sale => sale.ReceiptNumber == 1);
    }

    [Fact]
    public async Task Expenses_exclude_soft_deleted_rows_and_keep_every_category()
    {
        await _database.ResetAsync();
        var rent = await InsertExpenseAsync("rent", 2.50m, new DateOnly(2026, 10, 22), false);
        await InsertExpenseAsync("rent", 1.25m, new DateOnly(2026, 10, 22), true);
        var utilities = await InsertExpenseAsync("utilities", 4.00m, new DateOnly(2026, 10, 21), false);
        var reports = new ReportStore(_database.Connections);

        var day = await reports.DashboardAsync(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22), CancellationToken.None);
        var both = await reports.DashboardAsync(new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 22), CancellationToken.None);

        Assert.Equal(1, day.Expenses.Count);
        Assert.Equal(2.50m, day.Expenses.Total);
        Assert.Equal(2.50m, Category(day, "rent").Total);
        Assert.Equal(0m, Category(day, "utilities").Total);
        Assert.Equal(7, day.Expenses.Categories.Count);
        Assert.Equal(2, both.Expenses.Count);
        Assert.Equal(6.50m, both.Expenses.Total);
        Assert.Equal(rent, both.RecentExpenses[0].Id);
        Assert.Equal(new DateOnly(2026, 10, 22), both.RecentExpenses[0].ExpenseDate);
        Assert.Equal(utilities, both.RecentExpenses[1].Id);
        Assert.Equal(4.00m, both.RecentExpenses[1].Amount);
    }

    [Fact]
    public async Task Purchases_keep_recorded_dates_separate_from_received_stock()
    {
        await _database.ResetAsync();
        var supplier = await InsertSupplierAsync();
        await InsertPurchaseAsync(supplier, new DateOnly(2026, 10, 22), 20.00m, recorded: true, deleted: false, receivedAt: null);
        await InsertPurchaseAsync(supplier, new DateOnly(2026, 10, 22), 5.00m, recorded: true, deleted: true, receivedAt: null);
        await InsertPurchaseAsync(
            supplier,
            new DateOnly(2026, 10, 20),
            30.00m,
            recorded: false,
            deleted: false,
            receivedAt: new DateTimeOffset(2026, 10, 22, 1, 0, 0, TimeSpan.Zero));
        await InsertPurchaseAsync(
            supplier,
            new DateOnly(2026, 10, 22),
            7.00m,
            recorded: false,
            deleted: false,
            receivedAt: new DateTimeOffset(2026, 10, 21, 23, 0, 0, TimeSpan.Zero));
        var reports = new ReportStore(_database.Connections);

        var recordedDay = await reports.DashboardAsync(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22), CancellationToken.None);
        var receivedDay = await reports.DashboardAsync(new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 21), CancellationToken.None);
        await using var connection = await _database.OpenAsync();
        var movements = await connection.ExecuteScalarAsync<long>("select count(*) from stock_movements;");

        Assert.Equal(2, recordedDay.Purchases.RecordedCount);
        Assert.Equal(27.00m, recordedDay.Purchases.RecordedTotal);
        Assert.Equal(1, recordedDay.Purchases.ReceivedCount);
        Assert.Equal(30.00m, recordedDay.Purchases.ReceivedTotal);
        Assert.Equal(0, receivedDay.Purchases.RecordedCount);
        Assert.Equal(1, receivedDay.Purchases.ReceivedCount);
        Assert.Equal(7.00m, receivedDay.Purchases.ReceivedTotal);
        Assert.Equal(0, movements);
    }

    [Fact]
    public async Task Low_stock_sums_every_shelf_and_skips_inactive_or_deleted_products()
    {
        await _database.ResetAsync();
        var unit = await InsertUnitAsync();
        var area = Guid.NewGuid();
        var openShelf = Guid.NewGuid();
        var closedShelf = Guid.NewGuid();
        await using (var connection = await _database.OpenAsync())
        {
            await connection.ExecuteAsync(
                """
                insert into areas (id, name, is_active, created_at, updated_at, is_deleted)
                values (@Id, @Name, true, now(), now(), false);
                insert into shelves (id, area_id, name, is_active, created_at, updated_at, is_deleted)
                values (@OpenShelf, @Area, 'Open', true, now(), now(), false),
                       (@ClosedShelf, @Area, 'Closed', false, now(), now(), false);
                """,
                new { Id = area, Name = "Shop " + area.ToString("N"), Area = area, OpenShelf = openShelf, ClosedShelf = closedShelf });
        }

        var apple = await InsertProductAsync(unit, "Apple", 2.000m, active: true, deleted: false);
        var rice = await InsertProductAsync(unit, "Rice", 3.000m, active: true, deleted: false);
        var oil = await InsertProductAsync(unit, "Oil", 3.000m, active: true, deleted: false);
        var salt = await InsertProductAsync(unit, "Salt", 1.000m, active: true, deleted: false);
        var soap = await InsertProductAsync(unit, "Soap", 5.000m, active: false, deleted: false);
        var old = await InsertProductAsync(unit, "Old", 5.000m, active: true, deleted: true);
        await InsertBalanceAsync(apple, openShelf, 1.000m);
        await InsertBalanceAsync(rice, openShelf, 1.000m);
        await InsertBalanceAsync(rice, closedShelf, 2.000m);
        await InsertBalanceAsync(oil, openShelf, 2.000m);
        await InsertBalanceAsync(oil, closedShelf, 2.000m);
        await InsertBalanceAsync(soap, openShelf, 1.000m);
        await InsertBalanceAsync(old, openShelf, 1.000m);
        var reports = new ReportStore(_database.Connections);

        var stock = await reports.LowStockAsync(10, CancellationToken.None);
        var limited = await reports.LowStockAsync(1, CancellationToken.None);

        Assert.Equal(2, stock.Count);
        Assert.Equal(apple, stock.Products[0].Id);
        Assert.Equal(1.000m, stock.Products[0].OnHand);
        Assert.Equal(rice, stock.Products[1].Id);
        Assert.Equal(3.000m, stock.Products[1].OnHand);
        Assert.Equal(3.000m, stock.Products[1].MinimumStock);
        Assert.DoesNotContain(stock.Products, product => product.Id == oil || product.Id == salt || product.Id == soap || product.Id == old);
        Assert.Equal(2, limited.Count);
        Assert.Equal(apple, Assert.Single(limited.Products).Id);
    }

    [Fact]
    public async Task Empty_periods_are_zero_and_authorization_rejects_other_roles()
    {
        await _database.ResetAsync();
        var cashier = await CreateUserAsync(ShopRoleNames.Cashier);
        var staff = await CreateUserAsync(ShopRoleNames.InventoryStaff);
        var manager = await CreateUserAsync(ShopRoleNames.Manager);
        using var client = _database.CreateClient();
        var cashierToken = await LoginAsync(client, cashier);
        var staffToken = await LoginAsync(client, staff);
        var managerToken = await LoginAsync(client, manager);

        var anonymous = await client.GetAsync("/api/reports/dashboard?from=2026-01-01&to=2026-01-02");
        var reversed = await SendAsync(client, "/api/reports/dashboard?from=2026-10-22&to=2026-10-21", managerToken);
        var missing = await SendAsync(client, "/api/reports/dashboard", managerToken);
        var badLimit = await SendAsync(client, "/api/reports/low-stock?limit=0", managerToken);
        var cashierDashboard = await SendAsync(client, "/api/reports/dashboard?from=2026-01-01&to=2026-01-02", cashierToken);
        var staffStock = await SendAsync(client, "/api/reports/low-stock", staffToken);
        var dashboard = await SendAsync(client, "/api/reports/dashboard?from=2026-01-01&to=2026-01-02", managerToken);
        var stock = await SendAsync(client, "/api/reports/low-stock", managerToken);
        var empty = await dashboard.Content.ReadFromJsonAsync<DashboardResponse>();
        var low = await stock.Content.ReadFromJsonAsync<LowStockReportResponse>();
        using var reversedBody = JsonDocument.Parse(await reversed.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Contains("Authentication is required.", await anonymous.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal("The start date must be on or before the end date.", reversedBody.RootElement.GetProperty("error").GetString());
        Assert.Equal("error", Assert.Single(reversedBody.RootElement.EnumerateObject()).Name);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("Start date and end date are required.", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, badLimit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cashierDashboard.StatusCode);
        Assert.Contains("You do not have access to this action.", await cashierDashboard.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, staffStock.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Equal(0, empty!.Sales.Count);
        Assert.Equal(0m, empty.Sales.Total);
        Assert.Null(empty.Sales.Average);
        Assert.Equal(0, empty.Expenses.Count);
        Assert.Equal(0m, empty.Expenses.Total);
        Assert.Equal(0, empty.Purchases.RecordedCount);
        Assert.Equal(0m, empty.Purchases.ReceivedTotal);
        Assert.Equal(HttpStatusCode.OK, stock.StatusCode);
        Assert.Equal(0, low!.Count);
        Assert.Empty(low.Products);
    }

    private static ExpenseCategoryTotal Category(DashboardResponse report, string category)
    {
        return Assert.Single(report.Expenses.Categories, item => item.Category == category);
    }

    private async Task InsertSaleAsync(int receiptNumber, decimal total, DateTimeOffset soldAt)
    {
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            """
            insert into sales (
                id, receipt_number, operation_id, operation_fingerprint, sold_at, payment_method, total,
                created_at, updated_at, is_deleted)
            values (
                @Id, @ReceiptNumber, @OperationId, 'report', @SoldAt, 'cash', @Total,
                @SoldAt, @SoldAt, false);
            """,
            new { Id = Guid.NewGuid(), ReceiptNumber = receiptNumber, OperationId = Guid.NewGuid(), SoldAt = soldAt, Total = total });
    }

    private async Task<Guid> InsertExpenseAsync(string category, decimal amount, DateOnly date, bool deleted)
    {
        var id = Guid.NewGuid();
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            """
            insert into expenses (
                id, operation_id, operation_fingerprint, category, amount, expense_date, notes,
                payment_method, created_at, updated_at, is_deleted)
            values (
                @Id, @OperationId, 'report', @Category, @Amount, @ExpenseDate, null,
                'cash', now(), now(), @Deleted);
            """,
            new
            {
                Id = id,
                OperationId = Guid.NewGuid(),
                Category = category,
                Amount = amount,
                ExpenseDate = date.ToDateTime(TimeOnly.MinValue),
                Deleted = deleted
            });
        return id;
    }

    private async Task<Guid> InsertSupplierAsync()
    {
        var id = Guid.NewGuid();
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            """
            insert into suppliers (
                id, name, contact_person, phone, email, address, notes, is_active, created_at, updated_at, is_deleted)
            values (@Id, @Name, null, null, null, null, null, true, now(), now(), false);
            """,
            new { Id = id, Name = "Supplier " + id.ToString("N") });
        return id;
    }

    private async Task InsertPurchaseAsync(
        Guid supplierId,
        DateOnly purchaseDate,
        decimal total,
        bool recorded,
        bool deleted,
        DateTimeOffset? receivedAt)
    {
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            """
            insert into purchases (
                id, supplier_id, purchase_date, reference, notes, status, total,
                receive_operation_id, receive_fingerprint, received_at, created_at, updated_at, is_deleted)
            values (
                @Id, @SupplierId, @PurchaseDate, null, null, @Status, @Total,
                @OperationId, @Fingerprint, @ReceivedAt, now(), now(), @Deleted);
            """,
            new
            {
                Id = Guid.NewGuid(),
                SupplierId = supplierId,
                PurchaseDate = purchaseDate.ToDateTime(TimeOnly.MinValue),
                Status = recorded ? "recorded" : "received",
                Total = total,
                OperationId = recorded ? (Guid?)null : Guid.NewGuid(),
                Fingerprint = recorded ? null : "report",
                ReceivedAt = receivedAt,
                Deleted = deleted
            });
    }

    private async Task<Guid> InsertUnitAsync()
    {
        var id = Guid.NewGuid();
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            """
            insert into units (id, name, created_at, updated_at, is_deleted)
            values (@Id, @Name, now(), now(), false);
            """,
            new { Id = id, Name = "Piece " + id.ToString("N") });
        return id;
    }

    private async Task<Guid> InsertProductAsync(Guid unitId, string name, decimal minimumStock, bool active, bool deleted)
    {
        var id = Guid.NewGuid();
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            """
            insert into products (
                id, name, category_id, brand_id, unit_id, purchase_price, selling_price, minimum_stock,
                is_active, created_at, updated_at, is_deleted)
            values (
                @Id, @Name, null, null, @UnitId, 1.00, 2.00, @MinimumStock,
                @Active, now(), now(), @Deleted);
            """,
            new { Id = id, Name = name + " " + id.ToString("N"), UnitId = unitId, MinimumStock = minimumStock, Active = active, Deleted = deleted });
        return id;
    }

    private async Task InsertBalanceAsync(Guid productId, Guid shelfId, decimal quantity)
    {
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            """
            insert into stock_balances (id, product_id, shelf_id, quantity, created_at, updated_at)
            values (@Id, @ProductId, @ShelfId, @Quantity, now(), now());
            """,
            new { Id = Guid.NewGuid(), ProductId = productId, ShelfId = shelfId, Quantity = quantity });
    }

    private async Task<string> CreateUserAsync(string role)
    {
        var userName = "report-"
            + role.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant()
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

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }
}

public static class ReportDatabaseGuard
{
    public const string PermittedDatabase = "retail360_reports_test";

    public static void Require(string? databaseName)
    {
        if (!string.Equals(databaseName, PermittedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to change a database that is not the isolated reports test database.");
        }
    }
}

public sealed class ReportDatabaseFixture : IAsyncLifetime
{
    public const string SigningKey = "retail360-report-test-signing-key";

    public const string Issuer = "retail360-report-tests";

    public const string Audience = "retail360-report-tests";

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
            Database = ReportDatabaseGuard.PermittedDatabase
        };
        ReportDatabaseGuard.Require(builder.Database);
        Connections = new NpgsqlConnectionFactory(builder.ConnectionString);
        await using (var probe = await Connections.OpenConnectionAsync(CancellationToken.None))
        {
            ReportDatabaseGuard.Require(await probe.ExecuteScalarAsync<string>("select current_database();"));
        }

        var runner = new SqlMigrationRunner(Connections);
        await runner.ApplyAsync(CancellationToken.None);
        await runner.ApplyAsync(CancellationToken.None);
        Directory = new ShopUserDirectory(Connections, new PostgresTransaction(Connections));
        _factory = new ReportApiFactory(builder.ConnectionString);
    }

    public HttpClient CreateClient()
    {
        return _factory!.CreateClient();
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = await Connections.OpenConnectionAsync(CancellationToken.None);
        ReportDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
        return connection;
    }

    public async Task ResetAsync()
    {
        await using var connection = await OpenAsync();
        ReportDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
        await connection.ExecuteAsync(
            """
            delete from stock_balances;
            delete from sales;
            delete from purchases;
            delete from expenses;
            delete from products;
            delete from shelves;
            delete from areas;
            delete from suppliers;
            delete from units;
            """);
        ReportDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
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
            new { Name = ReportDatabaseGuard.PermittedDatabase });
        if (exists)
        {
            return;
        }

        await connection.ExecuteAsync($"create database {ReportDatabaseGuard.PermittedDatabase};");
    }

    private sealed class ReportApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public ReportApiFactory(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            ReportDatabaseGuard.Require(builder.Database);
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
