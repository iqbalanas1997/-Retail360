using Dapper;
using Retail360.Application.Inventory;
using Retail360.Domain.Inventory;
using Retail360.Infrastructure.Catalog;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Inventory;

public sealed class AreaInventoryStore : IAreaStore
{
    private readonly NpgsqlConnectionFactory _connections;
    private readonly PostgresTransaction _transactions;

    public AreaInventoryStore(NpgsqlConnectionFactory connections, PostgresTransaction transactions)
    {
        _connections = connections;
        _transactions = transactions;
    }

    public async Task<Area?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<AreaRow>(new CommandDefinition(
            """
            select id, name, is_active, created_at, updated_at, is_deleted
            from areas
            where id = @Id and is_deleted = false;
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
        return row is null ? null : ToArea(row);
    }

    public async Task<IReadOnlyList<Area>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AreaRow>(new CommandDefinition(
            """
            select id, name, is_active, created_at, updated_at, is_deleted
            from areas
            where is_deleted = false
            order by lower(name), id;
            """,
            cancellationToken: cancellationToken));
        return rows.Select(ToArea).ToArray();
    }

    public async Task<bool> HasShelfOrHistoryAsync(Guid areaId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists (
                select 1 from shelves where area_id = @Id and is_deleted = false
                union all
                select 1
                from shelves s
                join stock_balances b on b.shelf_id = s.id
                where s.area_id = @Id
                union all
                select 1
                from shelves s
                join stock_movements m on m.shelf_id = s.id or m.other_shelf_id = s.id
                where s.area_id = @Id
            );
            """,
            new { Id = areaId },
            cancellationToken: cancellationToken));
    }

    public async Task SoftDeleteIfNoShelfOrHistoryAsync(
        Guid areaId,
        DateTimeOffset deletedAt,
        CancellationToken cancellationToken)
    {
        await _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            var area = await connection.QuerySingleOrDefaultAsync<AreaLockRow>(new CommandDefinition(
                """
                select id, is_deleted
                from areas
                where id = @Id
                for update;
                """,
                new { Id = areaId },
                transaction,
                cancellationToken: token));
            if (area is null || area.IsDeleted)
            {
                throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Area was not found.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                select id
                from shelves
                where area_id = @Id
                order by id
                for update;
                """,
                new { Id = areaId },
                transaction,
                cancellationToken: token));

            var blocked = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                select exists (
                    select 1 from shelves where area_id = @Id and is_deleted = false
                    union all
                    select 1
                    from shelves s
                    join stock_balances b on b.shelf_id = s.id
                    where s.area_id = @Id
                    union all
                    select 1
                    from shelves s
                    join stock_movements m on m.shelf_id = s.id or m.other_shelf_id = s.id
                    where s.area_id = @Id
                );
                """,
                new { Id = areaId },
                transaction,
                cancellationToken: token));
            if (blocked)
            {
                throw new InventoryRuleException(
                    InventoryRuleFailure.Conflict,
                    "Area cannot be deleted because it has a shelf or stock history.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                update areas
                set is_deleted = true,
                    updated_at = @DeletedAt
                where id = @Id and is_deleted = false;
                """,
                new { Id = areaId, DeletedAt = deletedAt },
                transaction,
                cancellationToken: token));
        }, cancellationToken);
    }

    public Task AddAsync(Area area, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            insert into areas (id, name, is_active, created_at, updated_at, is_deleted)
            values (@Id, @Name, @IsActive, @CreatedAt, @UpdatedAt, @IsDeleted);
            """,
            area,
            requireRow: false,
            cancellationToken);
    }

    public Task UpdateAsync(Area area, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            update areas
            set name = @Name,
                is_active = @IsActive,
                updated_at = @UpdatedAt,
                is_deleted = @IsDeleted
            where id = @Id and is_deleted = false;
            """,
            area,
            requireRow: true,
            cancellationToken);
    }

    private async Task ExecuteAsync(string sql, Area area, bool requireRow, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
            var rows = await connection.ExecuteAsync(new CommandDefinition(sql, area, cancellationToken: cancellationToken));
            if (requireRow && rows == 0)
            {
                throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Area was not found.");
            }
        }
        catch (InventoryRuleException)
        {
            throw;
        }
        catch (Exception exception) when (PostgresUnique.AsConflict(exception, "An area with this name already exists.") is { } conflict)
        {
            throw new InventoryRuleException(InventoryRuleFailure.Conflict, conflict.Message);
        }
    }

    private static Area ToArea(AreaRow row)
    {
        return Area.Restore(row.Id, row.Name, row.IsActive, row.CreatedAt, row.UpdatedAt, row.IsDeleted);
    }

    private sealed class AreaLockRow
    {
        public Guid Id { get; init; }

        public bool IsDeleted { get; init; }
    }

    private sealed class AreaRow
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public bool IsActive { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public bool IsDeleted { get; init; }
    }
}
