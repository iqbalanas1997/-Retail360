namespace Retail360.Application.Purchasing;

public sealed record PurchaseLineResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitCost,
    decimal LineTotal,
    Guid? ShelfId);

public sealed record PurchaseResponse(
    Guid Id,
    Guid SupplierId,
    string SupplierName,
    DateOnly PurchaseDate,
    string? Reference,
    string? Notes,
    string Status,
    decimal Total,
    Guid? ReceiveOperationId,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<PurchaseLineResponse> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SavePurchaseLineCommand(Guid ProductId, decimal Quantity, decimal UnitCost);

public sealed record SavePurchaseCommand(
    Guid SupplierId,
    DateOnly PurchaseDate,
    string? Reference,
    string? Notes,
    IReadOnlyList<SavePurchaseLineCommand>? Lines);

public sealed record ReceivePurchaseLineCommand(Guid LineId, Guid ShelfId);

public sealed record ReceivePurchaseCommand(
    Guid OperationId,
    IReadOnlyList<ReceivePurchaseLineCommand>? Lines);
