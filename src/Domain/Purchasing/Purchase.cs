namespace Retail360.Domain.Purchasing;

public sealed class PurchaseLine
{
    private PurchaseLine(
        Guid id,
        Guid productId,
        decimal quantity,
        decimal unitCost,
        decimal lineTotal,
        int lineNumber,
        Guid? shelfId,
        bool isDeleted)
    {
        Id = id;
        ProductId = productId;
        Quantity = quantity;
        UnitCost = unitCost;
        LineTotal = lineTotal;
        LineNumber = lineNumber;
        ShelfId = shelfId;
        IsDeleted = isDeleted;
    }

    public Guid Id { get; }

    public Guid ProductId { get; }

    public decimal Quantity { get; }

    public decimal UnitCost { get; }

    public decimal LineTotal { get; }

    public int LineNumber { get; }

    public Guid? ShelfId { get; private set; }

    public bool IsDeleted { get; private set; }

    public static PurchaseLine Create(Guid productId, decimal quantity, decimal unitCost, int lineNumber)
    {
        var total = PurchaseText.LineTotal(quantity, unitCost);
        return new PurchaseLine(
            Guid.NewGuid(),
            PurchaseText.RequireId(productId, "Product"),
            quantity,
            unitCost,
            total,
            lineNumber,
            null,
            false);
    }

    public static PurchaseLine Restore(
        Guid id,
        Guid productId,
        decimal quantity,
        decimal unitCost,
        decimal lineTotal,
        int lineNumber,
        Guid? shelfId,
        bool isDeleted)
    {
        return new PurchaseLine(id, productId, quantity, unitCost, lineTotal, lineNumber, shelfId, isDeleted);
    }

    public void AssignShelf(Guid shelfId)
    {
        ShelfId = PurchaseText.RequireId(shelfId, "Shelf");
    }

    public void SoftDelete()
    {
        IsDeleted = true;
    }
}

public sealed class Purchase
{
    private readonly List<PurchaseLine> _lines;

    private Purchase(
        Guid id,
        Guid supplierId,
        DateOnly purchaseDate,
        string? reference,
        string? notes,
        string status,
        decimal total,
        Guid? receiveOperationId,
        DateTimeOffset? receivedAt,
        IEnumerable<PurchaseLine> lines,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted)
    {
        Id = id;
        SupplierId = supplierId;
        PurchaseDate = purchaseDate;
        Reference = reference;
        Notes = notes;
        Status = status;
        Total = total;
        ReceiveOperationId = receiveOperationId;
        ReceivedAt = receivedAt;
        _lines = lines.ToList();
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        IsDeleted = isDeleted;
    }

    public Guid Id { get; }

    public Guid SupplierId { get; private set; }

    public DateOnly PurchaseDate { get; private set; }

    public string? Reference { get; private set; }

    public string? Notes { get; private set; }

    public string Status { get; private set; }

    public decimal Total { get; private set; }

    public Guid? ReceiveOperationId { get; private set; }

    public DateTimeOffset? ReceivedAt { get; private set; }

    public IReadOnlyList<PurchaseLine> Lines => _lines;

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsDeleted { get; private set; }

    public static Purchase Record(
        Guid id,
        Guid supplierId,
        DateOnly purchaseDate,
        string? reference,
        string? notes,
        IReadOnlyList<(Guid ProductId, decimal Quantity, decimal UnitCost)> lines,
        DateTimeOffset utcNow)
    {
        return new Purchase(
            PurchaseText.RequireId(id, "Purchase"),
            PurchaseText.RequireId(supplierId, "Supplier"),
            PurchaseText.RequireDate(purchaseDate),
            PurchaseText.OptionalReference(reference),
            PurchaseText.OptionalNotes(notes),
            PurchaseText.Recorded,
            Sum(lines),
            null,
            null,
            CreateLines(lines),
            utcNow,
            utcNow,
            false);
    }

    public void Replace(
        Guid supplierId,
        DateOnly purchaseDate,
        string? reference,
        string? notes,
        IReadOnlyList<(Guid ProductId, decimal Quantity, decimal UnitCost)> lines,
        DateTimeOffset utcNow)
    {
        EnsureRecorded("A received purchase cannot be changed.");
        SupplierId = PurchaseText.RequireId(supplierId, "Supplier");
        PurchaseDate = PurchaseText.RequireDate(purchaseDate);
        Reference = PurchaseText.OptionalReference(reference);
        Notes = PurchaseText.OptionalNotes(notes);
        foreach (var line in _lines)
        {
            line.SoftDelete();
        }

        _lines.AddRange(CreateLines(lines));
        Total = Sum(lines);
        UpdatedAt = utcNow;
    }

    public void SoftDelete(DateTimeOffset utcNow)
    {
        EnsureRecorded("A received purchase cannot be deleted.");
        IsDeleted = true;
        UpdatedAt = utcNow;
    }

    public static Purchase Restore(
        Guid id,
        Guid supplierId,
        DateOnly purchaseDate,
        string? reference,
        string? notes,
        string status,
        decimal total,
        Guid? receiveOperationId,
        DateTimeOffset? receivedAt,
        IEnumerable<PurchaseLine> lines,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted)
    {
        return new Purchase(
            id,
            supplierId,
            purchaseDate,
            reference,
            notes,
            status,
            total,
            receiveOperationId,
            receivedAt,
            lines,
            createdAt,
            updatedAt,
            isDeleted);
    }

    private void EnsureRecorded(string message)
    {
        if (IsDeleted)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.NotFound, "Purchase was not found.");
        }

        if (Status != PurchaseText.Recorded)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Conflict, message);
        }
    }

    private static IReadOnlyList<PurchaseLine> CreateLines(
        IReadOnlyList<(Guid ProductId, decimal Quantity, decimal UnitCost)> lines)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, "A purchase needs at least one line.");
        }

        if (lines.Count > PurchaseText.MaxLines)
        {
            throw new PurchaseRuleException(
                PurchaseRuleFailure.Invalid,
                $"A purchase can have at most {PurchaseText.MaxLines} lines.");
        }

        var created = new List<PurchaseLine>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            created.Add(PurchaseLine.Create(line.ProductId, line.Quantity, line.UnitCost, index + 1));
        }

        return created;
    }

    private static decimal Sum(IReadOnlyList<(Guid ProductId, decimal Quantity, decimal UnitCost)> lines)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, "A purchase needs at least one line.");
        }

        decimal total = 0;
        foreach (var line in lines)
        {
            total += PurchaseText.LineTotal(line.Quantity, line.UnitCost);
        }

        return PurchaseText.RequireTotal(total);
    }
}
