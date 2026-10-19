using System.Globalization;
using Dapper;
using Npgsql;
using Retail360.Application.Inventory;
using Retail360.Application.Sales;
using Retail360.Domain.Inventory;
using Retail360.Domain.Sales;
using Retail360.Infrastructure.Database;
using Retail360.Infrastructure.Inventory;

namespace Retail360.Infrastructure.Sales;

public sealed class SaleStore : ISaleStore
{
    private readonly NpgsqlConnectionFactory _connections;
    private readonly PostgresTransaction _transactions;
    private readonly StockLedgerStore _ledger;

    public SaleStore(
        NpgsqlConnectionFactory connections,
        PostgresTransaction transactions,
        StockLedgerStore ledger)
    {
        _connections = connections;
        _transactions = transactions;
        _ledger = ledger;
    }

    public async Task<SaleResponse?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(connection, null, id, cancellationToken);
    }

    public async Task<IReadOnlyList<SaleResponse>> ListAsync(
        int? receiptNumber,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var ids = (await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            select id
            from sales
            where (@ReceiptNumber is null or receipt_number = @ReceiptNumber)
            order by sold_at desc, id desc
            limit @Limit offset @Offset;
            """,
            new { ReceiptNumber = receiptNumber, Limit = limit, Offset = offset },
            cancellationToken: cancellationToken))).ToArray();
        var sales = new List<SaleResponse>(ids.Length);
        foreach (var id in ids)
        {
            var sale = await ReadAsync(connection, null, id, cancellationToken);
            if (sale is not null)
            {
                sales.Add(sale);
            }
        }

        return sales;
    }

    public async Task<SaleResponse> CompleteAsync(
        Guid operationId,
        string fingerprint,
        IReadOnlyList<CompleteSaleLineCommand> lines,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _transactions.ExecuteAsync(
                (connection, transaction, token) => CompleteInTransactionAsync(
                    connection,
                    transaction,
                    operationId,
                    fingerprint,
                    lines,
                    utcNow,
                    token),
                cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == "uq_sales_operation_id")
        {
            return await ReplayOrRejectAsync(operationId, fingerprint, cancellationToken);
        }
    }

    private async Task<SaleResponse> CompleteInTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        string fingerprint,
        IReadOnlyList<CompleteSaleLineCommand> lines,
        DateTimeOffset utcNow,
        CancellationToken token)
    {
        var existingId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            select id
            from sales
            where operation_id = @OperationId
            for update;
            """,
            new { OperationId = operationId },
            transaction,
            cancellationToken: token));
        if (existingId is not null)
        {
            return await SameSaleOrRejectAsync(connection, transaction, existingId.Value, fingerprint, token);
        }

        var receiptNumber = await connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            update sale_receipt_counter
            set last_number = last_number + 1
            where id = 1
            returning last_number;
            """,
            transaction: transaction,
            cancellationToken: token));
        var products = new Dictionary<Guid, ProductRow>();
        foreach (var productId in lines.Select(line => line.ProductId).Distinct().OrderBy(id => id))
        {
            var product = await connection.QuerySingleOrDefaultAsync<ProductRow>(new CommandDefinition(
                """
                select id, name, selling_price, is_active, is_deleted
                from products
                where id = @Id
                for update;
                """,
                new { Id = productId },
                transaction,
                cancellationToken: token));
            if (product is null || product.IsDeleted)
            {
                throw new SaleRuleException(SaleRuleFailure.NotFound, "Product was not found.");
            }

            if (!product.IsActive)
            {
                throw new SaleRuleException(SaleRuleFailure.Conflict, "Product is inactive.");
            }

            products.Add(productId, product);
        }

        var saleId = Guid.NewGuid();
        var saleLines = lines.Select((line, index) => SaleLine.Create(
            Guid.NewGuid(),
            line.ProductId,
            products[line.ProductId].Name,
            line.ShelfId,
            index + 1,
            line.Quantity,
            products[line.ProductId].SellingPrice)).ToArray();
        var sale = Sale.Create(saleId, receiptNumber, operationId, fingerprint, utcNow, saleLines);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into sales (
                id, receipt_number, operation_id, operation_fingerprint, sold_at, payment_method,
                total, created_at, updated_at, is_deleted)
            values (
                @Id, @ReceiptNumber, @OperationId, @Fingerprint, @SoldAt, @PaymentMethod,
                @Total, @SoldAt, @SoldAt, false);
            """,
            new
            {
                sale.Id,
                sale.ReceiptNumber,
                sale.OperationId,
                Fingerprint = sale.Fingerprint,
                SoldAt = sale.SoldAt,
                PaymentMethod = SaleText.Cash,
                sale.Total
            },
            transaction,
            cancellationToken: token));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into sale_lines (
                id, sale_id, product_id, shelf_id, line_number, product_name,
                quantity, unit_price, line_total, created_at, updated_at, is_deleted)
            values (
                @Id, @SaleId, @ProductId, @ShelfId, @LineNumber, @ProductName,
                @Quantity, @UnitPrice, @LineTotal, @Now, @Now, false);
            """,
            sale.Lines.Select(line => new
            {
                line.Id,
                SaleId = sale.Id,
                line.ProductId,
                line.ShelfId,
                line.LineNumber,
                line.ProductName,
                line.Quantity,
                line.UnitPrice,
                line.LineTotal,
                Now = utcNow
            }),
            transaction,
            cancellationToken: token));

        var reason = "Sale " + sale.Id.ToString("N");
        foreach (var group in sale.Lines.GroupBy(line => line.ProductId).OrderBy(group => group.Key))
        {
            var ordered = group.OrderBy(line => line.LineNumber).ToArray();
            var stockLines = ordered.Select(line => new StockLine(
                line.ShelfId,
                line.Quantity,
                StockDirection.Out,
                StockMovementType.Sale,
                reason,
                null)).ToArray();
            var stockFingerprint = string.Join(
                '|',
                new[] { "sale", group.Key.ToString("N"), reason }.Concat(ordered.Select(line =>
                    line.Id.ToString("N")
                    + "|"
                    + line.ShelfId.ToString("N")
                    + "|"
                    + line.Quantity.ToString("0.000", CultureInfo.InvariantCulture))));
            await _ledger.PostInTransactionAsync(
                connection,
                transaction,
                new StockPosting(
                    SaleText.StockOperationId(operationId, group.Key),
                    stockFingerprint,
                    group.Key,
                    stockLines,
                    utcNow),
                token);
        }

        return (await ReadAsync(connection, transaction, sale.Id, token))!;
    }

    private async Task<SaleResponse> ReplayOrRejectAsync(
        Guid operationId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var existingId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "select id from sales where operation_id = @OperationId;",
            new { OperationId = operationId },
            cancellationToken: cancellationToken));
        if (existingId is null)
        {
            throw new SaleRuleException(
                SaleRuleFailure.Conflict,
                "This operation key was already used for a different stock change.");
        }

        return await SameSaleOrRejectAsync(connection, null, existingId.Value, fingerprint, cancellationToken);
    }

    private async Task<SaleResponse> SameSaleOrRejectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid saleId,
        string fingerprint,
        CancellationToken token)
    {
        var stored = await connection.QuerySingleAsync<string>(new CommandDefinition(
            "select operation_fingerprint from sales where id = @Id;",
            new { Id = saleId },
            transaction,
            cancellationToken: token));
        if (!string.Equals(stored, fingerprint, StringComparison.Ordinal))
        {
            throw new SaleRuleException(
                SaleRuleFailure.Conflict,
                "This operation key was already used for a different stock change.");
        }

        return (await ReadAsync(connection, transaction, saleId, token))!;
    }

    private static async Task<SaleResponse?> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid id,
        CancellationToken token)
    {
        var header = await connection.QuerySingleOrDefaultAsync<SaleRow>(new CommandDefinition(
            """
            select
                id, receipt_number, sold_at, payment_method, total, created_at, updated_at
            from sales
            where id = @Id and is_deleted = false;
            """,
            new { Id = id },
            transaction,
            cancellationToken: token));
        if (header is null)
        {
            return null;
        }

        var lines = (await connection.QueryAsync<SaleLineRow>(new CommandDefinition(
            """
            select
                l.id, l.product_id, l.product_name, l.shelf_id, s.name as shelf_name,
                l.quantity, l.unit_price, l.line_total
            from sale_lines l
            join shelves s on s.id = l.shelf_id
            where l.sale_id = @Id and l.is_deleted = false
            order by l.line_number;
            """,
            new { Id = id },
            transaction,
            cancellationToken: token))).ToArray();
        return new SaleResponse(
            header.Id,
            header.ReceiptNumber,
            header.SoldAt,
            header.PaymentMethod,
            header.Total,
            lines.Select(line => new SaleLineResponse(
                line.Id,
                line.ProductId,
                line.ProductName,
                line.ShelfId,
                line.ShelfName,
                line.Quantity,
                line.UnitPrice,
                line.LineTotal)).ToArray(),
            header.CreatedAt,
            header.UpdatedAt);
    }

    private sealed class ProductRow
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public decimal SellingPrice { get; init; }

        public bool IsActive { get; init; }

        public bool IsDeleted { get; init; }
    }

    private sealed class SaleRow
    {
        public Guid Id { get; init; }

        public int ReceiptNumber { get; init; }

        public DateTimeOffset SoldAt { get; init; }

        public string PaymentMethod { get; init; } = string.Empty;

        public decimal Total { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class SaleLineRow
    {
        public Guid Id { get; init; }

        public Guid ProductId { get; init; }

        public string ProductName { get; init; } = string.Empty;

        public Guid ShelfId { get; init; }

        public string ShelfName { get; init; } = string.Empty;

        public decimal Quantity { get; init; }

        public decimal UnitPrice { get; init; }

        public decimal LineTotal { get; init; }
    }
}
