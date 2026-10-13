using Dapper;
using Retail360.Application.Identity;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Identity;

public sealed class ShopUserDirectory : IShopUserDirectory
{
    private readonly NpgsqlConnectionFactory _connections;
    private readonly PostgresTransaction _transactions;

    public ShopUserDirectory(NpgsqlConnectionFactory connections, PostgresTransaction transactions)
    {
        _connections = connections;
        _transactions = transactions;
    }

    public async Task<ShopUserAccount?> FindByUserNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select id, user_name, password_hash, is_deleted, lockout_enabled, lockout_end
            from asp_net_users
            where normalized_user_name = @NormalizedUserName;
            """;
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ShopUserRow>(new CommandDefinition(
            sql,
            new { NormalizedUserName = normalizedUserName },
            cancellationToken: cancellationToken));
        return row is null ? null : ToAccount(row);
    }

    public async Task<ShopUserAccount?> FindActiveByIdAsync(
        string userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select id, user_name, password_hash, is_deleted, lockout_enabled, lockout_end
            from asp_net_users
            where id = @Id;
            """;
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ShopUserRow>(new CommandDefinition(
            sql,
            new { Id = userId },
            cancellationToken: cancellationToken));
        if (row is null || row.IsDeleted || string.IsNullOrWhiteSpace(row.UserName))
        {
            return null;
        }

        if (row.LockoutEnabled && row.LockoutEnd is { } lockedUntil && lockedUntil > utcNow)
        {
            return null;
        }

        return ToAccount(row);
    }

    public async Task<IReadOnlyList<string>> ListRoleNamesAsync(string userId, CancellationToken cancellationToken)
    {
        const string sql = """
            select r.name
            from asp_net_user_roles ur
            join asp_net_roles r on r.id = ur.role_id
            where ur.user_id = @UserId
              and r.is_deleted = false
              and r.name is not null
            order by r.name;
            """;
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var names = await connection.QueryAsync<string>(new CommandDefinition(
            sql,
            new { UserId = userId },
            cancellationToken: cancellationToken));
        return names.ToArray();
    }

    public async Task<bool> HasActiveAdminAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from asp_net_user_roles ur
                join asp_net_users u on u.id = ur.user_id
                join asp_net_roles r on r.id = ur.role_id
                where u.is_deleted = false
                  and r.is_deleted = false
                  and r.name = 'Admin');
            """;
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql, cancellationToken: cancellationToken));
    }

    public async Task<bool> UserNameExistsAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from asp_net_users
                where normalized_user_name = @NormalizedUserName);
            """;
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            sql,
            new { NormalizedUserName = normalizedUserName },
            cancellationToken: cancellationToken));
    }

    public Task CreateAsync(NewShopUser user, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        var normalized = user.UserName.Trim().ToUpperInvariant();
        return _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            var roleId = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                select id
                from asp_net_roles
                where name = @Name and is_deleted = false;
                """,
                new { Name = user.RoleName },
                transaction,
                cancellationToken: token));
            if (roleId is null)
            {
                throw new InvalidOperationException($"Shop role {user.RoleName} is missing.");
            }

            var stamp = Guid.NewGuid().ToString("D");
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into asp_net_users (
                    id, created_at, updated_at, is_deleted, user_name, normalized_user_name,
                    email, normalized_email, email_confirmed, password_hash, security_stamp,
                    concurrency_stamp, phone_number, phone_number_confirmed, two_factor_enabled,
                    lockout_end, lockout_enabled, access_failed_count)
                values (
                    @Id, @UtcNow, @UtcNow, false, @UserName, @NormalizedUserName,
                    null, null, false, @PasswordHash, @Stamp,
                    @Stamp, null, false, false,
                    null, true, 0);
                """,
                new
                {
                    user.Id,
                    UtcNow = utcNow,
                    UserName = user.UserName.Trim(),
                    NormalizedUserName = normalized,
                    user.PasswordHash,
                    Stamp = stamp
                },
                transaction,
                cancellationToken: token));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into asp_net_user_roles (user_id, role_id)
                values (@UserId, @RoleId);
                """,
                new { UserId = user.Id, RoleId = roleId },
                transaction,
                cancellationToken: token));
        }, cancellationToken);
    }

    public async Task RecordFailedAccessAsync(
        string userId,
        int maxFailedAttempts,
        DateTimeOffset utcNow,
        DateTimeOffset lockoutEnd,
        CancellationToken cancellationToken)
    {
        const string sql = """
            update asp_net_users
            set access_failed_count = case
                    when lockout_enabled and access_failed_count + 1 >= @MaxFailedAttempts then 0
                    else access_failed_count + 1
                end,
                lockout_end = case
                    when lockout_enabled and access_failed_count + 1 >= @MaxFailedAttempts then @LockoutEnd
                    else lockout_end
                end,
                updated_at = @UtcNow
            where id = @Id
              and is_deleted = false
              and (not lockout_enabled or lockout_end is null or lockout_end <= @UtcNow);
            """;
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = userId, MaxFailedAttempts = maxFailedAttempts, UtcNow = utcNow, LockoutEnd = lockoutEnd },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> ResetAccessFailuresIfUnlockedAsync(
        string userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        const string sql = """
            update asp_net_users
            set access_failed_count = 0,
                lockout_end = null,
                updated_at = @UtcNow
            where id = @Id
              and is_deleted = false
              and (not lockout_enabled or lockout_end is null or lockout_end <= @UtcNow);
            """;
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = userId, UtcNow = utcNow },
            cancellationToken: cancellationToken));
        return updated == 1;
    }

    public Task<bool> TryCreateAdminAsync(NewShopUser user, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        var normalized = user.UserName.Trim().ToUpperInvariant();
        return _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            var roleId = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                select id
                from asp_net_roles
                where name = @Name and is_deleted = false
                for update;
                """,
                new { Name = user.RoleName },
                transaction,
                cancellationToken: token));
            if (roleId is null)
            {
                throw new InvalidOperationException($"Shop role {user.RoleName} is missing.");
            }

            var adminExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                select exists (
                    select 1
                    from asp_net_user_roles ur
                    join asp_net_users u on u.id = ur.user_id
                    where ur.role_id = @RoleId
                      and u.is_deleted = false);
                """,
                new { RoleId = roleId },
                transaction,
                cancellationToken: token));
            if (adminExists)
            {
                return false;
            }

            var nameTaken = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                select exists (
                    select 1
                    from asp_net_users
                    where normalized_user_name = @NormalizedUserName);
                """,
                new { NormalizedUserName = normalized },
                transaction,
                cancellationToken: token));
            if (nameTaken)
            {
                throw new InvalidOperationException("Bootstrap admin user name is already in use.");
            }

            var stamp = Guid.NewGuid().ToString("D");
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into asp_net_users (
                    id, created_at, updated_at, is_deleted, user_name, normalized_user_name,
                    email, normalized_email, email_confirmed, password_hash, security_stamp,
                    concurrency_stamp, phone_number, phone_number_confirmed, two_factor_enabled,
                    lockout_end, lockout_enabled, access_failed_count)
                values (
                    @Id, @UtcNow, @UtcNow, false, @UserName, @NormalizedUserName,
                    null, null, false, @PasswordHash, @Stamp,
                    @Stamp, null, false, false,
                    null, true, 0);
                """,
                new
                {
                    user.Id,
                    UtcNow = utcNow,
                    UserName = user.UserName.Trim(),
                    NormalizedUserName = normalized,
                    user.PasswordHash,
                    Stamp = stamp
                },
                transaction,
                cancellationToken: token));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into asp_net_user_roles (user_id, role_id)
                values (@UserId, @RoleId);
                """,
                new { UserId = user.Id, RoleId = roleId },
                transaction,
                cancellationToken: token));
            return true;
        }, cancellationToken);
    }

    private static ShopUserAccount ToAccount(ShopUserRow row)
    {
        return new ShopUserAccount(
            row.Id,
            row.UserName ?? string.Empty,
            row.PasswordHash,
            row.IsDeleted,
            row.LockoutEnabled,
            row.LockoutEnd);
    }

    private sealed class ShopUserRow
    {
        public string Id { get; init; } = string.Empty;

        public string? UserName { get; init; }

        public string? PasswordHash { get; init; }

        public bool IsDeleted { get; init; }

        public bool LockoutEnabled { get; init; }

        public DateTimeOffset? LockoutEnd { get; init; }
    }
}
