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
using Retail360.Application.Expenses;
using Retail360.Application.Identity;
using Retail360.Domain.Expenses;
using Retail360.Domain.Identity;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Expenses;
using Retail360.Infrastructure.Identity;
using Retail360.Infrastructure.Persistence.Sql;
using Xunit;

namespace Retail360.Tests;

[CollectionDefinition("expenses-database", DisableParallelization = true)]
public sealed class ExpenseDatabaseCollection : ICollectionFixture<ExpenseDatabaseFixture>
{
}

[Collection("expenses-database")]
public sealed class ExpenseDatabaseTests
{
    private const string Password = "correct-horse";

    private readonly ExpenseDatabaseFixture _database;

    public ExpenseDatabaseTests(ExpenseDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void Guard_rejects_shop_and_other_test_databases_without_connecting()
    {
        Assert.Equal("retail360_expenses_test", ExpenseDatabaseGuard.PermittedDatabase);
        Assert.Throws<InvalidOperationException>(() => ExpenseDatabaseGuard.Require("retail360"));
        Assert.Throws<InvalidOperationException>(() => ExpenseDatabaseGuard.Require("retail360_dev"));
        Assert.Throws<InvalidOperationException>(() => ExpenseDatabaseGuard.Require("retail360_catalog_test"));
        Assert.Throws<InvalidOperationException>(() => ExpenseDatabaseGuard.Require("retail360_inventory_test"));
        Assert.Throws<InvalidOperationException>(() => ExpenseDatabaseGuard.Require("retail360_identity_test"));
        Assert.Throws<InvalidOperationException>(() => ExpenseDatabaseGuard.Require("retail360_suppliers_test"));
        Assert.Throws<InvalidOperationException>(() => ExpenseDatabaseGuard.Require("retail360_purchasing_test"));
        Assert.Throws<InvalidOperationException>(() => ExpenseDatabaseGuard.Require("retail360_sales_test"));
    }

    [Fact]
    public async Task Migration_is_recorded_once_on_the_expenses_database()
    {
        await using var connection = await _database.OpenAsync();
        var name = await connection.ExecuteScalarAsync<string>("select current_database();");
        var recorded = await connection.ExecuteScalarAsync<long>(
            "select count(*) from schema_migrations where version in ('009_expenses', '010_expense_amount_scale');");

        Assert.Equal(ExpenseDatabaseGuard.PermittedDatabase, name);
        Assert.Equal(2, recorded);
    }

    [Fact]
    public async Task Create_stores_the_amount_and_a_replay_does_not_insert_again()
    {
        await _database.ResetAsync();
        var expenses = _database.CreateService(new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero));
        var operationId = Guid.NewGuid();
        var command = Command(operationId, ExpenseText.Rent, 12.50m, new DateOnly(2026, 10, 22), "  Shop rent ");

        var created = await expenses.CreateAsync(command, CancellationToken.None);
        var replay = await expenses.CreateAsync(command, CancellationToken.None);
        var changed = await Assert.ThrowsAsync<ExpenseRuleException>(() => expenses.CreateAsync(
            Command(operationId, ExpenseText.Rent, 13.00m, new DateOnly(2026, 10, 22), "Shop rent"),
            CancellationToken.None));
        await using var connection = await _database.OpenAsync();
        var rows = await connection.ExecuteScalarAsync<long>("select count(*) from expenses;");
        var movements = await connection.ExecuteScalarAsync<long>("select count(*) from stock_movements;");
        var sales = await connection.ExecuteScalarAsync<long>("select count(*) from sales;");
        var purchases = await connection.ExecuteScalarAsync<long>("select count(*) from purchases;");

        Assert.Equal(created.Id, replay.Id);
        Assert.Equal(12.50m, created.Amount);
        Assert.Equal("Shop rent", created.Notes);
        Assert.Equal(ExpenseText.Cash, created.PaymentMethod);
        Assert.Equal(ExpenseRuleFailure.Conflict, changed.Failure);
        Assert.Equal("This operation key was already used for a different expense.", changed.Message);
        Assert.Equal(1, rows);
        Assert.Equal(0, movements);
        Assert.Equal(0, sales);
        Assert.Equal(0, purchases);
    }

    [Fact]
    public async Task Invalid_amounts_are_rejected_and_leave_no_row()
    {
        await _database.ResetAsync();
        var expenses = _database.CreateService(DateTimeOffset.UnixEpoch);
        var zero = await Assert.ThrowsAsync<ExpenseRuleException>(() => expenses.CreateAsync(
            Command(Guid.NewGuid(), ExpenseText.Other, 0m, new DateOnly(2026, 10, 22), null),
            CancellationToken.None));
        var negative = await Assert.ThrowsAsync<ExpenseRuleException>(() => expenses.CreateAsync(
            Command(Guid.NewGuid(), ExpenseText.Other, -2m, new DateOnly(2026, 10, 22), null),
            CancellationToken.None));
        var extra = await Assert.ThrowsAsync<ExpenseRuleException>(() => expenses.CreateAsync(
            Command(Guid.NewGuid(), ExpenseText.Other, 1.235m, new DateOnly(2026, 10, 22), null),
            CancellationToken.None));
        await using var connection = await _database.OpenAsync();
        var rows = await connection.ExecuteScalarAsync<long>("select count(*) from expenses;");
        var zeroRow = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            """
            insert into expenses (
                id, operation_id, operation_fingerprint, category, amount, expense_date, notes,
                payment_method, created_at, updated_at, is_deleted)
            values (
                @Id, @OperationId, 'x', 'rent', 0, date '2026-10-22', null,
                'cash', now(), now(), false);
            """,
            new { Id = Guid.NewGuid(), OperationId = Guid.NewGuid() }));
        var extraDigits = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            """
            insert into expenses (
                id, operation_id, operation_fingerprint, category, amount, expense_date, notes,
                payment_method, created_at, updated_at, is_deleted)
            values (
                @Id, @OperationId, 'x', 'rent', 1.235, date '2026-10-22', null,
                'cash', now(), now(), false);
            """,
            new { Id = Guid.NewGuid(), OperationId = Guid.NewGuid() }));
        var rowsAfter = await connection.ExecuteScalarAsync<long>("select count(*) from expenses;");

        Assert.Equal(ExpenseRuleFailure.Invalid, zero.Failure);
        Assert.Equal(ExpenseRuleFailure.Invalid, negative.Failure);
        Assert.Equal(ExpenseRuleFailure.Invalid, extra.Failure);
        Assert.Equal(0, rows);
        Assert.Equal(0, rowsAfter);
        Assert.Equal(PostgresErrorCodes.CheckViolation, zeroRow.SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, extraDigits.SqlState);
    }

    [Fact]
    public async Task Edit_keeps_the_created_time_and_soft_delete_hides_the_expense()
    {
        await _database.ResetAsync();
        var createdAt = new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
        var expenses = _database.CreateService(createdAt);
        var created = await expenses.CreateAsync(
            Command(Guid.NewGuid(), ExpenseText.Transport, 4.00m, new DateOnly(2026, 10, 22), null),
            CancellationToken.None);
        var updated = await expenses.UpdateAsync(
            created.Id,
            new SaveExpenseCommand(ExpenseText.Repairs, 4.25m, new DateOnly(2026, 10, 23), "Door", ExpenseText.Cash),
            CancellationToken.None);
        await expenses.DeleteAsync(created.Id, CancellationToken.None);
        var missing = await Assert.ThrowsAsync<ExpenseRuleException>(() =>
            expenses.GetAsync(created.Id, CancellationToken.None));
        var listed = await expenses.ListAsync(null, null, null, 50, 0, CancellationToken.None);

        Assert.Equal(createdAt, updated.CreatedAt);
        Assert.Equal(4.25m, updated.Amount);
        Assert.Equal(ExpenseText.Repairs, updated.Category);
        Assert.Equal("Door", updated.Notes);
        Assert.Equal(ExpenseRuleFailure.NotFound, missing.Failure);
        Assert.Empty(listed.Expenses);
        Assert.Equal(0m, listed.Total);
    }

    [Fact]
    public async Task Date_and_category_filters_use_the_matching_total()
    {
        await _database.ResetAsync();
        var expenses = _database.CreateService(DateTimeOffset.UnixEpoch);
        await expenses.CreateAsync(Command(Guid.NewGuid(), ExpenseText.Rent, 10.00m, new DateOnly(2026, 10, 1), null), CancellationToken.None);
        await expenses.CreateAsync(Command(Guid.NewGuid(), ExpenseText.Rent, 2.50m, new DateOnly(2026, 10, 20), null), CancellationToken.None);
        await expenses.CreateAsync(Command(Guid.NewGuid(), ExpenseText.Wages, 5.00m, new DateOnly(2026, 10, 20), null), CancellationToken.None);

        var rent = await expenses.ListAsync(
            ExpenseText.Rent,
            new DateOnly(2026, 10, 15),
            new DateOnly(2026, 10, 31),
            50,
            0,
            CancellationToken.None);
        var month = await expenses.ListAsync(null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 50, 0, CancellationToken.None);
        var reversed = await Assert.ThrowsAsync<ExpenseRuleException>(() => expenses.ListAsync(
            null,
            new DateOnly(2026, 10, 31),
            new DateOnly(2026, 10, 1),
            50,
            0,
            CancellationToken.None));

        Assert.Equal(2.50m, rent.Total);
        Assert.Equal(ExpenseText.Rent, Assert.Single(rent.Expenses).Category);
        Assert.Equal(17.50m, month.Total);
        Assert.Equal(3, month.Expenses.Count);
        Assert.Equal(new DateOnly(2026, 10, 20), month.Expenses[0].ExpenseDate);
        Assert.Equal(ExpenseRuleFailure.Invalid, reversed.Failure);
    }

    [Fact]
    public async Task Anonymous_callers_are_rejected_and_only_admin_and_manager_can_change_an_expense()
    {
        await _database.ResetAsync();
        var cashier = await CreateUserAsync(ShopRoleNames.Cashier);
        var staff = await CreateUserAsync(ShopRoleNames.InventoryStaff);
        var manager = await CreateUserAsync(ShopRoleNames.Manager);
        using var client = _database.CreateClient();
        var cashierToken = await LoginAsync(client, cashier);
        var staffToken = await LoginAsync(client, staff);
        var managerToken = await LoginAsync(client, manager);
        var body = new
        {
            operationId = Guid.NewGuid(),
            category = ExpenseText.Supplies,
            amount = 3.25m,
            expenseDate = new DateOnly(2026, 10, 22),
            notes = (string?)null,
            paymentMethod = ExpenseText.Cash
        };

        var anonymous = await client.GetAsync("/api/expenses");
        var cashierRead = await SendAsync(client, "/api/expenses", cashierToken);
        var cashierWrite = await SendAsync(client, "/api/expenses", cashierToken, HttpMethod.Post, body);
        var staffWrite = await SendAsync(client, "/api/expenses", staffToken, HttpMethod.Post, body);
        var created = await SendAsync(client, "/api/expenses", managerToken, HttpMethod.Post, body);
        var createdBody = await created.Content.ReadFromJsonAsync<ExpenseResponse>();
        var listed = await SendAsync(client, "/api/expenses?category=supplies", cashierToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Contains("Authentication is required.", await anonymous.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, cashierRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cashierWrite.StatusCode);
        Assert.Contains(
            "You do not have access to this action.",
            await cashierWrite.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, staffWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(3.25m, createdBody!.Amount);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using var document = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        Assert.Equal(3.25m, document.RootElement.GetProperty("total").GetDecimal());
        Assert.Equal(createdBody.Id, document.RootElement.GetProperty("expenses")[0].GetProperty("id").GetGuid());
    }

    private async Task<string> CreateUserAsync(string role)
    {
        var userName = "expense-"
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

    private static CreateExpenseCommand Command(
        Guid operationId,
        string category,
        decimal amount,
        DateOnly expenseDate,
        string? notes)
    {
        return new CreateExpenseCommand(operationId, category, amount, expenseDate, notes, ExpenseText.Cash);
    }
}

public static class ExpenseDatabaseGuard
{
    public const string PermittedDatabase = "retail360_expenses_test";

    public static void Require(string? databaseName)
    {
        if (!string.Equals(databaseName, PermittedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to change a database that is not the isolated expenses test database.");
        }
    }
}

public sealed class ExpenseDatabaseFixture : IAsyncLifetime
{
    public const string SigningKey = "retail360-expense-test-signing-key";

    public const string Issuer = "retail360-expense-tests";

    public const string Audience = "retail360-expense-tests";

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
            Database = ExpenseDatabaseGuard.PermittedDatabase
        };
        ExpenseDatabaseGuard.Require(builder.Database);
        Connections = new NpgsqlConnectionFactory(builder.ConnectionString);
        await using (var probe = await Connections.OpenConnectionAsync(CancellationToken.None))
        {
            ExpenseDatabaseGuard.Require(await probe.ExecuteScalarAsync<string>("select current_database();"));
        }

        var runner = new SqlMigrationRunner(Connections);
        await runner.ApplyAsync(CancellationToken.None);
        await runner.ApplyAsync(CancellationToken.None);
        Directory = new ShopUserDirectory(Connections, new PostgresTransaction(Connections));
        _factory = new ExpenseApiFactory(builder.ConnectionString);
    }

    public ExpenseService CreateService(DateTimeOffset utcNow)
    {
        return new ExpenseService(
            new ExpenseStore(Connections, new PostgresTransaction(Connections)),
            new FixedClock(utcNow));
    }

    public HttpClient CreateClient()
    {
        return _factory!.CreateClient();
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = await Connections.OpenConnectionAsync(CancellationToken.None);
        ExpenseDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
        return connection;
    }

    public async Task ResetAsync()
    {
        await using var connection = await OpenAsync();
        ExpenseDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
        await connection.ExecuteAsync("delete from expenses;");
        ExpenseDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
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
            new { Name = ExpenseDatabaseGuard.PermittedDatabase });
        if (exists)
        {
            return;
        }

        await connection.ExecuteAsync($"create database {ExpenseDatabaseGuard.PermittedDatabase};");
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class ExpenseApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public ExpenseApiFactory(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            ExpenseDatabaseGuard.Require(builder.Database);
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
