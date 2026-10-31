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
using Retail360.Domain.Identity;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Identity;
using Retail360.Infrastructure.Persistence.Sql;
using Xunit;

namespace Retail360.Tests;

[CollectionDefinition("users-database", DisableParallelization = true)]
public sealed class UserDatabaseCollection : ICollectionFixture<UserDatabaseFixture>
{
}

[Collection("users-database")]
public sealed class UserDatabaseTests
{
    private const string Password = "correct-horse";

    private readonly UserDatabaseFixture _database;

    public UserDatabaseTests(UserDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void Guard_rejects_shop_and_other_test_databases_without_connecting()
    {
        Assert.Equal("retail360_users_test", UserDatabaseGuard.PermittedDatabase);
        Assert.Throws<InvalidOperationException>(() => UserDatabaseGuard.Require("retail360"));
        Assert.Throws<InvalidOperationException>(() => UserDatabaseGuard.Require("retail360_dev"));
        Assert.Throws<InvalidOperationException>(() => UserDatabaseGuard.Require("retail360_catalog_test"));
        Assert.Throws<InvalidOperationException>(() => UserDatabaseGuard.Require("retail360_inventory_test"));
        Assert.Throws<InvalidOperationException>(() => UserDatabaseGuard.Require("retail360_identity_test"));
        Assert.Throws<InvalidOperationException>(() => UserDatabaseGuard.Require("retail360_suppliers_test"));
        Assert.Throws<InvalidOperationException>(() => UserDatabaseGuard.Require("retail360_purchasing_test"));
        Assert.Throws<InvalidOperationException>(() => UserDatabaseGuard.Require("retail360_sales_test"));
        Assert.Throws<InvalidOperationException>(() => UserDatabaseGuard.Require("retail360_expenses_test"));
        Assert.Throws<InvalidOperationException>(() => UserDatabaseGuard.Require("retail360_reports_test"));
        Assert.Throws<InvalidOperationException>(() => UserDatabaseGuard.Require("retail360_audit_test"));
        Assert.Throws<InvalidOperationException>(() => UserDatabaseGuard.Require("retail360_customers_test"));
    }

    [Fact]
    public async Task Active_flag_migration_is_recorded_once()
    {
        await using var connection = await _database.OpenAsync();
        var versions = await connection.QueryAsync<string>(
            "select version from schema_migrations where version = '011_user_active';");
        var column = await connection.QuerySingleAsync(
            """
            select is_nullable, column_default
            from information_schema.columns
            where table_schema = 'public'
              and table_name = 'asp_net_users'
              and column_name = 'is_active';
            """);

        Assert.Equal(new[] { "011_user_active" }, versions);
        Assert.Equal("NO", (string)column.is_nullable);
        Assert.Contains("true", (string)column.column_default, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_can_create_search_and_assign_a_role()
    {
        await _database.ResetAsync();
        var adminId = await _database.CreateUserAsync("owner", ShopRoleNames.Admin);
        using var client = _database.CreateClient();
        var adminToken = await LoginAsync(client, "owner", Password);
        var created = await SendAsync(client, HttpMethod.Post, "/api/users", adminToken, new
        {
            userName = "  counter  ",
            role = ShopRoleNames.Cashier,
            password = Password
        });
        var createdBody = await created.Content.ReadAsStringAsync();
        using var createdJson = JsonDocument.Parse(createdBody);
        var cashierId = createdJson.RootElement.GetProperty("id").GetString();

        var list = await SendAsync(client, HttpMethod.Get, "/api/users?name=count&activeOnly=false&limit=50&offset=0", adminToken);
        var listBody = await list.Content.ReadAsStringAsync();
        var cashierToken = await LoginAsync(client, "counter", Password);
        var denied = await SendAsync(client, HttpMethod.Get, "/api/users", cashierToken);
        var products = await SendAsync(client, HttpMethod.Get, "/api/products?limit=1&offset=0", cashierToken);
        await using var connection = await _database.OpenAsync();
        var hash = await connection.ExecuteScalarAsync<string>(
            "select password_hash from asp_net_users where id = @Id;",
            new { Id = cashierId });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("counter", createdJson.RootElement.GetProperty("userName").GetString());
        Assert.Equal(ShopRoleNames.Cashier, createdJson.RootElement.GetProperty("role").GetString());
        Assert.True(createdJson.RootElement.GetProperty("isActive").GetBoolean());
        Assert.False(createdJson.RootElement.TryGetProperty("password", out _));
        Assert.False(createdJson.RootElement.TryGetProperty("passwordHash", out _));
        Assert.False(string.IsNullOrEmpty(hash));
        Assert.DoesNotContain(hash!, createdBody, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, createdBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains("counter", listBody, StringComparison.Ordinal);
        Assert.DoesNotContain(hash, listBody, StringComparison.Ordinal);
        Assert.DoesNotContain("owner", listBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
        Assert.Equal(adminId, await UserIdAsync(client, "owner", Password));
    }

    [Fact]
    public async Task Invalid_input_and_duplicate_names_are_rejected()
    {
        await _database.ResetAsync();
        await _database.CreateUserAsync("owner", ShopRoleNames.Admin);
        using var client = _database.CreateClient();
        var token = await LoginAsync(client, "owner", Password);

        var blank = await SendAsync(client, HttpMethod.Post, "/api/users", token, new
        {
            userName = "   ",
            role = ShopRoleNames.Manager,
            password = Password
        });
        var shortPassword = await SendAsync(client, HttpMethod.Post, "/api/users", token, new
        {
            userName = "stock",
            role = ShopRoleNames.InventoryStaff,
            password = "short"
        });
        var unknownRole = await SendAsync(client, HttpMethod.Post, "/api/users", token, new
        {
            userName = "stock",
            role = "Owner",
            password = Password
        });
        var missing = await SendAsync(client, HttpMethod.Post, "/api/users", token, null);
        var limit = await SendAsync(client, HttpMethod.Get, "/api/users?limit=0", token);
        var created = await SendAsync(client, HttpMethod.Post, "/api/users", token, new
        {
            userName = "Cash Desk",
            role = ShopRoleNames.Manager,
            password = Password
        });
        var duplicate = await SendAsync(client, HttpMethod.Post, "/api/users", token, new
        {
            userName = "cash desk",
            role = ShopRoleNames.Cashier,
            password = Password
        });
        var absent = await SendAsync(client, HttpMethod.Get, "/api/users/missing-user", token);
        var anonymous = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal("User name must be between 1 and 256 characters.", await ErrorAsync(blank));
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
        Assert.Equal("Password must be between 8 and 128 characters.", await ErrorAsync(shortPassword));
        Assert.Equal(HttpStatusCode.BadRequest, unknownRole.StatusCode);
        Assert.Equal("Role must be Admin, Manager, Cashier, or Inventory Staff.", await ErrorAsync(unknownRole));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("Request body is required.", await ErrorAsync(missing));
        Assert.Equal(HttpStatusCode.BadRequest, limit.StatusCode);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("A user with this name already exists.", await ErrorAsync(duplicate));
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal("User was not found.", await ErrorAsync(absent));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Self_deactivation_and_the_last_admin_are_protected()
    {
        await _database.ResetAsync();
        var ownerId = await _database.CreateUserAsync("owner", ShopRoleNames.Admin);
        using var client = _database.CreateClient();
        var ownerToken = await LoginAsync(client, "owner", Password);

        var selfDeactivate = await SendAsync(client, HttpMethod.Post, $"/api/users/{ownerId}/deactivate", ownerToken);
        var selfRemove = await SendAsync(client, HttpMethod.Delete, $"/api/users/{ownerId}", ownerToken);
        var demote = await SendAsync(client, HttpMethod.Put, $"/api/users/{ownerId}", ownerToken, new
        {
            userName = "owner",
            role = ShopRoleNames.Cashier,
            password = ""
        });
        var store = new UserAccountStore(_database.Connections, new PostgresTransaction(_database.Connections));
        var deactivateLast = await Assert.ThrowsAsync<UserRuleException>(() => store.SetActiveAsync(
            "other-admin",
            ownerId,
            false,
            DateTimeOffset.UtcNow,
            CancellationToken.None));
        var removeLast = await Assert.ThrowsAsync<UserRuleException>(() => store.RemoveAsync(
            "other-admin",
            ownerId,
            DateTimeOffset.UtcNow,
            CancellationToken.None));
        var second = await SendAsync(client, HttpMethod.Post, "/api/users", ownerToken, new
        {
            userName = "relief",
            role = ShopRoleNames.Admin,
            password = Password
        });
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var reliefId = secondJson.RootElement.GetProperty("id").GetString()!;
        var reliefToken = await LoginAsync(client, "relief", Password);
        var deactivateRelief = await SendAsync(client, HttpMethod.Post, $"/api/users/{reliefId}/deactivate", ownerToken);
        var reliefAfter = await SendAsync(client, HttpMethod.Get, "/api/users", reliefToken);
        var reliefLogin = await client.PostAsJsonAsync("/api/auth/login", new { userName = "relief", password = Password });
        var stillLast = await SendAsync(client, HttpMethod.Post, $"/api/users/{ownerId}/deactivate", reliefToken);
        var activateRelief = await SendAsync(client, HttpMethod.Post, $"/api/users/{reliefId}/activate", ownerToken);
        var reliefAgain = await LoginAsync(client, "relief", Password);
        var deactivateOwner = await SendAsync(client, HttpMethod.Post, $"/api/users/{ownerId}/deactivate", reliefAgain);
        var ownerAfter = await SendAsync(client, HttpMethod.Get, "/api/products?limit=1&offset=0", ownerToken);
        var ownerLogin = await client.PostAsJsonAsync("/api/auth/login", new { userName = "owner", password = Password });

        Assert.Equal(HttpStatusCode.Conflict, selfDeactivate.StatusCode);
        Assert.Equal("You cannot deactivate your own account.", await ErrorAsync(selfDeactivate));
        Assert.Equal(HttpStatusCode.Conflict, selfRemove.StatusCode);
        Assert.Equal("You cannot remove your own account.", await ErrorAsync(selfRemove));
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal("The last Admin cannot be given another role.", await ErrorAsync(demote));
        Assert.Equal("The last Admin cannot be deactivated.", deactivateLast.Message);
        Assert.Equal("The last Admin cannot be removed.", removeLast.Message);
        Assert.Equal(HttpStatusCode.OK, deactivateRelief.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reliefAfter.StatusCode);
        Assert.Equal("Authentication is required.", await ErrorAsync(reliefAfter));
        Assert.Equal(HttpStatusCode.Unauthorized, reliefLogin.StatusCode);
        Assert.Equal("The user name or password is incorrect.", await ErrorAsync(reliefLogin));
        Assert.Equal(HttpStatusCode.Unauthorized, stillLast.StatusCode);
        Assert.Equal(HttpStatusCode.OK, activateRelief.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deactivateOwner.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ownerAfter.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ownerLogin.StatusCode);
        Assert.Equal("The user name or password is incorrect.", await ErrorAsync(ownerLogin));
    }

    [Fact]
    public async Task Removal_hides_the_account_and_keeps_the_name_taken()
    {
        await _database.ResetAsync();
        await _database.CreateUserAsync("owner", ShopRoleNames.Admin);
        using var client = _database.CreateClient();
        var token = await LoginAsync(client, "owner", Password);
        var created = await SendAsync(client, HttpMethod.Post, "/api/users", token, new
        {
            userName = "a_b",
            role = ShopRoleNames.InventoryStaff,
            password = Password
        });
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var userId = createdJson.RootElement.GetProperty("id").GetString()!;
        await SendAsync(client, HttpMethod.Post, "/api/users", token, new
        {
            userName = "axb",
            role = ShopRoleNames.Cashier,
            password = Password
        });

        var staffToken = await LoginAsync(client, "a_b", Password);
        var staffDenied = await SendAsync(client, HttpMethod.Get, "/api/users", staffToken);
        var search = await SendAsync(client, HttpMethod.Get, "/api/users?name=" + Uri.EscapeDataString("a_b"), token);
        var removed = await SendAsync(client, HttpMethod.Delete, $"/api/users/{userId}", token);
        var list = await SendAsync(client, HttpMethod.Get, "/api/users?name=a", token);
        var reused = await SendAsync(client, HttpMethod.Post, "/api/users", token, new
        {
            userName = "A_B",
            role = ShopRoleNames.Cashier,
            password = Password
        });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { userName = "a_b", password = Password });
        await using var connection = await _database.OpenAsync();
        var deleted = await connection.ExecuteScalarAsync<bool>(
            "select is_deleted from asp_net_users where id = @Id;",
            new { Id = userId });
        var roleCount = await connection.ExecuteScalarAsync<int>("select count(*) from asp_net_roles where is_deleted = false;");

        Assert.Equal(HttpStatusCode.Forbidden, staffDenied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var searchJson = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        Assert.Equal(1, searchJson.RootElement.GetArrayLength());
        Assert.Equal("a_b", searchJson.RootElement[0].GetProperty("userName").GetString());
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain("a_b", await list.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.True(deleted);
        Assert.Equal(4, roleCount);
    }

    [Fact]
    public async Task Password_and_role_can_change_without_returning_the_hash()
    {
        await _database.ResetAsync();
        await _database.CreateUserAsync("owner", ShopRoleNames.Admin);
        using var client = _database.CreateClient();
        var token = await LoginAsync(client, "owner", Password);
        var created = await SendAsync(client, HttpMethod.Post, "/api/users", token, new
        {
            userName = "counter",
            role = ShopRoleNames.Cashier,
            password = Password
        });
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var userId = createdJson.RootElement.GetProperty("id").GetString()!;
        var cashierToken = await LoginAsync(client, "counter", Password);

        var kept = await SendAsync(client, HttpMethod.Put, $"/api/users/{userId}", token, new
        {
            userName = "counter",
            role = ShopRoleNames.Manager,
            password = ""
        });
        var oldStillWorks = await client.PostAsJsonAsync("/api/auth/login", new { userName = "counter", password = Password });
        var changed = await SendAsync(client, HttpMethod.Put, $"/api/users/{userId}", token, new
        {
            userName = "counter",
            role = ShopRoleNames.Manager,
            password = "another-horse"
        });
        var changedBody = await changed.Content.ReadAsStringAsync();
        var oldPassword = await client.PostAsJsonAsync("/api/auth/login", new { userName = "counter", password = Password });
        var newPassword = await client.PostAsJsonAsync("/api/auth/login", new { userName = "counter", password = "another-horse" });
        var managerDenied = await SendAsync(client, HttpMethod.Get, "/api/users", cashierToken);
        await using var connection = await _database.OpenAsync();
        var hash = await connection.ExecuteScalarAsync<string>(
            "select password_hash from asp_net_users where id = @Id;",
            new { Id = userId });

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal(HttpStatusCode.OK, oldStillWorks.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.False(string.IsNullOrEmpty(hash));
        Assert.DoesNotContain(hash!, changedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("another-horse", changedBody, StringComparison.Ordinal);
        Assert.False(JsonDocument.Parse(changedBody).RootElement.TryGetProperty("passwordHash", out _));
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, managerDenied.StatusCode);
    }

    private static async Task<string> LoginAsync(HttpClient client, string userName, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("Login did not return an access token.");
    }

    private static async Task<string> UserIdAsync(HttpClient client, string userName, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("userId").GetString()
            ?? throw new InvalidOperationException("Login did not return a user id.");
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string token,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null || method == HttpMethod.Post || method == HttpMethod.Put)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("error").GetString() ?? string.Empty;
    }
}

public static class UserDatabaseGuard
{
    public const string PermittedDatabase = "retail360_users_test";

    public static void Require(string? databaseName)
    {
        if (!string.Equals(databaseName, PermittedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to change a database that is not the isolated users test database.");
        }
    }
}

public sealed class UserDatabaseFixture : IAsyncLifetime
{
    public const string SigningKey = "retail360-users-test-signing-key";

    public const string Issuer = "retail360-users-tests";

    public const string Audience = "retail360-users-tests";

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
            Database = UserDatabaseGuard.PermittedDatabase
        };
        UserDatabaseGuard.Require(builder.Database);
        Connections = new NpgsqlConnectionFactory(builder.ConnectionString);
        await using (var probe = await Connections.OpenConnectionAsync(CancellationToken.None))
        {
            UserDatabaseGuard.Require(await probe.ExecuteScalarAsync<string>("select current_database();"));
        }

        var runner = new SqlMigrationRunner(Connections);
        await runner.ApplyAsync(CancellationToken.None);
        await runner.ApplyAsync(CancellationToken.None);
        Directory = new ShopUserDirectory(Connections, new PostgresTransaction(Connections));
        _factory = new UserApiFactory(builder.ConnectionString);
    }

    public HttpClient CreateClient()
    {
        return _factory!.CreateClient();
    }

    public async Task<string> CreateUserAsync(string userName, string role)
    {
        var id = Guid.NewGuid().ToString("D");
        await Directory.CreateAsync(
            new NewShopUser(id, userName, Passwords.Hash(UserDatabaseTestsPassword.Value), role),
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        return id;
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = await Connections.OpenConnectionAsync(CancellationToken.None);
        UserDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
        return connection;
    }

    public async Task ResetAsync()
    {
        await using var connection = await OpenAsync();
        UserDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
        await connection.ExecuteAsync(
            """
            delete from asp_net_user_roles;
            delete from asp_net_users;
            """);
        UserDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
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
            new { Name = UserDatabaseGuard.PermittedDatabase });
        if (exists)
        {
            return;
        }

        await connection.ExecuteAsync($"create database {UserDatabaseGuard.PermittedDatabase};");
    }

    private sealed class UserApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public UserApiFactory(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            UserDatabaseGuard.Require(builder.Database);
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

internal static class UserDatabaseTestsPassword
{
    public const string Value = "correct-horse";
}
