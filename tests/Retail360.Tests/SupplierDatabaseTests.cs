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
using Retail360.Application.Suppliers;
using Retail360.Domain.Identity;
using Retail360.Domain.Suppliers;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Identity;
using Retail360.Infrastructure.Persistence.Sql;
using Retail360.Infrastructure.Suppliers;
using Xunit;

namespace Retail360.Tests;

public class SupplierServiceTests
{
    [Fact]
    public async Task Invalid_name_does_not_reach_the_store()
    {
        var store = new CountingStore();
        var service = new SupplierService(store, new FixedClock(DateTimeOffset.UnixEpoch));

        var error = await Assert.ThrowsAsync<SupplierRuleException>(() => service.CreateAsync(
            new SaveSupplierCommand(" ", null, null, null, null, null),
            CancellationToken.None));

        Assert.Equal(SupplierRuleFailure.Invalid, error.Failure);
        Assert.Equal(0, store.Adds);
    }

    private sealed class CountingStore : ISupplierStore
    {
        public int Adds { get; private set; }

        public Task<Supplier?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult<Supplier?>(null);
        }

        public Task<Supplier?> FindByNameAsync(string name, CancellationToken cancellationToken)
        {
            return Task.FromResult<Supplier?>(null);
        }

        public Task<IReadOnlyList<Supplier>> ListAsync(
            string? name,
            bool activeOnly,
            int limit,
            int offset,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<Supplier>>([]);
        }

        public Task AddAsync(Supplier supplier, CancellationToken cancellationToken)
        {
            Adds++;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Supplier supplier, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
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

[CollectionDefinition("suppliers-database", DisableParallelization = true)]
public sealed class SupplierDatabaseCollection : ICollectionFixture<SupplierDatabaseFixture>
{
}

[Collection("suppliers-database")]
public sealed class SupplierDatabaseTests
{
    private const string Password = "correct-horse";

    private readonly SupplierDatabaseFixture _database;

    public SupplierDatabaseTests(SupplierDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void Guard_rejects_other_databases_without_connecting()
    {
        Assert.Equal("retail360_suppliers_test", SupplierDatabaseGuard.PermittedDatabase);
        Assert.Throws<InvalidOperationException>(() => SupplierDatabaseGuard.Require("retail360"));
        Assert.Throws<InvalidOperationException>(() => SupplierDatabaseGuard.Require("retail360_catalog_test"));
        Assert.Throws<InvalidOperationException>(() => SupplierDatabaseGuard.Require("retail360_inventory_test"));
        Assert.Throws<InvalidOperationException>(() => SupplierDatabaseGuard.Require("retail360_identity_test"));
        SupplierDatabaseGuard.Require(SupplierDatabaseGuard.PermittedDatabase);
    }

    [Fact]
    public async Task Migration_is_recorded_once_on_the_suppliers_database()
    {
        await _database.Runner.ApplyAsync(CancellationToken.None);
        await using var connection = await _database.OpenAsync();
        var name = await connection.ExecuteScalarAsync<string>("select current_database();");
        var versions = (await connection.QueryAsync<string>(
            "select version from schema_migrations order by version;")).ToArray();
        var supplierRows = await connection.ExecuteScalarAsync<long>(
            "select count(*) from schema_migrations where version = '006_suppliers';");

        Assert.Equal(SupplierDatabaseGuard.PermittedDatabase, name);
        Assert.Contains("006_suppliers", versions);
        Assert.Equal(1, supplierRows);
        Assert.Equal(versions.Distinct(StringComparer.Ordinal).Count(), versions.Length);
    }

    [Fact]
    public async Task Database_rejects_a_blank_name_and_a_bad_email()
    {
        await using var connection = await _database.OpenAsync();
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            var blank = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                InsertSql,
                Sample(Guid.NewGuid(), " ", "shop@example.com"),
                transaction));
            Assert.Equal(PostgresErrorCodes.CheckViolation, blank.SqlState);
            await transaction.RollbackAsync();
        }

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            var email = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                InsertSql,
                Sample(Guid.NewGuid(), "Sufi", "not-an-email"),
                transaction));
            Assert.Equal(PostgresErrorCodes.CheckViolation, email.SqlState);
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Database_rejects_a_case_insensitive_duplicate_and_allows_the_name_after_delete()
    {
        await using var connection = await _database.OpenAsync();
        var name = "Index " + Guid.NewGuid().ToString("N");
        var first = Guid.NewGuid();
        await connection.ExecuteAsync(InsertSql, Sample(first, name, null));
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync(InsertSql, Sample(Guid.NewGuid(), name.ToUpperInvariant(), null)));
        await connection.ExecuteAsync(
            "update suppliers set is_deleted = true, updated_at = now() where id = @Id;",
            new { Id = first });
        await connection.ExecuteAsync(InsertSql, Sample(Guid.NewGuid(), name, null));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
    }

    [Fact]
    public async Task Supplier_can_be_created_searched_updated_and_removed()
    {
        var service = _database.CreateService(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));
        var marker = Guid.NewGuid().ToString("N");
        var created = await service.CreateAsync(
            new SaveSupplierCommand($"Alpha {marker}", "Amina", "0300-1111111", "alpha@example.com", "Karachi", "Rice"),
            CancellationToken.None);
        var other = await service.CreateAsync(
            new SaveSupplierCommand($"Beta {marker}", null, null, null, null, null),
            CancellationToken.None);

        var found = await service.ListAsync(marker, false, 50, 0, CancellationToken.None);
        var active = await service.ListAsync(marker, true, 50, 0, CancellationToken.None);
        var page = await service.ListAsync(marker, false, 1, 1, CancellationToken.None);
        var read = await service.GetAsync(created.Id, CancellationToken.None);
        var duplicate = await Assert.ThrowsAsync<SupplierRuleException>(() => service.CreateAsync(
            new SaveSupplierCommand($"alpha {marker}", null, null, null, null, null),
            CancellationToken.None));

        var deactivated = await service.DeactivateAsync(created.Id, CancellationToken.None);
        var edited = await service.UpdateAsync(
            created.Id,
            new SaveSupplierCommand($"Alpha {marker}", "Amina Khan", "0300-2222222", null, null, null),
            CancellationToken.None);
        var afterDeactivate = await service.ListAsync(marker, true, 50, 0, CancellationToken.None);
        var activated = await service.ActivateAsync(created.Id, CancellationToken.None);
        await service.DeleteAsync(other.Id, CancellationToken.None);
        var missing = await Assert.ThrowsAsync<SupplierRuleException>(() =>
            service.GetAsync(other.Id, CancellationToken.None));
        var reused = await service.CreateAsync(
            new SaveSupplierCommand($"Beta {marker}", null, null, null, null, null),
            CancellationToken.None);
        var limit = await Assert.ThrowsAsync<SupplierRuleException>(() =>
            service.ListAsync(null, false, 201, 0, CancellationToken.None));

        Assert.Equal(2, found.Count);
        Assert.Equal(2, active.Count);
        Assert.Single(page);
        Assert.Equal("alpha@example.com", read.Email);
        Assert.Equal(SupplierRuleFailure.Conflict, duplicate.Failure);
        Assert.False(deactivated.IsActive);
        Assert.False(edited.IsActive);
        Assert.Equal("Amina Khan", edited.ContactPerson);
        Assert.Equal("0300-2222222", edited.Phone);
        Assert.Null(edited.Email);
        Assert.DoesNotContain(afterDeactivate, supplier => supplier.Id == created.Id);
        Assert.True(activated.IsActive);
        Assert.Equal(SupplierRuleFailure.NotFound, missing.Failure);
        Assert.NotEqual(other.Id, reused.Id);
        Assert.Equal(SupplierRuleFailure.Invalid, limit.Failure);
    }

    [Fact]
    public async Task Anonymous_callers_cannot_read_suppliers()
    {
        using var client = _database.CreateClient();
        var response = await client.GetAsync("/api/suppliers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Authentication is required.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_admin_and_manager_can_change_suppliers()
    {
        var cashier = await CreateUserAsync(ShopRoleNames.Cashier);
        var staff = await CreateUserAsync(ShopRoleNames.InventoryStaff);
        var manager = await CreateUserAsync(ShopRoleNames.Manager);
        var admin = await CreateUserAsync(ShopRoleNames.Admin);
        using var client = _database.CreateClient();
        var cashierToken = await LoginAsync(client, cashier);
        var staffToken = await LoginAsync(client, staff);
        var managerToken = await LoginAsync(client, manager);
        var adminToken = await LoginAsync(client, admin);
        var name = "Http " + Guid.NewGuid().ToString("N");

        var cashierRead = await SendAsync(client, "/api/suppliers", cashierToken);
        var cashierWrite = await SendAsync(client, "/api/suppliers", cashierToken, HttpMethod.Post, Body(name));
        var staffWrite = await SendAsync(client, "/api/suppliers", staffToken, HttpMethod.Post, Body(name));
        var created = await SendAsync(client, "/api/suppliers", managerToken, HttpMethod.Post, Body(name));
        var createdBody = await created.Content.ReadFromJsonAsync<SupplierResponse>();
        var deactivated = await SendAsync(
            client,
            $"/api/suppliers/{createdBody!.Id}/deactivate",
            adminToken,
            HttpMethod.Post);

        Assert.Equal(HttpStatusCode.OK, cashierRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cashierWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staffWrite.StatusCode);
        Assert.Contains(
            "You do not have access to this action.",
            await cashierWrite.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
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

    private static SaveSupplierCommand Body(string name)
    {
        return new SaveSupplierCommand(name, null, null, "shop@example.com", null, null);
    }

    private const string InsertSql = """
        insert into suppliers (
            id, name, contact_person, phone, email, address, notes, is_active,
            created_at, updated_at, is_deleted)
        values (
            @Id, @Name, null, null, @Email, null, null, true,
            now(), now(), false);
        """;

    private static object Sample(Guid id, string name, string? email)
    {
        return new { Id = id, Name = name, Email = email };
    }
}

public sealed class SupplierDatabaseFixture : IAsyncLifetime
{
    public const string SigningKey = "retail360-supplier-test-signing-key";

    public const string Issuer = "retail360-supplier-tests";

    public const string Audience = "retail360-supplier-tests";

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
            Database = SupplierDatabaseGuard.PermittedDatabase
        };
        SupplierDatabaseGuard.Require(builder.Database);
        Connections = new NpgsqlConnectionFactory(builder.ConnectionString);
        await using (var probe = await Connections.OpenConnectionAsync(CancellationToken.None))
        {
            SupplierDatabaseGuard.Require(await probe.ExecuteScalarAsync<string>("select current_database();"));
        }

        Runner = new SqlMigrationRunner(Connections);
        await Runner.ApplyAsync(CancellationToken.None);
        await Runner.ApplyAsync(CancellationToken.None);
        Directory = new ShopUserDirectory(Connections, new PostgresTransaction(Connections));
        _factory = new SupplierApiFactory(builder.ConnectionString);
    }

    public SupplierService CreateService(DateTimeOffset utcNow)
    {
        return new SupplierService(new SupplierStore(Connections), new FixedClock(utcNow));
    }

    public HttpClient CreateClient()
    {
        return _factory!.CreateClient();
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = await Connections.OpenConnectionAsync(CancellationToken.None);
        SupplierDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
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
            new { Name = SupplierDatabaseGuard.PermittedDatabase });
        if (exists)
        {
            return;
        }

        await connection.ExecuteAsync($"create database {SupplierDatabaseGuard.PermittedDatabase};");
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class SupplierApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public SupplierApiFactory(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            SupplierDatabaseGuard.Require(builder.Database);
            _connectionString = builder.ConnectionString;
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Host settings become command-line arguments and outrank appsettings and
            // environment variables, so this host cannot open retail360 or another test database.
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
