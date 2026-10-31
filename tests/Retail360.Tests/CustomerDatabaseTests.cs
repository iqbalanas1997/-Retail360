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
using Retail360.Application.Customers;
using Retail360.Application.Identity;
using Retail360.Application.Sales;
using Retail360.Domain.Customers;
using Retail360.Domain.Identity;
using Retail360.Infrastructure.Customers;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Identity;
using Retail360.Infrastructure.Inventory;
using Retail360.Infrastructure.Persistence.Sql;
using Retail360.Infrastructure.Sales;
using Xunit;

namespace Retail360.Tests;

public class CustomerServiceTests
{
    [Fact]
    public async Task Blank_name_does_not_reach_the_store()
    {
        var store = new CountingStore();
        var service = new CustomerService(store, new EmptySales(), new FixedClock(DateTimeOffset.UnixEpoch));

        var error = await Assert.ThrowsAsync<CustomerRuleException>(() => service.CreateAsync(
            new SaveCustomerCommand("   ", "0300", null, null, "note"),
            CancellationToken.None));

        Assert.Equal(CustomerRuleFailure.Invalid, error.Failure);
        Assert.Equal(0, store.Adds);
    }

    private sealed class CountingStore : ICustomerStore
    {
        public int Adds { get; private set; }

        public Task<Customer?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult<Customer?>(null);
        }

        public Task<IReadOnlyList<Customer>> ListAsync(
            string? search,
            bool activeOnly,
            int limit,
            int offset,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<Customer>>([]);
        }

        public Task AddAsync(Customer customer, CancellationToken cancellationToken)
        {
            Adds++;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Customer customer, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class EmptySales : ISaleStore
    {
        public Task<SaleResponse?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult<SaleResponse?>(null);
        }

        public Task<IReadOnlyList<SaleResponse>> ListAsync(
            int? receiptNumber,
            int limit,
            int offset,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SaleResponse>>([]);
        }

        public Task<IReadOnlyList<CustomerSaleSummary>> ListForCustomerAsync(
            Guid customerId,
            int limit,
            int offset,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CustomerSaleSummary>>([]);
        }

        public Task<SaleResponse> CompleteAsync(
            Guid operationId,
            string fingerprint,
            IReadOnlyList<CompleteSaleLineCommand> lines,
            Guid? customerId,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}

[CollectionDefinition("customers-database", DisableParallelization = true)]
public sealed class CustomerDatabaseCollection : ICollectionFixture<CustomerDatabaseFixture>
{
}

[Collection("customers-database")]
public sealed class CustomerDatabaseTests
{
    private const string Password = "correct-horse";

    private readonly CustomerDatabaseFixture _database;

    public CustomerDatabaseTests(CustomerDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void Guard_rejects_other_databases_without_connecting()
    {
        Assert.Equal("retail360_customers_test", CustomerDatabaseGuard.PermittedDatabase);
        Assert.Throws<InvalidOperationException>(() => CustomerDatabaseGuard.Require("retail360"));
        Assert.Throws<InvalidOperationException>(() => CustomerDatabaseGuard.Require("retail360_dev"));
        Assert.Throws<InvalidOperationException>(() => CustomerDatabaseGuard.Require("retail360_sales_test"));
        CustomerDatabaseGuard.Require(CustomerDatabaseGuard.PermittedDatabase);
    }

    [Fact]
    public async Task Migration_is_recorded_once_and_keeps_customer_optional_on_a_sale()
    {
        await _database.Runner.ApplyAsync(CancellationToken.None);
        await using var connection = await _database.OpenAsync();
        var name = await connection.ExecuteScalarAsync<string>("select current_database();");
        var versions = (await connection.QueryAsync<string>(
            "select version from schema_migrations where version = '013_customers';")).ToArray();
        var nullable = await connection.ExecuteScalarAsync<string>(
            """
            select is_nullable
            from information_schema.columns
            where table_name = 'sales' and column_name = 'customer_id';
            """);

        Assert.Equal(CustomerDatabaseGuard.PermittedDatabase, name);
        Assert.Equal(["013_customers"], versions);
        Assert.Equal("YES", nullable);
    }

    [Fact]
    public async Task Database_rejects_a_blank_name_and_a_bad_email()
    {
        await using var connection = await _database.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var blank = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            """
            insert into customers (
                id, name, phone, email, address, notes, is_active, created_at, updated_at, is_deleted)
            values (
                @Id, ' ', null, null, null, null, true, now(), now(), false);
            """,
            new { Id = Guid.NewGuid() },
            transaction));

        Assert.Equal(PostgresErrorCodes.CheckViolation, blank.SqlState);
        await transaction.RollbackAsync();

        await using var emailTransaction = await connection.BeginTransactionAsync();
        var email = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            """
            insert into customers (
                id, name, phone, email, address, notes, is_active, created_at, updated_at, is_deleted)
            values (
                @Id, 'Amina', null, 'not-an-email', null, null, true, now(), now(), false);
            """,
            new { Id = Guid.NewGuid() },
            emailTransaction));
        Assert.Equal(PostgresErrorCodes.CheckViolation, email.SqlState);
        await emailTransaction.RollbackAsync();
    }

    [Fact]
    public async Task Customers_can_share_a_name_and_search_pages_and_hides_deleted_rows()
    {
        var service = _database.CreateService(DateTimeOffset.Parse("2026-10-31T12:00:00Z"));
        var marker = Guid.NewGuid().ToString("N");
        var first = await service.CreateAsync(
            new SaveCustomerCommand($"{marker} Ali", "0300111", "ali-a@example.com", null, "door code"),
            CancellationToken.None);
        var second = await service.CreateAsync(
            new SaveCustomerCommand($"{marker} Ali", null, null, "Lane 2", null),
            CancellationToken.None);
        var third = await service.CreateAsync(
            new SaveCustomerCommand($"{marker} Sara", "0300333", "sara@example.com", null, null),
            CancellationToken.None);
        var blank = await Assert.ThrowsAsync<CustomerRuleException>(() => service.CreateAsync(
            new SaveCustomerCommand("  ", "0300", null, null, null),
            CancellationToken.None));
        var email = await Assert.ThrowsAsync<CustomerRuleException>(() => service.CreateAsync(
            new SaveCustomerCommand($"{marker} Bad", null, "not-an-email", null, null),
            CancellationToken.None));

        var byName = await service.ListAsync(marker + " Ali", false, 50, 0, CancellationToken.None);
        var byPhone = await service.ListAsync("0300111", false, 50, 0, CancellationToken.None);
        var byEmail = await service.ListAsync("sara@example.com", false, 50, 0, CancellationToken.None);
        var page = await service.ListAsync(marker, false, 1, 1, CancellationToken.None);
        var edited = await service.UpdateAsync(
            first.Id,
            new SaveCustomerCommand($"{marker} Ali Khan", "0300222", null, "Lane 9", null),
            CancellationToken.None);
        var deactivated = await service.DeactivateAsync(second.Id, CancellationToken.None);
        var active = await service.ListAsync(marker, true, 50, 0, CancellationToken.None);
        var activated = await service.ActivateAsync(second.Id, CancellationToken.None);
        await service.DeleteAsync(third.Id, CancellationToken.None);
        var afterDelete = await service.ListAsync(marker + " Sara", false, 50, 0, CancellationToken.None);
        var missing = await Assert.ThrowsAsync<CustomerRuleException>(() =>
            service.GetAsync(third.Id, CancellationToken.None));
        var history = await service.ListSalesAsync(first.Id, 50, 0, CancellationToken.None);
        await using var connection = await _database.OpenAsync();
        var details = await connection.ExecuteScalarAsync<string>(
            """
            select coalesce(string_agg(details::text, ' '), '')
            from audit_entries
            where entity_id = @Id;
            """,
            new { Id = first.Id.ToString("D") });
        var failedAudits = await connection.ExecuteScalarAsync<long>(
            "select count(*) from audit_entries where entity_id = @Id;",
            new { Id = third.Id.ToString("D") });
        var actions = (await connection.QueryAsync<string>(
            "select action from audit_entries where entity_id = @Id order by occurred_at, action;",
            new { Id = second.Id.ToString("D") })).ToArray();

        Assert.Equal(2, byName.Count);
        Assert.Equal(first.Id, Assert.Single(byPhone).Id);
        Assert.Equal(third.Id, Assert.Single(byEmail).Id);
        Assert.Single(page);
        Assert.Equal(CustomerRuleFailure.Invalid, blank.Failure);
        Assert.Equal(CustomerRuleFailure.Invalid, email.Failure);
        Assert.Equal("0300222", edited.Phone);
        Assert.Null(edited.Email);
        Assert.True(edited.IsActive);
        Assert.False(deactivated.IsActive);
        Assert.DoesNotContain(active, customer => customer.Id == second.Id);
        Assert.True(activated.IsActive);
        Assert.Empty(afterDelete);
        Assert.Equal(CustomerRuleFailure.NotFound, missing.Failure);
        Assert.Empty(history);
        Assert.Contains($"{marker} Ali Khan", details, StringComparison.Ordinal);
        Assert.DoesNotContain("0300222", details, StringComparison.Ordinal);
        Assert.DoesNotContain("door code", details, StringComparison.Ordinal);
        Assert.Equal(2, failedAudits);
        Assert.Equal(
            ["customer.activated", "customer.created", "customer.deactivated"],
            actions.OrderBy(action => action, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Cashier_can_search_without_notes_and_inventory_staff_cannot()
    {
        var service = _database.CreateService(DateTimeOffset.Parse("2026-10-31T12:00:00Z"));
        var created = await service.CreateAsync(
            new SaveCustomerCommand("Counter " + Guid.NewGuid().ToString("N"), "0300", null, "Lane 4", "private note"),
            CancellationToken.None);
        var cashier = await CreateUserAsync(ShopRoleNames.Cashier);
        var staff = await CreateUserAsync(ShopRoleNames.InventoryStaff);
        var manager = await CreateUserAsync(ShopRoleNames.Manager);
        using var client = _database.CreateClient();
        var cashierToken = await LoginAsync(client, cashier);
        var staffToken = await LoginAsync(client, staff);
        var managerToken = await LoginAsync(client, manager);

        var anonymous = await client.GetAsync("/api/customers");
        var cashierRead = await SendAsync(client, $"/api/customers/{created.Id}", cashierToken);
        var cashierBody = await cashierRead.Content.ReadFromJsonAsync<CustomerResponse>();
        var cashierWrite = await SendAsync(
            client,
            "/api/customers",
            cashierToken,
            HttpMethod.Post,
            new { name = "Blocked" });
        var staffRead = await SendAsync(client, "/api/customers", staffToken);
        var managerWrite = await SendAsync(
            client,
            "/api/customers",
            managerToken,
            HttpMethod.Post,
            new { name = "Manager " + Guid.NewGuid().ToString("N"), phone = " ", email = " " });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cashierRead.StatusCode);
        Assert.Equal(created.Name, cashierBody!.Name);
        Assert.Equal("0300", cashierBody.Phone);
        Assert.Null(cashierBody.Address);
        Assert.Null(cashierBody.Notes);
        Assert.Equal(HttpStatusCode.Forbidden, cashierWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staffRead.StatusCode);
        Assert.Contains(
            "You do not have access to this action.",
            await staffRead.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Created, managerWrite.StatusCode);
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
}

public static class CustomerDatabaseGuard
{
    public const string PermittedDatabase = "retail360_customers_test";

    public static void Require(string? databaseName)
    {
        if (!string.Equals(databaseName, PermittedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to change a database that is not the isolated customers test database.");
        }
    }
}

public sealed class CustomerDatabaseFixture : IAsyncLifetime
{
    public const string SigningKey = "retail360-customer-test-signing-key";

    public const string Issuer = "retail360-customer-tests";

    public const string Audience = "retail360-customer-tests";

    private WebApplicationFactory<Program>? _factory;

    public NpgsqlConnectionFactory Connections { get; private set; } = null!;

    public SqlMigrationRunner Runner { get; private set; } = null!;

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
            Database = CustomerDatabaseGuard.PermittedDatabase
        };
        CustomerDatabaseGuard.Require(builder.Database);
        Connections = new NpgsqlConnectionFactory(builder.ConnectionString);
        await using (var probe = await Connections.OpenConnectionAsync(CancellationToken.None))
        {
            CustomerDatabaseGuard.Require(await probe.ExecuteScalarAsync<string>("select current_database();"));
        }

        Runner = new SqlMigrationRunner(Connections);
        await Runner.ApplyAsync(CancellationToken.None);
        await Runner.ApplyAsync(CancellationToken.None);
        Directory = new ShopUserDirectory(Connections, new PostgresTransaction(Connections));
        _factory = new CustomerApiFactory(builder.ConnectionString);
    }

    public CustomerService CreateService(DateTimeOffset utcNow)
    {
        var transactions = new PostgresTransaction(Connections);
        return new CustomerService(
            new CustomerStore(Connections, transactions),
            new SaleStore(Connections, transactions, new StockLedgerStore(Connections, transactions)),
            new FixedClock(utcNow));
    }

    public HttpClient CreateClient()
    {
        return _factory!.CreateClient();
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = await Connections.OpenConnectionAsync(CancellationToken.None);
        CustomerDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
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
            new { Name = CustomerDatabaseGuard.PermittedDatabase });
        if (exists)
        {
            return;
        }

        await connection.ExecuteAsync($"create database {CustomerDatabaseGuard.PermittedDatabase};");
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class CustomerApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public CustomerApiFactory(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            CustomerDatabaseGuard.Require(builder.Database);
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
