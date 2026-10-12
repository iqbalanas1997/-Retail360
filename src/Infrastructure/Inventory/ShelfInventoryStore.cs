using Dapper;
using Retail360.Application.Inventory;
using Retail360.Domain.Inventory;
using Retail360.Infrastructure.Catalog;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Inventory;

public sealed class ShelfInventoryStore : IShelfStore
{
    private readonly NpgsqlConnectionFactory _connections;
    private readonly PostgresTransaction _transactions;

    public ShelfInventoryStore(NpgsqlConnectionFactory connections, PostgresTransaction transactions)
    {
        _connections = connections;
        _transactions = transactions;
    }

    public async Task<Shelf?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ShelfWriteRow>(new CommandDefinition(
            """
            select id, area_id, name, is_active, created_at, updated_at, is_deleted
            from shelves
            where id = @Id and is_deleted = false;
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
        return row is null ? null : Shelf.Restore(row.Id, row.AreaId, row.Name, row.IsActive, row.CreatedAt, row.UpdatedAt, row.IsDeleted);
    }

    public async Task<ShelfResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ShelfReadRow>(new CommandDefinition(
            $"""
            select {ShelfColumns}
            {ShelfJoins}
            where s.id = @Id and s.is_deleted = false;
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
        return row is null ? null : ToResponse(row);
    }

    public async Task<IReadOnlyList<ShelfResponse>> ListAsync(Guid? areaId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ShelfReadRow>(new CommandDefinition(
            $"""
            select {ShelfColumns}
            {ShelfJoins}
            where s.is_deleted = false
              and (@AreaId is null or s.area_id = @AreaId)
            order by lower(a.name), lower(s.name), s.id;
            """,
            new { AreaId = areaId },
            cancellationToken: cancellationToken));
        return rows.Select(ToResponse).ToArray();
    }

    public async Task<bool> HasHistoryAsync(Guid shelfId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists (
                select 1 from stock_balances where shelf_id = @Id
                union all
                select 1 from stock_movements where shelf_id = @Id or other_shelf_id = @Id
            );
            """,
            new { Id = shelfId },
            cancellationToken: cancellationToken));
    }

    public async Task SoftDeleteIfNoHistoryAsync(Guid shelfId, DateTimeOffset deletedAt, CancellationToken cancellationToken)
    {
        await _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            var shelf = await connection.QuerySingleOrDefaultAsync<ShelfLockRow>(new CommandDefinition(
                """
                select id, is_deleted
                from shelves
                where id = @Id
                for update;
                """,
                new { Id = shelfId },
                transaction,
                cancellationToken: token));
            if (shelf is null || shelf.IsDeleted)
            {
                throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Shelf was not found.");
            }

            var hasHistory = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                select exists (
                    select 1 from stock_balances where shelf_id = @Id
                    union all
                    select 1 from stock_movements where shelf_id = @Id or other_shelf_id = @Id
                );
                """,
                new { Id = shelfId },
                transaction,
                cancellationToken: token));
            if (hasHistory)
            {
                throw new InventoryRuleException(
                    InventoryRuleFailure.Conflict,
                    "Shelf cannot be deleted because it has stock history.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                update shelves
                set is_deleted = true,
                    updated_at = @DeletedAt
                where id = @Id and is_deleted = false;
                """,
                new { Id = shelfId, DeletedAt = deletedAt },
                transaction,
                cancellationToken: token));
        }, cancellationToken);
    }

    public async Task AddAsync(Shelf shelf, CancellationToken cancellationToken)
    {
        try
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
                    new { Id = shelf.AreaId },
                    transaction,
                    cancellationToken: token));
                if (area is null || area.IsDeleted)
                {
                    throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Area was not found.");
                }

                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    insert into shelves (id, area_id, name, is_active, created_at, updated_at, is_deleted)
                    values (@Id, @AreaId, @Name, @IsActive, @CreatedAt, @UpdatedAt, @IsDeleted);
                    """,
                    shelf,
                    transaction,
                    cancellationToken: token));
            }, cancellationToken);
        }
        catch (InventoryRuleException)
        {
            throw;
        }
        catch (Exception exception) when (PostgresUnique.AsConflict(exception, "A shelf with this name already exists in the area.") is { } conflict)
        {
            throw new InventoryRuleException(InventoryRuleFailure.Conflict, conflict.Message);
        }
    }

    public Task UpdateAsync(Shelf shelf, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            update shelves
            set name = @Name,
                is_active = @IsActive,
                updated_at = @UpdatedAt,
                is_deleted = @IsDeleted
            where id = @Id and is_deleted = false;
            """,
            shelf,
            requireRow: true,
            cancellationToken);
    }

    private async Task ExecuteAsync(string sql, Shelf shelf, bool requireRow, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
            var rows = await connection.ExecuteAsync(new CommandDefinition(sql, shelf, cancellationToken: cancellationToken));
            if (requireRow && rows == 0)
            {
                throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Shelf was not found.");
            }
        }
        catch (InventoryRuleException)
        {
            throw;
        }
        catch (Exception exception) when (PostgresUnique.AsConflict(exception, "A shelf with this name already exists in the area.") is { } conflict)
        {
            throw new InventoryRuleException(InventoryRuleFailure.Conflict, conflict.Message);
        }
    }

    private const string ShelfColumns = """
        s.id,
        s.area_id,
        a.name as area_name,
        s.name,
        s.is_active,
        s.created_at,
        s.updated_at
        """;

    private const string ShelfJoins = """
        from shelves s
        join areas a on a.id = s.area_id and a.is_deleted = false
        """;

    private static ShelfResponse ToResponse(ShelfReadRow row)
    {
        return new ShelfResponse(row.Id, row.AreaId, row.AreaName, row.Name, row.IsActive, row.CreatedAt, row.UpdatedAt);
    }

    private sealed class ShelfReadRow
    {
        public Guid Id { get; init; }

        public Guid AreaId { get; init; }

        public string AreaName { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public bool IsActive { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class AreaLockRow
    {
        public Guid Id { get; init; }

        public bool IsDeleted { get; init; }
    }

    private sealed class ShelfLockRow
    {
        public Guid Id { get; init; }

        public bool IsDeleted { get; init; }
    }

    private sealed class ShelfWriteRow
    {
        public Guid Id { get; init; }

        public Guid AreaId { get; init; }

        public string Name { get; init; } = string.Empty;

        public bool IsActive { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public bool IsDeleted { get; init; }
    }
}
