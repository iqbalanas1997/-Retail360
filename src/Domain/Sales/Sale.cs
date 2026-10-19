namespace Retail360.Domain.Sales;

public sealed class SaleLine
{
    private SaleLine(
        Guid id,
        Guid productId,
        string productName,
        Guid shelfId,
        int lineNumber,
        decimal quantity,
        decimal unitPrice,
        decimal lineTotal)
    {
        Id = id;
        ProductId = productId;
        ProductName = productName;
        ShelfId = shelfId;
        LineNumber = lineNumber;
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineTotal = lineTotal;
    }

    public Guid Id { get; }

    public Guid ProductId { get; }

    public string ProductName { get; }

    public Guid ShelfId { get; }

    public int LineNumber { get; }

    public decimal Quantity { get; }

    public decimal UnitPrice { get; }

    public decimal LineTotal { get; }

    public static SaleLine Create(
        Guid id,
        Guid productId,
        string productName,
        Guid shelfId,
        int lineNumber,
        decimal quantity,
        decimal unitPrice)
    {
        var name = productName.Trim();
        if (name.Length is < 1 or > 200)
        {
            throw new SaleRuleException(SaleRuleFailure.Invalid, "Product name must be between 1 and 200 characters.");
        }

        var pricedQuantity = SaleText.RequireQuantity(quantity);
        var price = SaleText.RequireUnitPrice(unitPrice);
        return new SaleLine(
            id,
            productId,
            name,
            shelfId,
            lineNumber,
            pricedQuantity,
            price,
            SaleText.LineTotal(pricedQuantity, price));
    }
}

public sealed class Sale
{
    private Sale(
        Guid id,
        int receiptNumber,
        Guid operationId,
        string fingerprint,
        DateTimeOffset soldAt,
        decimal total,
        IReadOnlyList<SaleLine> lines)
    {
        Id = id;
        ReceiptNumber = receiptNumber;
        OperationId = operationId;
        Fingerprint = fingerprint;
        SoldAt = soldAt;
        Total = total;
        Lines = lines;
    }

    public Guid Id { get; }

    public int ReceiptNumber { get; }

    public Guid OperationId { get; }

    public string Fingerprint { get; }

    public DateTimeOffset SoldAt { get; }

    public decimal Total { get; }

    public IReadOnlyList<SaleLine> Lines { get; }

    public static Sale Create(
        Guid id,
        int receiptNumber,
        Guid operationId,
        string fingerprint,
        DateTimeOffset soldAt,
        IReadOnlyList<SaleLine> lines)
    {
        if (lines.Count == 0)
        {
            throw new SaleRuleException(SaleRuleFailure.Invalid, "A sale needs at least one line.");
        }

        if (lines.Count > SaleText.MaxLines)
        {
            throw new SaleRuleException(
                SaleRuleFailure.Invalid,
                $"A sale can have at most {SaleText.MaxLines} lines.");
        }

        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            throw new SaleRuleException(SaleRuleFailure.Invalid, "Operation id is required.");
        }

        var total = SaleText.RequireTotal(lines.Sum(line => line.LineTotal));
        return new Sale(id, receiptNumber, operationId, fingerprint, soldAt, total, lines);
    }
}
