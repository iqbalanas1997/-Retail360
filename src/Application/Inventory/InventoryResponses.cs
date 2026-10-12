using Retail360.Domain.Inventory;

namespace Retail360.Application.Inventory;

public sealed record AreaResponse(
    Guid Id,
    string Name,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ShelfResponse(
    Guid Id,
    Guid AreaId,
    string AreaName,
    string Name,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SaveAreaCommand(string Name);

public sealed record SaveShelfCommand(Guid AreaId, string Name);

public sealed record OpeningStockCommand(
    Guid ProductId,
    Guid ShelfId,
    decimal Quantity,
    string Reason,
    Guid OperationId);

public sealed record AdjustmentCommand(
    Guid ProductId,
    Guid ShelfId,
    decimal Quantity,
    string Reason,
    Guid OperationId);

public sealed record TransferCommand(
    Guid ProductId,
    Guid SourceShelfId,
    Guid DestinationShelfId,
    decimal Quantity,
    string? Reason,
    Guid OperationId);

public sealed record StockMovementResponse(
    Guid Id,
    Guid OperationId,
    Guid ProductId,
    Guid ShelfId,
    decimal Quantity,
    string Direction,
    string MovementType,
    string Reason,
    Guid? OtherShelfId,
    DateTimeOffset OccurredAt);

public sealed record ShelfBalanceResponse(
    Guid ProductId,
    string ProductName,
    Guid ShelfId,
    string ShelfName,
    Guid AreaId,
    string AreaName,
    decimal Quantity,
    bool ShelfIsActive);

public sealed record ProductStockResponse(
    Guid ProductId,
    string ProductName,
    bool ProductIsActive,
    decimal OnHand,
    decimal MinimumStock,
    string Status);

public sealed record ReconciliationRow(
    Guid ProductId,
    Guid ShelfId,
    decimal StoredQuantity,
    decimal LedgerQuantity);

public sealed record StockLine(
    Guid ShelfId,
    decimal Quantity,
    StockDirection Direction,
    StockMovementType MovementType,
    string Reason,
    Guid? OtherShelfId);

public sealed record StockPosting(
    Guid OperationId,
    string Fingerprint,
    Guid ProductId,
    IReadOnlyList<StockLine> Lines,
    DateTimeOffset OccurredAt);
