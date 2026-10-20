using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Retail360.Application.Catalog;
using Retail360.Application.Identity;
using Retail360.Domain.Identity;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Identity;
using Retail360.Infrastructure.Persistence.Sql;
using Xunit;

namespace Retail360.Tests;

[CollectionDefinition("identity-database", DisableParallelization = true)]
public sealed class IdentityDatabaseCollection : ICollectionFixture<IdentityDatabaseFixture>
{
}

[Collection("identity-database")]
public sealed class AuthenticationTests
{
    private const string Password = "correct-horse";

    private readonly IdentityDatabaseFixture _database;

    public AuthenticationTests(IdentityDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public void Guard_rejects_the_shop_database_without_connecting()
    {
        Assert.Equal("retail360_identity_test", IdentityDatabaseGuard.PermittedDatabase);
        Assert.Throws<InvalidOperationException>(() => IdentityDatabaseGuard.Require("retail360"));
        Assert.Throws<InvalidOperationException>(() => IdentityDatabaseGuard.Require("retail360_catalog_test"));
        Assert.Throws<InvalidOperationException>(() => IdentityDatabaseGuard.Require("retail360_inventory_test"));
        IdentityDatabaseGuard.Require(IdentityDatabaseGuard.PermittedDatabase);
    }

    [Fact]
    public async Task Malformed_json_and_an_invalid_query_use_the_error_body()
    {
        using var client = _database.CreateClient();
        using var malformed = new StringContent("{", Encoding.UTF8, "application/json");
        var login = await client.PostAsync("/api/auth/login", malformed);
        var loginBody = await login.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        AssertShopError(loginBody, "The request body is invalid.");

        var userName = await CreateUserAsync(ShopRoleNames.Cashier, Password);
        var token = await LoginAsync(client, userName);
        var products = await SendAsync(client, "/api/products?limit=no", token);
        var productBody = await products.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, products.StatusCode);
        AssertShopError(productBody, null);
        Assert.DoesNotContain("traceId", productBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_signing_key_is_rejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Issuer"] = IdentityDatabaseFixture.Issuer,
                ["Authentication:Audience"] = IdentityDatabaseFixture.Audience
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() => ShopAuthenticationSettings.Read(configuration));
        Assert.Contains("SigningKey", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Valid_credentials_return_an_access_token()
    {
        var userName = await CreateUserAsync(ShopRoleNames.Cashier, Password);
        using var client = _database.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password = Password });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var token = document.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal("Bearer", document.RootElement.GetProperty("tokenType").GetString());
        Assert.Equal(userName, document.RootElement.GetProperty("userName").GetString());
        Assert.False(document.RootElement.TryGetProperty("passwordHash", out _));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var products = await client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
    }

    [Fact]
    public async Task Invalid_user_and_wrong_password_return_the_same_response()
    {
        var userName = await CreateUserAsync(ShopRoleNames.Cashier, Password);
        using var client = _database.CreateClient();
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { userName = "missing-" + Guid.NewGuid().ToString("N"), password = Password });
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { userName, password = "wrong-password" });
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        var wrongBody = await wrong.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(unknown.StatusCode, wrong.StatusCode);
        Assert.Equal(unknownBody, wrongBody);
        Assert.Contains(ShopSignInService.InvalidCredentialsMessage, unknownBody, StringComparison.Ordinal);
        Assert.DoesNotContain("not found", unknownBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Soft_deleted_and_locked_users_cannot_sign_in()
    {
        var deletedName = await CreateUserAsync(ShopRoleNames.Manager, Password);
        var lockedName = await CreateUserAsync(ShopRoleNames.Manager, Password);
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            "update asp_net_users set is_deleted = true, updated_at = now() where normalized_user_name = @Name;",
            new { Name = deletedName.ToUpperInvariant() });
        await connection.ExecuteAsync(
            """
            update asp_net_users
            set lockout_enabled = true, lockout_end = now() + interval '1 hour', updated_at = now()
            where normalized_user_name = @Name;
            """,
            new { Name = lockedName.ToUpperInvariant() });

        using var client = _database.CreateClient();
        var deleted = await client.PostAsJsonAsync("/api/auth/login", new { userName = deletedName, password = Password });
        var locked = await client.PostAsJsonAsync("/api/auth/login", new { userName = lockedName, password = Password });
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { userName = lockedName, password = "wrong-password" });
        var deletedBody = await deleted.Content.ReadAsStringAsync();
        var lockedBody = await locked.Content.ReadAsStringAsync();
        var wrongBody = await wrong.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, deleted.StatusCode);
        Assert.Equal(deletedBody, lockedBody);
        Assert.Equal(lockedBody, wrongBody);
    }

    [Fact]
    public async Task Expired_malformed_and_incorrectly_signed_tokens_are_rejected()
    {
        var userName = await CreateUserAsync(ShopRoleNames.Cashier, Password);
        var userId = await UserIdAsync(userName);
        using var client = _database.CreateClient();
        var expired = await SendAsync(client, "/api/products", Token(userId, userName, ShopRoleNames.Cashier, IdentityDatabaseFixture.SigningKey, IdentityDatabaseFixture.Issuer, IdentityDatabaseFixture.Audience, DateTime.UtcNow.AddHours(-2)));
        var malformed = await SendAsync(client, "/api/products", "not-a-token");
        var resigned = await SendAsync(client, "/api/products", Token(userId, userName, ShopRoleNames.Cashier, "different-retail360-test-signing-key", IdentityDatabaseFixture.Issuer, IdentityDatabaseFixture.Audience, DateTime.UtcNow.AddMinutes(5)));

        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, resigned.StatusCode);
    }

    [Fact]
    public async Task Incorrect_issuer_and_audience_are_rejected()
    {
        var userName = await CreateUserAsync(ShopRoleNames.Cashier, Password);
        var userId = await UserIdAsync(userName);
        using var client = _database.CreateClient();
        var issuer = await SendAsync(client, "/api/products", Token(userId, userName, ShopRoleNames.Cashier, IdentityDatabaseFixture.SigningKey, "other-issuer", IdentityDatabaseFixture.Audience, DateTime.UtcNow.AddMinutes(5)));
        var audience = await SendAsync(client, "/api/products", Token(userId, userName, ShopRoleNames.Cashier, IdentityDatabaseFixture.SigningKey, IdentityDatabaseFixture.Issuer, "other-audience", DateTime.UtcNow.AddMinutes(5)));

        Assert.Equal(HttpStatusCode.Unauthorized, issuer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, audience.StatusCode);
    }

    [Fact]
    public async Task Login_response_does_not_contain_the_password_hash()
    {
        var userName = await CreateUserAsync(ShopRoleNames.Admin, Password);
        using var client = _database.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password = Password });
        var body = await response.Content.ReadAsStringAsync();
        await using var connection = await _database.OpenAsync();
        var hash = await connection.ExecuteScalarAsync<string>(
            "select password_hash from asp_net_users where normalized_user_name = @Name;",
            new { Name = userName.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.DoesNotContain(hash, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, body, StringComparison.Ordinal);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_token_cannot_grant_a_role_that_is_not_assigned()
    {
        var userName = await CreateUserAsync(ShopRoleNames.Cashier, Password);
        var userId = await UserIdAsync(userName);
        using var client = _database.CreateClient();
        var forged = Token(userId, userName, ShopRoleNames.Admin, IdentityDatabaseFixture.SigningKey, IdentityDatabaseFixture.Issuer, IdentityDatabaseFixture.Audience, DateTime.UtcNow.AddMinutes(5));
        var response = await SendAsync(client, "/api/categories", forged, HttpMethod.Post, new { name = "Forged " + Guid.NewGuid().ToString("N") });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var connection = await _database.OpenAsync();
        var created = await connection.ExecuteScalarAsync<long>(
            "select count(*) from categories where name like 'Forged %';");
        Assert.Equal(0, created);
    }

    [Fact]
    public async Task Anonymous_catalog_and_inventory_requests_are_unauthorized()
    {
        using var client = _database.CreateClient();
        var products = await client.GetAsync("/api/products");
        var areas = await client.GetAsync("/api/areas");
        var opening = await client.PostAsJsonAsync("/api/inventory/opening", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, products.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, areas.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, opening.StatusCode);
        Assert.Contains("Authentication is required.", await products.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cashier_cannot_change_catalog_and_manager_can()
    {
        var cashier = await CreateUserAsync(ShopRoleNames.Cashier, Password);
        var manager = await CreateUserAsync(ShopRoleNames.Manager, Password);
        using var client = _database.CreateClient();
        var cashierToken = await LoginAsync(client, cashier);
        var denied = await SendAsync(client, "/api/categories", cashierToken, HttpMethod.Post, new { name = "Dairy " + Guid.NewGuid().ToString("N") });
        var managerToken = await LoginAsync(client, manager);
        var name = "Dairy " + Guid.NewGuid().ToString("N");
        var allowed = await SendAsync(client, "/api/categories", managerToken, HttpMethod.Post, new { name });
        var invalid = await SendAsync(client, "/api/categories", managerToken, HttpMethod.Post, new { name = "" });
        var invalidBody = await invalid.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("\"error\"", invalidBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cashier_can_read_inventory_but_cannot_create_an_area()
    {
        var cashier = await CreateUserAsync(ShopRoleNames.Cashier, Password);
        var staff = await CreateUserAsync(ShopRoleNames.InventoryStaff, Password);
        using var client = _database.CreateClient();
        var cashierToken = await LoginAsync(client, cashier);
        var balances = await SendAsync(client, "/api/inventory/balances", cashierToken);
        var denied = await SendAsync(client, "/api/areas", cashierToken, HttpMethod.Post, new { name = "Front " + Guid.NewGuid().ToString("N") });
        var staffToken = await LoginAsync(client, staff);
        var created = await SendAsync(client, "/api/areas", staffToken, HttpMethod.Post, new { name = "Front " + Guid.NewGuid().ToString("N") });

        Assert.Equal(HttpStatusCode.OK, balances.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task A_deleted_user_token_is_rejected_on_the_next_request()
    {
        var userName = await CreateUserAsync(ShopRoleNames.Manager, Password);
        using var client = _database.CreateClient();
        var token = await LoginAsync(client, userName);
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            "update asp_net_users set is_deleted = true, updated_at = now() where normalized_user_name = @Name;",
            new { Name = userName.ToUpperInvariant() });
        var response = await SendAsync(client, "/api/products", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_creates_one_admin_and_stores_a_hash()
    {
        var userName = "bootstrap-" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Bootstrap:UserName"] = userName,
                ["Authentication:Bootstrap:Password"] = Password
            })
            .Build();
        var bootstrap = new ShopAdminBootstrap(
            configuration,
            _database.Directory,
            _database.Passwords,
            new SystemClock());
        await using (var cleanup = await _database.OpenAsync())
        {
            await cleanup.ExecuteAsync(
                """
                update asp_net_users u
                set is_deleted = true, updated_at = now()
                from asp_net_user_roles ur
                join asp_net_roles r on r.id = ur.role_id
                where ur.user_id = u.id
                  and r.name = 'Admin'
                  and u.is_deleted = false;
                """);
        }

        await bootstrap.EnsureAdminAsync(CancellationToken.None);
        await bootstrap.EnsureAdminAsync(CancellationToken.None);

        await using var connection = await _database.OpenAsync();
        var hash = await connection.ExecuteScalarAsync<string>(
            "select password_hash from asp_net_users where normalized_user_name = @Name and is_deleted = false;",
            new { Name = userName.ToUpperInvariant() });
        var admins = await connection.ExecuteScalarAsync<long>(
            """
            select count(*)
            from asp_net_users u
            join asp_net_user_roles ur on ur.user_id = u.id
            join asp_net_roles r on r.id = ur.role_id
            where u.normalized_user_name = @Name and r.name = 'Admin';
            """,
            new { Name = userName.ToUpperInvariant() });

        Assert.Equal(1, admins);
        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.NotEqual(Password, hash);
        Assert.True(_database.Passwords.Verify(hash, Password));
    }

    [Fact]
    public async Task Five_failures_lock_the_account_and_a_correct_password_does_not_unlock_it()
    {
        var userName = await CreateUserAsync(ShopRoleNames.Cashier, Password);
        using var client = _database.CreateClient();
        string? failedBody = null;
        for (var attempt = 0; attempt < ShopLockoutPolicy.Default.MaxFailedAttempts; attempt++)
        {
            var failed = await client.PostAsJsonAsync("/api/auth/login", new { userName, password = "wrong-password" });
            failedBody = await failed.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var correct = await client.PostAsJsonAsync("/api/auth/login", new { userName, password = Password });
        var unknown = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { userName = "missing-" + Guid.NewGuid().ToString("N"), password = Password });
        var correctBody = await correct.Content.ReadAsStringAsync();
        var state = await FailureStateAsync(userName);

        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
        Assert.Equal(failedBody, correctBody);
        Assert.Equal(correctBody, await unknown.Content.ReadAsStringAsync());
        Assert.Equal(0, state.AccessFailedCount);
        Assert.True(state.LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_successful_login_resets_the_failed_count()
    {
        var userName = await CreateUserAsync(ShopRoleNames.Manager, Password);
        using var client = _database.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new { userName, password = "wrong-password" });
        await client.PostAsJsonAsync("/api/auth/login", new { userName, password = "wrong-password" });
        var success = await client.PostAsJsonAsync("/api/auth/login", new { userName, password = Password });
        var state = await FailureStateAsync(userName);

        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Equal(0, state.AccessFailedCount);
        Assert.Null(state.LockoutEnd);
    }

    [Fact]
    public async Task Concurrent_failed_logins_lock_the_account_once()
    {
        var userName = await CreateUserAsync(ShopRoleNames.Cashier, Password);
        var userId = await UserIdAsync(userName);
        var lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(5);
        await using var holder = await _database.OpenAsync();
        await using var gate = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "select id from asp_net_users where id = @Id for update;",
            new { Id = userId },
            gate);

        var first = RunOnOwnThread(() => _database.Directory.RecordFailedAccessAsync(
            userId,
            ShopLockoutPolicy.Default.MaxFailedAttempts,
            DateTimeOffset.UtcNow,
            lockoutEnd,
            CancellationToken.None));
        var second = RunOnOwnThread(() => _database.Directory.RecordFailedAccessAsync(
            userId,
            ShopLockoutPolicy.Default.MaxFailedAttempts,
            DateTimeOffset.UtcNow,
            lockoutEnd,
            CancellationToken.None));
        await using var watcher = await _database.OpenAsync();
        await WaitForWaitingSessionsAsync(watcher, 2, "%access_failed_count%", first, second);
        await gate.CommitAsync();
        await Task.WhenAll(first, second);
        for (var extra = 0; extra < ShopLockoutPolicy.Default.MaxFailedAttempts - 2; extra++)
        {
            await _database.Directory.RecordFailedAccessAsync(
                userId,
                ShopLockoutPolicy.Default.MaxFailedAttempts,
                DateTimeOffset.UtcNow,
                lockoutEnd,
                CancellationToken.None);
        }

        var state = await FailureStateAsync(userName);

        Assert.Equal(0, state.AccessFailedCount);
        Assert.True(state.LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_locked_user_token_is_rejected()
    {
        var userName = await CreateUserAsync(ShopRoleNames.Cashier, Password);
        using var client = _database.CreateClient();
        var token = await LoginAsync(client, userName);
        await using var connection = await _database.OpenAsync();
        await connection.ExecuteAsync(
            """
            update asp_net_users
            set lockout_enabled = true, lockout_end = now() + interval '1 hour', updated_at = now()
            where normalized_user_name = @Name;
            """,
            new { Name = userName.ToUpperInvariant() });
        var response = await SendAsync(client, "/api/products", token);
        var before = await FailureStateAsync(userName);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { userName, password = Password });
        var after = await FailureStateAsync(userName);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal(before.LockoutEnd, after.LockoutEnd);
    }

    [Fact]
    public async Task Concurrent_bootstrap_creates_one_admin()
    {
        var userName = "bootstrap-" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Bootstrap:UserName"] = userName,
                ["Authentication:Bootstrap:Password"] = Password
            })
            .Build();
        await using (var cleanup = await _database.OpenAsync())
        {
            await cleanup.ExecuteAsync(
                """
                update asp_net_users u
                set is_deleted = true, updated_at = now()
                from asp_net_user_roles ur
                join asp_net_roles r on r.id = ur.role_id
                where ur.user_id = u.id and r.name = 'Admin' and u.is_deleted = false;
                """);
        }

        var firstBootstrap = new ShopAdminBootstrap(configuration, _database.Directory, _database.Passwords, new SystemClock());
        var secondBootstrap = new ShopAdminBootstrap(configuration, _database.Directory, _database.Passwords, new SystemClock());
        await using var holder = await _database.OpenAsync();
        await using var gate = await holder.BeginTransactionAsync();
        await holder.ExecuteAsync(
            "select id from asp_net_roles where name = 'Admin' and is_deleted = false for update;",
            transaction: gate);
        var first = RunOnOwnThread(() => firstBootstrap.EnsureAdminAsync(CancellationToken.None));
        var second = RunOnOwnThread(() => secondBootstrap.EnsureAdminAsync(CancellationToken.None));
        await using var watcher = await _database.OpenAsync();
        await WaitForWaitingSessionsAsync(watcher, 2, "%asp_net_roles%");
        await gate.CommitAsync();
        await Task.WhenAll(first, second);

        await using var connection = await _database.OpenAsync();
        var admins = await connection.ExecuteScalarAsync<long>(
            """
            select count(*)
            from asp_net_users u
            join asp_net_user_roles ur on ur.user_id = u.id
            join asp_net_roles r on r.id = ur.role_id
            where u.is_deleted = false and r.name = 'Admin';
            """);
        var named = await connection.ExecuteScalarAsync<long>(
            "select count(*) from asp_net_users where normalized_user_name = @Name;",
            new { Name = userName.ToUpperInvariant() });

        Assert.Equal(1, admins);
        Assert.Equal(1, named);
    }

    private async Task<FailureState> FailureStateAsync(string userName)
    {
        await using var connection = await _database.OpenAsync();
        return await connection.QuerySingleAsync<FailureState>(
            """
            select access_failed_count, lockout_end
            from asp_net_users
            where normalized_user_name = @Name;
            """,
            new { Name = userName.ToUpperInvariant() });
    }

    private static async Task WaitForWaitingSessionsAsync(
        NpgsqlConnection connection,
        int sessionCount,
        string queryPattern,
        params Task[] attempts)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        long waiting = 0;
        while (DateTime.UtcNow < deadline)
        {
            var faulted = attempts.FirstOrDefault(attempt => attempt.IsFaulted);
            if (faulted is not null)
            {
                await faulted;
            }

            waiting = await connection.ExecuteScalarAsync<long>(
                """
                select count(*)
                from pg_stat_activity
                where datname = current_database()
                  and pid <> pg_backend_pid()
                  and wait_event_type = 'Lock'
                  and query ilike @QueryPattern;
                """,
                new { QueryPattern = queryPattern });
            if (waiting >= sessionCount)
            {
                return;
            }

            await Task.Delay(20);
        }

        var states = string.Join(", ", attempts.Select(attempt => attempt.Status.ToString()));
        throw new TimeoutException(
            $"Expected {sessionCount} waiting session(s). Last count was {waiting}. Task states: {states}.");
    }

    private static Task RunOnOwnThread(Func<Task> work)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                work().GetAwaiter().GetResult();
                completion.SetResult();
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

    private sealed class FailureState
    {
        public int AccessFailedCount { get; init; }

        public DateTimeOffset? LockoutEnd { get; init; }
    }

    private async Task<string> CreateUserAsync(string role, string password)
    {
        var userName = role.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant()
            + "-"
            + Guid.NewGuid().ToString("N");
        await _database.Directory.CreateAsync(
            new NewShopUser(Guid.NewGuid().ToString("D"), userName, _database.Passwords.Hash(password), role),
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        return userName;
    }

    private async Task<string> UserIdAsync(string userName)
    {
        await using var connection = await _database.OpenAsync();
        return await connection.ExecuteScalarAsync<string>(
            "select id from asp_net_users where normalized_user_name = @Name;",
            new { Name = userName.ToUpperInvariant() })
            ?? throw new InvalidOperationException("The test user was not stored.");
    }

    private static void AssertShopError(string body, string? expected)
    {
        using var document = JsonDocument.Parse(body);
        var message = document.RootElement.GetProperty("error").GetString();
        Assert.False(string.IsNullOrWhiteSpace(message));
        if (expected is not null)
        {
            Assert.Equal(expected, message);
        }

        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
        Assert.Single(document.RootElement.EnumerateObject());
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

    private static string Token(
        string userId,
        string userName,
        string role,
        string signingKey,
        string issuer,
        string audience,
        DateTime expires)
    {
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [
                new Claim("sub", userId),
                new Claim("unique_name", userName),
                new Claim("role", role)
            ],
            expires.AddMinutes(-10),
            expires,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class IdentityDatabaseFixture : IAsyncLifetime
{
    public const string SigningKey = "retail360-identity-test-signing-key";

    public const string Issuer = "retail360-tests";

    public const string Audience = "retail360-tests";

    private WebApplicationFactory<Program>? _factory;

    public ShopUserDirectory Directory { get; private set; } = null!;

    public ShopPasswordHasher Passwords { get; } = new();

    public async Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var builder = new NpgsqlConnectionStringBuilder(TestShopConnection.Read())
        {
            Database = "postgres"
        };
        await EnsureDatabaseAsync(builder.ConnectionString);
        builder.Database = IdentityDatabaseGuard.PermittedDatabase;
        IdentityDatabaseGuard.Require(builder.Database);
        var connections = new NpgsqlConnectionFactory(builder.ConnectionString);
        await using (var probe = await connections.OpenConnectionAsync(CancellationToken.None))
        {
            IdentityDatabaseGuard.Require(await probe.ExecuteScalarAsync<string>("select current_database();"));
        }

        Directory = new ShopUserDirectory(connections, new PostgresTransaction(connections));
        var runner = new SqlMigrationRunner(connections);
        await runner.ApplyAsync(CancellationToken.None);
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", builder.ConnectionString);
        Environment.SetEnvironmentVariable("Authentication__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Authentication__Audience", Audience);
        Environment.SetEnvironmentVariable("Authentication__SigningKey", SigningKey);
        Environment.SetEnvironmentVariable("Authentication__AccessTokenMinutes", "15");
        _factory = new IdentityApiFactory(builder.ConnectionString);
    }

    public HttpClient CreateClient()
    {
        return _factory!.CreateClient();
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(TestShopConnection.Read())
        {
            Database = IdentityDatabaseGuard.PermittedDatabase
        };
        IdentityDatabaseGuard.Require(builder.Database);
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        IdentityDatabaseGuard.Require(await connection.ExecuteScalarAsync<string>("select current_database();"));
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
        var exists = await connection.ExecuteScalarAsync<bool>(
            "select exists(select 1 from pg_database where datname = @Name);",
            new { Name = IdentityDatabaseGuard.PermittedDatabase });
        if (exists)
        {
            return;
        }

        await connection.ExecuteAsync($"create database {IdentityDatabaseGuard.PermittedDatabase};");
    }

    private sealed class IdentityApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public IdentityApiFactory(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            IdentityDatabaseGuard.Require(builder.Database);
            _connectionString = builder.ConnectionString;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = _connectionString,
                    ["Authentication:Issuer"] = IdentityDatabaseFixture.Issuer,
                    ["Authentication:Audience"] = IdentityDatabaseFixture.Audience,
                    ["Authentication:SigningKey"] = IdentityDatabaseFixture.SigningKey,
                    ["Authentication:AccessTokenMinutes"] = "15"
                });
            });
        }
    }
}
