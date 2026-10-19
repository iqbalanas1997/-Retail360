using System.Globalization;
using Dapper;
using Npgsql;
using Retail360.Application.Inventory;
using Retail360.Application.Purchasing;
using Retail360.Domain.Inventory;
using Retail360.Domain.Purchasing;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Inventory;

namespace Retail360.Infrastructure.Purchasing;

public sealed class PurchaseStore : IPurchaseStore
{
    private readonly NpgsqlConnectionFactory _connections;
    private readonly PostgresTransaction _transactions;
    private readonly StockLedgerStore _ledger;

    public PurchaseStore(
        NpgsqlConnectionFactory connections,
        PostgresTransaction transactions,
        StockLedgerStore ledger)
    {
        _connections = connections;
        _transactions = transactions;
        _ledger = ledger;
    }

    public async Task<PurchaseResponse?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(connection, null, id, cancellationToken);
    }

    public async Task<IReadOnlyList<PurchaseResponse>> ListAsync(
        Guid? supplierId,
        string? status,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var headers = (await connection.QueryAsync<PurchaseRow>(new CommandDefinition(
            """
            select
                p.id, p.supplier_id, s.name as supplier_name, p.purchase_date, p.reference, p.notes,
                p.status, p.total, p.receive_operation_id, p.received_at, p.created_at, p.updated_at
            from purchases p
            join suppliers s on s.id = p.supplier_id
            where p.is_deleted = false
              and (@SupplierId is null or p.supplier_id = @SupplierId)
              and (@Status is null or p.status = @Status)
            order by p.purchase_date desc, p.created_at desc, p.id
            limit @Limit offset @Offset;
            """,
            new { SupplierId = supplierId, Status = status, Limit = limit, Offset = offset },
            cancellationToken: cancellationToken))).ToArray();
        if (headers.Length == 0)
        {
            return [];
        }

        var lines = await LoadLinesAsync(connection, null, headers.Select(header => header.Id).ToArray(), cancellationToken);
        return headers.Select(header => ToResponse(header, lines)).ToArray();
    }

    public Task<PurchaseResponse> AddAsync(Purchase purchase, CancellationToken cancellationToken)
    {
        return _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            await RequireActiveSupplierAsync(connection, transaction, purchase.SupplierId, token);
            await RequireProductsAsync(connection, transaction, purchase.Lines.Where(line => !line.IsDeleted).Select(line => line.ProductId), token);
            await InsertPurchaseAsync(connection, transaction, purchase, token);
            await InsertLinesAsync(connection, transaction, purchase, token);
            return (await ReadAsync(connection, transaction, purchase.Id, token))!;
        }, cancellationToken);
    }

    public Task<PurchaseResponse> ReplaceAsync(Purchase purchase, CancellationToken cancellationToken)
    {
        return _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            var status = await LockStatusAsync(connection, transaction, purchase.Id, token);
            if (status != PurchaseText.Recorded)
            {
                throw new PurchaseRuleException(PurchaseRuleFailure.Conflict, "A received purchase cannot be changed.");
            }

            await RequireActiveSupplierAsync(connection, transaction, purchase.SupplierId, token);
            var active = purchase.Lines.Where(line => !line.IsDeleted).ToArray();
            await RequireProductsAsync(connection, transaction, active.Select(line => line.ProductId), token);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                update purchases
                set supplier_id = @SupplierId,
                    purchase_date = @PurchaseDate,
                    reference = @Reference,
                    notes = @Notes,
                    total = @Total,
                    updated_at = @UpdatedAt
                where id = @Id and is_deleted = false and status = 'recorded';
                """,
                new
                {
                    purchase.Id,
                    purchase.SupplierId,
                    PurchaseDate = purchase.PurchaseDate.ToDateTime(TimeOnly.MinValue),
                    purchase.Reference,
                    purchase.Notes,
                    purchase.Total,
                    purchase.UpdatedAt
                },
                transaction,
                cancellationToken: token));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                update purchase_lines
                set is_deleted = true, updated_at = @UpdatedAt
                where purchase_id = @PurchaseId and is_deleted = false;
                """,
                new { PurchaseId = purchase.Id, purchase.UpdatedAt },
                transaction,
                cancellationToken: token));
            await InsertLinesAsync(connection, transaction, purchase, token);
            return (await ReadAsync(connection, transaction, purchase.Id, token))!;
        }, cancellationToken);
    }

    public Task DeleteAsync(Purchase purchase, CancellationToken cancellationToken)
    {
        return _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            var status = await LockStatusAsync(connection, transaction, purchase.Id, token);
            if (status != PurchaseText.Recorded)
            {
                throw new PurchaseRuleException(PurchaseRuleFailure.Conflict, "A received purchase cannot be deleted.");
            }

            var rows = await connection.ExecuteAsync(new CommandDefinition(
                """
                update purchases
                set is_deleted = true, updated_at = @UpdatedAt
                where id = @Id and is_deleted = false and status = 'recorded';
                """,
                new { purchase.Id, purchase.UpdatedAt },
                transaction,
                cancellationToken: token));
            if (rows != 1)
            {
                throw new PurchaseRuleException(PurchaseRuleFailure.NotFound, "Purchase was not found.");
            }

            return 0;
        }, cancellationToken);
    }

    public Task<PurchaseResponse> ReceiveAsync(
        Guid purchaseId,
        Guid operationId,
        IReadOnlyDictionary<Guid, Guid> shelves,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        return _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            var header = await connection.QuerySingleOrDefaultAsync<ReceiveLockRow>(new CommandDefinition(
                """
                select id, status, receive_operation_id, receive_fingerprint, is_deleted
                from purchases
                where id = @Id
                for update;
                """,
                new { Id = purchaseId },
                transaction,
                cancellationToken: token));
            if (header is null || header.IsDeleted)
            {
                throw new PurchaseRuleException(PurchaseRuleFailure.NotFound, "Purchase was not found.");
            }

            var lines = (await connection.QueryAsync<ReceiveLineRow>(new CommandDefinition(
                """
                select id, product_id, quantity, line_number
                from purchase_lines
                where purchase_id = @Id and is_deleted = false
                order by line_number
                for update;
                """,
                new { Id = purchaseId },
                transaction,
                cancellationToken: token))).ToArray();
            var fingerprint = Fingerprint(lines, shelves);
            if (header.Status == PurchaseText.Received)
            {
                if (header.ReceiveOperationId == operationId
                    && string.Equals(header.ReceiveFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    return (await ReadAsync(connection, transaction, purchaseId, token))!;
                }

                throw new PurchaseRuleException(
                    PurchaseRuleFailure.Conflict,
                    header.ReceiveOperationId == operationId
                        ? "This operation key was already used for a different stock change."
                        : "This purchase was already received.");
            }

            if (!SameLines(lines, shelves))
            {
                throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, "Receive each purchase line exactly once.");
            }

            var taken = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                select exists (
                    select 1 from purchases
                    where receive_operation_id = @OperationId and id <> @Id);
                """,
                new { OperationId = operationId, Id = purchaseId },
                transaction,
                cancellationToken: token));
            if (taken)
            {
                throw new PurchaseRuleException(
                    PurchaseRuleFailure.Conflict,
                    "This operation key was already used for a different stock change.");
            }

            var reason = "Purchase " + purchaseId.ToString("N");
            foreach (var group in lines.GroupBy(line => line.ProductId).OrderBy(group => group.Key))
            {
                var ordered = group.OrderBy(line => line.LineNumber).ToArray();
                var stockLines = ordered.Select(line => new StockLine(
                    shelves[line.Id],
                    line.Quantity,
                    StockDirection.In,
                    StockMovementType.Purchase,
                    reason,
                    null)).ToArray();
                var stockFingerprint = string.Join(
                    '|',
                    new[] { "purchase", group.Key.ToString("N"), reason }.Concat(ordered.Select(line =>
                        line.Id.ToString("N")
                        + "|"
                        + shelves[line.Id].ToString("N")
                        + "|"
                        + line.Quantity.ToString("0.000", CultureInfo.InvariantCulture))));
                await _ledger.PostInTransactionAsync(
                    connection,
                    transaction,
                    new StockPosting(
                        PurchaseText.StockOperationId(operationId, group.Key),
                        stockFingerprint,
                        group.Key,
                        stockLines,
                        utcNow),
                    token);
            }

            foreach (var line in lines)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    update purchase_lines
                    set shelf_id = @ShelfId, updated_at = @UpdatedAt
                    where id = @Id and purchase_id = @PurchaseId and is_deleted = false;
                    """,
                    new { line.Id, ShelfId = shelves[line.Id], UpdatedAt = utcNow, PurchaseId = purchaseId },
                    transaction,
                    cancellationToken: token));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                update purchases
                set status = 'received',
                    receive_operation_id = @OperationId,
                    receive_fingerprint = @Fingerprint,
                    received_at = @ReceivedAt,
                    updated_at = @ReceivedAt
                where id = @Id and status = 'recorded' and is_deleted = false;
                """,
                new { Id = purchaseId, OperationId = operationId, Fingerprint = fingerprint, ReceivedAt = utcNow },
                transaction,
                cancellationToken: token));
            return (await ReadAsync(connection, transaction, purchaseId, token))!;
        }, cancellationToken);
    }

    private static bool SameLines(IReadOnlyList<ReceiveLineRow> lines, IReadOnlyDictionary<Guid, Guid> shelves)
    {
        if (lines.Count != shelves.Count)
        {
            return false;
        }

        foreach (var line in lines)
        {
            if (!shelves.ContainsKey(line.Id))
            {
                return false;
            }
        }

        return true;
    }

    private static string Fingerprint(IReadOnlyList<ReceiveLineRow> lines, IReadOnlyDictionary<Guid, Guid> shelves)
    {
        if (!SameLines(lines, shelves))
        {
            return string.Empty;
        }

        return string.Join(
            "|",
            lines.OrderBy(line => line.LineNumber).Select(line => line.Id.ToString("N") + "|" + shelves[line.Id].ToString("N")));
    }

    private async Task<string> LockStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<StatusRow>(new CommandDefinition(
            """
            select status, is_deleted
            from purchases
            where id = @Id
            for update;
            """,
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));
        if (row is null || row.IsDeleted)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.NotFound, "Purchase was not found.");
        }

        return row.Status;
    }

    private static async Task RequireActiveSupplierAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid supplierId,
        CancellationToken cancellationToken)
    {
        var supplier = await connection.QuerySingleOrDefaultAsync<ActiveRow>(new CommandDefinition(
            """
            select is_active, is_deleted
            from suppliers
            where id = @Id
            for update;
            """,
            new { Id = supplierId },
            transaction,
            cancellationToken: cancellationToken));
        if (supplier is null || supplier.IsDeleted)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.NotFound, "Supplier was not found.");
        }

        if (!supplier.IsActive)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Conflict, "Supplier is inactive.");
        }
    }

    private static async Task RequireProductsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var ids = productIds.Distinct().OrderBy(id => id).ToArray();
        var rows = (await connection.QueryAsync<ActiveRow>(new CommandDefinition(
            """
            select is_active, is_deleted
            from products
            where id = any(@Ids)
            order by id
            for update;
            """,
            new { Ids = ids },
            transaction,
            cancellationToken: cancellationToken))).ToArray();
        if (rows.Length != ids.Length || rows.Any(row => row.IsDeleted))
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.NotFound, "Product was not found.");
        }
    }

    private static async Task InsertPurchaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Purchase purchase,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into purchases (
                id, supplier_id, purchase_date, reference, notes, status, total,
                receive_operation_id, receive_fingerprint, received_at, created_at, updated_at, is_deleted)
            values (
                @Id, @SupplierId, @PurchaseDate, @Reference, @Notes, @Status, @Total,
                null, null, null, @CreatedAt, @UpdatedAt, false);
            """,
            new
            {
                purchase.Id,
                purchase.SupplierId,
                PurchaseDate = purchase.PurchaseDate.ToDateTime(TimeOnly.MinValue),
                purchase.Reference,
                purchase.Notes,
                purchase.Status,
                purchase.Total,
                purchase.CreatedAt,
                purchase.UpdatedAt
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task InsertLinesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Purchase purchase,
        CancellationToken cancellationToken)
    {
        var rows = purchase.Lines.Where(line => !line.IsDeleted).Select(line => new
        {
            line.Id,
            PurchaseId = purchase.Id,
            line.ProductId,
            line.LineNumber,
            line.Quantity,
            line.UnitCost,
            line.LineTotal,
            line.ShelfId,
            CreatedAt = purchase.UpdatedAt,
            UpdatedAt = purchase.UpdatedAt
        }).ToArray();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into purchase_lines (
                id, purchase_id, product_id, line_number, quantity, unit_cost, line_total, shelf_id,
                created_at, updated_at, is_deleted)
            values (
                @Id, @PurchaseId, @ProductId, @LineNumber, @Quantity, @UnitCost, @LineTotal, @ShelfId,
                @CreatedAt, @UpdatedAt, false);
            """,
            rows,
            transaction,
            cancellationToken: cancellationToken));
    }

    private async Task<PurchaseResponse?> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        var header = await connection.QuerySingleOrDefaultAsync<PurchaseRow>(new CommandDefinition(
            """
            select
                p.id, p.supplier_id, s.name as supplier_name, p.purchase_date, p.reference, p.notes,
                p.status, p.total, p.receive_operation_id, p.received_at, p.created_at, p.updated_at
            from purchases p
            join suppliers s on s.id = p.supplier_id
            where p.id = @Id and p.is_deleted = false;
            """,
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));
        if (header is null)
        {
            return null;
        }

        var lines = await LoadLinesAsync(connection, transaction, [id], cancellationToken);
        return ToResponse(header, lines);
    }

    private static async Task<ILookup<Guid, PurchaseLineRow>> LoadLinesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid[] purchaseIds,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<PurchaseLineRow>(new CommandDefinition(
            """
            select
                l.purchase_id, l.id, l.product_id, p.name as product_name,
                l.quantity, l.unit_cost, l.line_total, l.shelf_id, l.line_number
            from purchase_lines l
            join products p on p.id = l.product_id
            where l.purchase_id = any(@Ids) and l.is_deleted = false
            order by l.line_number, l.id;
            """,
            new { Ids = purchaseIds },
            transaction,
            cancellationToken: cancellationToken));
        return rows.ToLookup(row => row.PurchaseId);
    }

    private static PurchaseResponse ToResponse(PurchaseRow header, ILookup<Guid, PurchaseLineRow> lines)
    {
        return new PurchaseResponse(
            header.Id,
            header.SupplierId,
            header.SupplierName,
            DateOnly.FromDateTime(header.PurchaseDate),
            header.Reference,
            header.Notes,
            header.Status,
            header.Total,
            header.ReceiveOperationId,
            header.ReceivedAt,
            lines[header.Id].Select(line => new PurchaseLineResponse(
                line.Id,
                line.ProductId,
                line.ProductName,
                line.Quantity,
                line.UnitCost,
                line.LineTotal,
                line.ShelfId)).ToArray(),
            header.CreatedAt,
            header.UpdatedAt);
    }

    private sealed class PurchaseRow
    {
        public Guid Id { get; init; }

        public Guid SupplierId { get; init; }

        public string SupplierName { get; init; } = string.Empty;

        public DateTime PurchaseDate { get; init; }

        public string? Reference { get; init; }

        public string? Notes { get; init; }

        public string Status { get; init; } = string.Empty;

        public decimal Total { get; init; }

        public Guid? ReceiveOperationId { get; init; }

        public DateTimeOffset? ReceivedAt { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class PurchaseLineRow
    {
        public Guid PurchaseId { get; init; }

        public Guid Id { get; init; }

        public Guid ProductId { get; init; }

        public string ProductName { get; init; } = string.Empty;

        public decimal Quantity { get; init; }

        public decimal UnitCost { get; init; }

        public decimal LineTotal { get; init; }

        public Guid? ShelfId { get; init; }
    }

    private sealed class ReceiveLockRow
    {
        public Guid Id { get; init; }

        public string Status { get; init; } = string.Empty;

        public Guid? ReceiveOperationId { get; init; }

        public string? ReceiveFingerprint { get; init; }

        public bool IsDeleted { get; init; }
    }

    private sealed class ReceiveLineRow
    {
        public Guid Id { get; init; }

        public Guid ProductId { get; init; }

        public decimal Quantity { get; init; }

        public int LineNumber { get; init; }
    }

    private sealed class StatusRow
    {
        public string Status { get; init; } = string.Empty;

        public bool IsDeleted { get; init; }
    }

    private sealed class ActiveRow
    {
        public bool IsActive { get; init; }

        public bool IsDeleted { get; init; }
    }
}
