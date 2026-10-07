using Dapper;
using Retail360.Domain.Identity;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Identity;

public sealed class ShopRoleQuery
{
    private static readonly string[] RequiredRoleNames =
    [
        ShopRoleNames.Admin,
        ShopRoleNames.Manager,
        ShopRoleNames.Cashier,
        ShopRoleNames.InventoryStaff
    ];

    private readonly NpgsqlConnectionFactory _connections;

    public ShopRoleQuery(NpgsqlConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<IReadOnlyList<ShopRoleRow>> ListActiveAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            select
                id,
                name,
                normalized_name,
                created_at,
                updated_at,
                is_deleted
            from asp_net_roles
            where is_deleted = false
            order by name;
            """;

        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var roles = await connection.QueryAsync<ShopRoleRow>(new CommandDefinition(
            sql,
            cancellationToken: cancellationToken));
        return roles.AsList();
    }

    public async Task EnsureRequiredRolesAsync(CancellationToken cancellationToken)
    {
        var roles = await ListActiveAsync(cancellationToken);
        var names = roles
            .Select(role => role.Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        var missing = RequiredRoleNames.Where(name => !names.Contains(name)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Required shop roles are missing: {string.Join(", ", missing)}.");
        }
    }
}

public sealed class ShopRoleRow
{
    public string Id { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? NormalizedName { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public bool IsDeleted { get; init; }
}
