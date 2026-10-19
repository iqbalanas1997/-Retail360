namespace Retail360.Application.Sales;

public sealed record SaleLineResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    Guid ShelfId,
    string ShelfName,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record SaleResponse(
    Guid Id,
    int ReceiptNumber,
    DateTimeOffset SoldAt,
    string PaymentMethod,
    decimal Total,
    IReadOnlyList<SaleLineResponse> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CompleteSaleLineCommand(Guid ProductId, Guid ShelfId, decimal Quantity);

public sealed record CompleteSaleCommand(
    Guid OperationId,
    IReadOnlyList<CompleteSaleLineCommand>? Lines);
