using Dapper;
using Npgsql;
using Retail360.Application.Identity;
using Retail360.Domain.Audit;
using Retail360.Domain.Identity;
using Retail360.Infrastructure.Audit;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Identity;

public sealed class UserAccountStore : IUserAccountStore
{
    private const string RoleSql = """
        (
            select r.name
            from asp_net_user_roles ur
            join asp_net_roles r on r.id = ur.role_id and r.is_deleted = false
            where ur.user_id = u.id
            order by r.name
            limit 1
        )
        """;

    private readonly NpgsqlConnectionFactory _connections;
    private readonly PostgresTransaction _transactions;

    public UserAccountStore(NpgsqlConnectionFactory connections, PostgresTransaction transactions)
    {
        _connections = connections;
        _transactions = transactions;
    }

    public async Task<IReadOnlyList<UserResponse>> ListAsync(
        string? name,
        bool activeOnly,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<UserRow>(new CommandDefinition(
            $"""
            select u.id, u.user_name, {RoleSql} as role, u.is_active, u.created_at, u.updated_at
            from asp_net_users u
            where u.is_deleted = false
              and (@ActiveOnly = false or u.is_active = true)
              and (@NamePattern is null or u.user_name ilike @NamePattern escape '\')
            order by lower(u.user_name), u.id
            limit @Limit offset @Offset;
            """,
            new
            {
                ActiveOnly = activeOnly,
                NamePattern = ContainsPattern(name),
                Limit = limit,
                Offset = offset
            },
            cancellationToken: cancellationToken));
        return rows.Select(ToResponse).ToArray();
    }

    public async Task<UserResponse?> FindAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            $"""
            select u.id, u.user_name, {RoleSql} as role, u.is_active, u.created_at, u.updated_at
            from asp_net_users u
            where u.id = @Id and u.is_deleted = false;
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
        return row is null ? null : ToResponse(row);
    }

    public Task<UserResponse> AddAsync(
        string id,
        string userName,
        string role,
        string passwordHash,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var normalized = userName.ToUpperInvariant();
        return _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            if (await NameTakenAsync(connection, transaction, normalized, null, token))
            {
                throw new UserRuleException(UserRuleFailure.Conflict, "A user with this name already exists.");
            }

            var roleId = await RequireRoleIdAsync(connection, transaction, role, token);
            var stamp = Guid.NewGuid().ToString("D");
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    insert into asp_net_users (
                        id, created_at, updated_at, is_deleted, is_active, user_name, normalized_user_name,
                        email, normalized_email, email_confirmed, password_hash, security_stamp,
                        concurrency_stamp, phone_number, phone_number_confirmed, two_factor_enabled,
                        lockout_end, lockout_enabled, access_failed_count)
                    values (
                        @Id, @UtcNow, @UtcNow, false, true, @UserName, @NormalizedUserName,
                        null, null, false, @PasswordHash, @Stamp,
                        @Stamp, null, false, false,
                        null, true, 0);
                    """,
                    new
                    {
                        Id = id,
                        UtcNow = utcNow,
                        UserName = userName,
                        NormalizedUserName = normalized,
                        PasswordHash = passwordHash,
                        Stamp = stamp
                    },
                    transaction,
                    cancellationToken: token));
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new UserRuleException(UserRuleFailure.Conflict, "A user with this name already exists.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into asp_net_user_roles (user_id, role_id)
                values (@UserId, @RoleId);
                """,
                new { UserId = id, RoleId = roleId },
                transaction,
                cancellationToken: token));
            await AuditEntryWriter.AppendAsync(
                connection,
                transaction,
                AuditActions.UserCreated,
                AuditEntityTypes.User,
                id,
                utcNow,
                new Dictionary<string, string?>
                {
                    ["userName"] = userName,
                    ["role"] = role
                },
                null,
                token);
            return new UserResponse(id, userName, role, true, utcNow, utcNow);
        }, cancellationToken);
    }

    public Task<UserResponse> UpdateAsync(
        string actorId,
        string id,
        string userName,
        string role,
        string? passwordHash,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var normalized = userName.ToUpperInvariant();
        return _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            var admins = await LockUsableAdminsAsync(connection, transaction, token);
            var current = await LockUserAsync(connection, transaction, id, token);
            if (await NameTakenAsync(connection, transaction, normalized, id, token))
            {
                throw new UserRuleException(UserRuleFailure.Conflict, "A user with this name already exists.");
            }

            if (admins.Contains(current.Id)
                && admins.Count == 1
                && !string.Equals(role, ShopRoleNames.Admin, StringComparison.Ordinal))
            {
                throw new UserRuleException(
                    UserRuleFailure.Conflict,
                    "The last Admin cannot be given another role.");
            }

            var roleId = await RequireRoleIdAsync(connection, transaction, role, token);
            var updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                update asp_net_users
                set user_name = @UserName,
                    normalized_user_name = @NormalizedUserName,
                    password_hash = coalesce(@PasswordHash, password_hash),
                    security_stamp = case when @PasswordHash is null then security_stamp else @Stamp end,
                    concurrency_stamp = @Stamp,
                    updated_at = @UtcNow
                where id = @Id and is_deleted = false;
                """,
                new
                {
                    Id = id,
                    UserName = userName,
                    NormalizedUserName = normalized,
                    PasswordHash = passwordHash,
                    Stamp = Guid.NewGuid().ToString("D"),
                    UtcNow = utcNow
                },
                transaction,
                cancellationToken: token));
            if (updated != 1)
            {
                throw new UserRuleException(UserRuleFailure.NotFound, "User was not found.");
            }

            var previousRole = await RoleNameAsync(connection, transaction, id, token);
            await connection.ExecuteAsync(new CommandDefinition(
                "delete from asp_net_user_roles where user_id = @Id;",
                new { Id = id },
                transaction,
                cancellationToken: token));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into asp_net_user_roles (user_id, role_id)
                values (@UserId, @RoleId);
                """,
                new { UserId = id, RoleId = roleId },
                transaction,
                cancellationToken: token));
            if (!string.Equals(previousRole, role, StringComparison.Ordinal))
            {
                await AuditEntryWriter.AppendAsync(
                    connection,
                    transaction,
                    AuditActions.UserRoleChanged,
                    AuditEntityTypes.User,
                    id,
                    utcNow,
                    new Dictionary<string, string?>
                    {
                        ["userName"] = userName,
                        ["previousRole"] = previousRole,
                        ["role"] = role
                    },
                    actorId,
                    token);
            }

            if (passwordHash is not null)
            {
                await AppendPasswordChangedAsync(connection, transaction, id, userName, utcNow, actorId, token);
            }

            return new UserResponse(id, userName, role, current.IsActive, current.CreatedAt, utcNow);
        }, cancellationToken);
    }

    public Task<UserResponse> SetActiveAsync(
        string actorId,
        string id,
        bool active,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        return _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            var admins = await LockUsableAdminsAsync(connection, transaction, token);
            var current = await LockUserAsync(connection, transaction, id, token);
            if (!active && SameUser(actorId, current.Id))
            {
                throw new UserRuleException(UserRuleFailure.Conflict, "You cannot deactivate your own account.");
            }

            if (!active && admins.Contains(current.Id) && admins.Count == 1)
            {
                throw new UserRuleException(UserRuleFailure.Conflict, "The last Admin cannot be deactivated.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                update asp_net_users
                set is_active = @Active,
                    updated_at = @UtcNow
                where id = @Id and is_deleted = false;
                """,
                new { Id = id, Active = active, UtcNow = utcNow },
                transaction,
                cancellationToken: token));
            var role = await RoleNameAsync(connection, transaction, id, token);
            await AuditEntryWriter.AppendAsync(
                connection,
                transaction,
                active ? AuditActions.UserActivated : AuditActions.UserDeactivated,
                AuditEntityTypes.User,
                id,
                utcNow,
                new Dictionary<string, string?> { ["userName"] = current.UserName },
                actorId,
                token);
            return new UserResponse(id, current.UserName, role, active, current.CreatedAt, utcNow);
        }, cancellationToken);
    }

    public Task ReplacePasswordAsync(
        string userId,
        string passwordHash,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        return _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            var current = await LockUserAsync(connection, transaction, userId, token);
            var updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                update asp_net_users
                set password_hash = @PasswordHash,
                    security_stamp = @Stamp,
                    concurrency_stamp = @Stamp,
                    updated_at = @UtcNow
                where id = @Id and is_deleted = false;
                """,
                new
                {
                    Id = userId,
                    PasswordHash = passwordHash,
                    Stamp = Guid.NewGuid().ToString("D"),
                    UtcNow = utcNow
                },
                transaction,
                cancellationToken: token));
            if (updated != 1)
            {
                throw new UserRuleException(UserRuleFailure.NotFound, "User was not found.");
            }

            await AppendPasswordChangedAsync(
                connection,
                transaction,
                userId,
                current.UserName,
                utcNow,
                userId,
                token);
        }, cancellationToken);
    }

    public Task RemoveAsync(string actorId, string id, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        return _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            var admins = await LockUsableAdminsAsync(connection, transaction, token);
            var current = await LockUserAsync(connection, transaction, id, token);
            if (SameUser(actorId, current.Id))
            {
                throw new UserRuleException(UserRuleFailure.Conflict, "You cannot remove your own account.");
            }

            if (admins.Contains(current.Id) && admins.Count == 1)
            {
                throw new UserRuleException(UserRuleFailure.Conflict, "The last Admin cannot be removed.");
            }

            var updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                update asp_net_users
                set is_deleted = true,
                    updated_at = @UtcNow
                where id = @Id and is_deleted = false;
                """,
                new { Id = id, UtcNow = utcNow },
                transaction,
                cancellationToken: token));
            if (updated != 1)
            {
                throw new UserRuleException(UserRuleFailure.NotFound, "User was not found.");
            }

            await AuditEntryWriter.AppendAsync(
                connection,
                transaction,
                AuditActions.UserDeleted,
                AuditEntityTypes.User,
                id,
                utcNow,
                new Dictionary<string, string?> { ["userName"] = current.UserName },
                actorId,
                token);
        }, cancellationToken);
    }

    private static async Task<HashSet<string>> LockUsableAdminsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var ids = await connection.QueryAsync<string>(new CommandDefinition(
            """
            select u.id
            from asp_net_users u
            join asp_net_user_roles ur on ur.user_id = u.id
            join asp_net_roles r on r.id = ur.role_id
            where u.is_deleted = false
              and u.is_active = true
              and r.is_deleted = false
              and r.name = 'Admin'
            order by u.id
            for update of u;
            """,
            transaction: transaction,
            cancellationToken: cancellationToken));
        return ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<LockedUser> LockUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string id,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<LockedUser>(new CommandDefinition(
            """
            select id, user_name, is_active, created_at
            from asp_net_users
            where id = @Id and is_deleted = false
            for update;
            """,
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));
        if (row is null || string.IsNullOrWhiteSpace(row.UserName))
        {
            throw new UserRuleException(UserRuleFailure.NotFound, "User was not found.");
        }

        return row;
    }

    private static async Task<bool> NameTakenAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string normalizedUserName,
        string? exceptId,
        CancellationToken cancellationToken)
    {
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists (
                select 1
                from asp_net_users
                where normalized_user_name = @NormalizedUserName
                  and (@ExceptId is null or id <> @ExceptId));
            """,
            new { NormalizedUserName = normalizedUserName, ExceptId = exceptId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static Task AppendPasswordChangedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string userId,
        string userName,
        DateTimeOffset utcNow,
        string actorId,
        CancellationToken cancellationToken)
    {
        return AuditEntryWriter.AppendAsync(
            connection,
            transaction,
            AuditActions.UserPasswordChanged,
            AuditEntityTypes.User,
            userId,
            utcNow,
            new Dictionary<string, string?> { ["userName"] = userName },
            actorId,
            cancellationToken);
    }

    private static async Task<string> RequireRoleIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string role,
        CancellationToken cancellationToken)
    {
        var roleId = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            select id
            from asp_net_roles
            where name = @Name and is_deleted = false;
            """,
            new { Name = role },
            transaction,
            cancellationToken: cancellationToken));
        if (roleId is null)
        {
            throw new UserRuleException(
                UserRuleFailure.Invalid,
                "Role must be Admin, Manager, Cashier, or Inventory Staff.");
        }

        return roleId;
    }

    private static async Task<string> RoleNameAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string id,
        CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            select r.name
            from asp_net_user_roles ur
            join asp_net_roles r on r.id = ur.role_id and r.is_deleted = false
            where ur.user_id = @Id
            order by r.name
            limit 1;
            """,
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken)) ?? string.Empty;
    }

    private static bool SameUser(string actorId, string userId)
    {
        return string.Equals(actorId, userId, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ContainsPattern(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var escaped = name.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static UserResponse ToResponse(UserRow row)
    {
        return new UserResponse(
            row.Id,
            row.UserName ?? string.Empty,
            row.Role ?? string.Empty,
            row.IsActive,
            row.CreatedAt,
            row.UpdatedAt);
    }

    private sealed class UserRow
    {
        public string Id { get; init; } = string.Empty;

        public string? UserName { get; init; }

        public string? Role { get; init; }

        public bool IsActive { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class LockedUser
    {
        public string Id { get; init; } = string.Empty;

        public string UserName { get; init; } = string.Empty;

        public bool IsActive { get; init; }

        public DateTimeOffset CreatedAt { get; init; }
    }
}
