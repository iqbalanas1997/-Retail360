using Dapper;
using Npgsql;
using Retail360.Application.Inventory;
using Retail360.Domain.Inventory;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Inventory;

public sealed class StockLedgerStore : IStockLedger, IInventoryProductLookup
{
    private readonly NpgsqlConnectionFactory _connections;
    private readonly PostgresTransaction _transactions;

    public StockLedgerStore(NpgsqlConnectionFactory connections, PostgresTransaction transactions)
    {
        _connections = connections;
        _transactions = transactions;
    }

    public async Task<InventoryProduct?> FindAvailableAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ProductReadRow>(new CommandDefinition(
            """
            select id, name, minimum_stock, is_active
            from products
            where id = @Id and is_deleted = false;
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
        return row is null ? null : new InventoryProduct(row.Id, row.Name, row.MinimumStock, row.IsActive);
    }

    public async Task<IReadOnlyList<StockMovementResponse>> PostAsync(StockPosting posting, CancellationToken cancellationToken)
    {
        if (posting.Lines.Count == 0)
        {
            throw new InventoryRuleException(InventoryRuleFailure.Invalid, "A stock operation needs a movement.");
        }

        var shelfIds = posting.Lines.Select(line => line.ShelfId).Distinct().OrderBy(id => id).ToArray();
        try
        {
            return await _transactions.ExecuteAsync(async (connection, transaction, token) =>
            {
                var product = await connection.QuerySingleOrDefaultAsync<ProductLockRow>(new CommandDefinition(
                    """
                    select id, is_deleted
                    from products
                    where id = @Id
                    for update;
                    """,
                    new { Id = posting.ProductId },
                    transaction,
                    cancellationToken: token));
                if (product is null)
                {
                    throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Product was not found.");
                }

                var inserted = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                    """
                    insert into stock_operations (id, product_id, fingerprint, created_at)
                    values (@Id, @ProductId, @Fingerprint, @CreatedAt)
                    on conflict (id) do nothing
                    returning id;
                    """,
                    new
                    {
                        Id = posting.OperationId,
                        posting.ProductId,
                        posting.Fingerprint,
                        CreatedAt = posting.OccurredAt
                    },
                    transaction,
                    cancellationToken: token));
                if (inserted is null)
                {
                    var existing = await connection.QuerySingleAsync<OperationRow>(new CommandDefinition(
                        """
                        select fingerprint
                        from stock_operations
                        where id = @Id
                        for update;
                        """,
                        new { Id = posting.OperationId },
                        transaction,
                        cancellationToken: token));
                    if (!string.Equals(existing.Fingerprint, posting.Fingerprint, StringComparison.Ordinal))
                    {
                        throw new InventoryRuleException(
                            InventoryRuleFailure.Conflict,
                            "This operation key was already used for a different stock change.");
                    }

                    return await ReadOperationAsync(connection, transaction, posting.OperationId, token);
                }

                if (product.IsDeleted)
                {
                    throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Product was not found.");
                }

                var shelves = (await connection.QueryAsync<ShelfLockRow>(new CommandDefinition(
                    """
                    select id, is_active, is_deleted
                    from shelves
                    where id = any(@Ids)
                    order by id
                    for update;
                    """,
                    new { Ids = shelfIds },
                    transaction,
                    cancellationToken: token))).ToDictionary(row => row.Id);

                foreach (var line in posting.Lines)
                {
                    if (!shelves.TryGetValue(line.ShelfId, out var shelf) || shelf.IsDeleted)
                    {
                        throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Shelf was not found.");
                    }

                    if (line.Direction == StockDirection.In && !shelf.IsActive)
                    {
                        throw new InventoryRuleException(
                            InventoryRuleFailure.Conflict,
                            "Shelf is inactive and cannot receive stock.");
                    }
                }

                foreach (var shelfId in shelfIds)
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        insert into stock_balances (id, product_id, shelf_id, quantity, created_at, updated_at)
                        values (@Id, @ProductId, @ShelfId, 0, @Now, @Now)
                        on conflict (product_id, shelf_id) do nothing;
                        """,
                        new { Id = Guid.NewGuid(), posting.ProductId, ShelfId = shelfId, Now = posting.OccurredAt },
                        transaction,
                        cancellationToken: token));
                }

                var locked = (await connection.QueryAsync<BalanceLockRow>(new CommandDefinition(
                    """
                    select shelf_id, quantity
                    from stock_balances
                    where product_id = @ProductId and shelf_id = any(@ShelfIds)
                    order by shelf_id
                    for update;
                    """,
                    new { posting.ProductId, ShelfIds = shelfIds },
                    transaction,
                    cancellationToken: token))).ToDictionary(row => row.ShelfId);

                var deltas = new Dictionary<Guid, decimal>();
                foreach (var line in posting.Lines)
                {
                    var signed = line.Direction == StockDirection.In ? line.Quantity : -line.Quantity;
                    deltas[line.ShelfId] = deltas.GetValueOrDefault(line.ShelfId) + signed;
                }

                foreach (var shelfId in shelfIds)
                {
                    var next = locked[shelfId].Quantity + deltas.GetValueOrDefault(shelfId);
                    if (next < 0)
                    {
                        throw new InventoryRuleException(
                            InventoryRuleFailure.Conflict,
                            "Shelf does not have enough stock.");
                    }

                    if (next > InventoryText.MaxQuantity)
                    {
                        throw new InventoryRuleException(
                            InventoryRuleFailure.Conflict,
                            "Shelf quantity exceeds the maximum.");
                    }

                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        update stock_balances
                        set quantity = @Quantity, updated_at = @UpdatedAt
                        where product_id = @ProductId and shelf_id = @ShelfId;
                        """,
                        new { Quantity = next, UpdatedAt = posting.OccurredAt, posting.ProductId, ShelfId = shelfId },
                        transaction,
                        cancellationToken: token));
                }

                var rows = posting.Lines.Select(line => new
                {
                    Id = Guid.NewGuid(),
                    posting.OperationId,
                    posting.ProductId,
                    line.ShelfId,
                    line.Quantity,
                    Direction = StockCodes.ToCode(line.Direction),
                    MovementType = StockCodes.ToCode(line.MovementType),
                    line.Reason,
                    line.OtherShelfId,
                    OccurredAt = posting.OccurredAt,
                    CreatedAt = posting.OccurredAt
                }).ToArray();
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    insert into stock_movements (
                        id, operation_id, product_id, shelf_id, quantity, direction, movement_type,
                        reason, other_shelf_id, occurred_at, created_at)
                    values (
                        @Id, @OperationId, @ProductId, @ShelfId, @Quantity, @Direction, @MovementType,
                        @Reason, @OtherShelfId, @OccurredAt, @CreatedAt);
                    """,
                    rows,
                    transaction,
                    cancellationToken: token));
                return await ReadOperationAsync(connection, transaction, posting.OperationId, token);
            }, cancellationToken);
        }
        catch (InventoryRuleException)
        {
            throw;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.CheckViolation)
        {
            var message = exception.ConstraintName == "ck_stock_balances_quantity"
                ? "Shelf does not have enough stock."
                : "The stock change was rejected.";
            throw new InventoryRuleException(InventoryRuleFailure.Conflict, message);
        }
    }

    public async Task<IReadOnlyList<ShelfBalanceResponse>> ListBalancesAsync(
        Guid? productId,
        Guid? shelfId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<BalanceReadRow>(new CommandDefinition(
            """
            select
                b.product_id,
                p.name as product_name,
                b.shelf_id,
                s.name as shelf_name,
                s.area_id,
                a.name as area_name,
                b.quantity,
                s.is_active as shelf_is_active
            from stock_balances b
            join products p on p.id = b.product_id and p.is_deleted = false
            join shelves s on s.id = b.shelf_id and s.is_deleted = false
            join areas a on a.id = s.area_id
            where (@ProductId is null or b.product_id = @ProductId)
              and (@ShelfId is null or b.shelf_id = @ShelfId)
            order by lower(p.name), lower(a.name), lower(s.name);
            """,
            new { ProductId = productId, ShelfId = shelfId },
            cancellationToken: cancellationToken));
        return rows.Select(row => new ShelfBalanceResponse(
            row.ProductId,
            row.ProductName,
            row.ShelfId,
            row.ShelfName,
            row.AreaId,
            row.AreaName,
            row.Quantity,
            row.ShelfIsActive)).ToArray();
    }

    public async Task<IReadOnlyList<StockMovementResponse>> ListMovementsAsync(
        Guid? productId,
        Guid? shelfId,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<MovementRow>(new CommandDefinition(
            """
            select id, operation_id, product_id, shelf_id, quantity, direction, movement_type,
                   reason, other_shelf_id, occurred_at
            from stock_movements
            where (@ProductId is null or product_id = @ProductId)
              and (@ShelfId is null or shelf_id = @ShelfId)
            order by occurred_at, id
            limit @Limit offset @Offset;
            """,
            new { ProductId = productId, ShelfId = shelfId, Limit = limit, Offset = offset },
            cancellationToken: cancellationToken));
        return rows.Select(ToMovement).ToArray();
    }

    public async Task<IReadOnlyList<ProductStockResponse>> ListProductStockAsync(
        string? status,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ProductStockRow>(new CommandDefinition(
            """
            with stock as (
                select
                    p.id as product_id,
                    p.name as product_name,
                    p.is_active as product_is_active,
                    coalesce(sum(b.quantity), 0) as on_hand,
                    p.minimum_stock
                from products p
                left join stock_balances b on b.product_id = p.id
                where p.is_deleted = false
                group by p.id, p.name, p.is_active, p.minimum_stock
            )
            select product_id, product_name, product_is_active, on_hand, minimum_stock
            from stock
            where @Status = 'all'
               or (@Status = 'out' and on_hand = 0)
               or (@Status = 'low' and on_hand > 0 and on_hand <= minimum_stock)
            order by lower(product_name), product_id
            limit @Limit offset @Offset;
            """,
            new { Status = status, Limit = limit, Offset = offset },
            cancellationToken: cancellationToken));
        return rows.Select(row =>
        {
            var stockStatus = row.OnHand == 0
                ? "out"
                : row.OnHand <= row.MinimumStock ? "low" : "ok";
            return new ProductStockResponse(
                row.ProductId,
                row.ProductName,
                row.ProductIsActive,
                row.OnHand,
                row.MinimumStock,
                stockStatus);
        }).ToArray();
    }

    public async Task<IReadOnlyList<ReconciliationRow>> ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ReconciliationReadRow>(new CommandDefinition(
            """
            with nets as (
                select
                    product_id,
                    shelf_id,
                    sum(case when direction = 'in' then quantity else -quantity end) as ledger_quantity
                from stock_movements
                group by product_id, shelf_id
            )
            select
                coalesce(b.product_id, n.product_id) as product_id,
                coalesce(b.shelf_id, n.shelf_id) as shelf_id,
                coalesce(b.quantity, 0) as stored_quantity,
                coalesce(n.ledger_quantity, 0) as ledger_quantity
            from stock_balances b
            full outer join nets n
                on n.product_id = b.product_id and n.shelf_id = b.shelf_id
            where coalesce(b.quantity, 0) <> coalesce(n.ledger_quantity, 0)
            order by 1, 2;
            """,
            cancellationToken: cancellationToken));
        return rows.Select(row => new ReconciliationRow(
            row.ProductId,
            row.ShelfId,
            row.StoredQuantity,
            row.LedgerQuantity)).ToArray();
    }

    private static async Task<IReadOnlyList<StockMovementResponse>> ReadOperationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<MovementRow>(new CommandDefinition(
            """
            select id, operation_id, product_id, shelf_id, quantity, direction, movement_type,
                   reason, other_shelf_id, occurred_at
            from stock_movements
            where operation_id = @OperationId
            order by direction desc, id;
            """,
            new { OperationId = operationId },
            transaction,
            cancellationToken: cancellationToken));
        return rows.Select(ToMovement).ToArray();
    }

    private static StockMovementResponse ToMovement(MovementRow row)
    {
        return new StockMovementResponse(
            row.Id,
            row.OperationId,
            row.ProductId,
            row.ShelfId,
            row.Quantity,
            row.Direction,
            row.MovementType,
            row.Reason,
            row.OtherShelfId,
            row.OccurredAt);
    }

    private sealed class OperationRow
    {
        public string Fingerprint { get; init; } = string.Empty;
    }

    private sealed class ProductLockRow
    {
        public Guid Id { get; init; }

        public bool IsDeleted { get; init; }
    }

    private sealed class ProductReadRow
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public decimal MinimumStock { get; init; }

        public bool IsActive { get; init; }
    }

    private sealed class ShelfLockRow
    {
        public Guid Id { get; init; }

        public bool IsActive { get; init; }

        public bool IsDeleted { get; init; }
    }

    private sealed class BalanceLockRow
    {
        public Guid ShelfId { get; init; }

        public decimal Quantity { get; init; }
    }

    private sealed class MovementRow
    {
        public Guid Id { get; init; }

        public Guid OperationId { get; init; }

        public Guid ProductId { get; init; }

        public Guid ShelfId { get; init; }

        public decimal Quantity { get; init; }

        public string Direction { get; init; } = string.Empty;

        public string MovementType { get; init; } = string.Empty;

        public string Reason { get; init; } = string.Empty;

        public Guid? OtherShelfId { get; init; }

        public DateTimeOffset OccurredAt { get; init; }
    }

    private sealed class BalanceReadRow
    {
        public Guid ProductId { get; init; }

        public string ProductName { get; init; } = string.Empty;

        public Guid ShelfId { get; init; }

        public string ShelfName { get; init; } = string.Empty;

        public Guid AreaId { get; init; }

        public string AreaName { get; init; } = string.Empty;

        public decimal Quantity { get; init; }

        public bool ShelfIsActive { get; init; }
    }

    private sealed class ReconciliationReadRow
    {
        public Guid ProductId { get; init; }

        public Guid ShelfId { get; init; }

        public decimal StoredQuantity { get; init; }

        public decimal LedgerQuantity { get; init; }
    }

    private sealed class ProductStockRow
    {
        public Guid ProductId { get; init; }

        public string ProductName { get; init; } = string.Empty;

        public bool ProductIsActive { get; init; }

        public decimal OnHand { get; init; }

        public decimal MinimumStock { get; init; }
    }
}
