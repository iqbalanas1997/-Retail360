using System.Globalization;
using Retail360.Application.Catalog;
using Retail360.Domain.Inventory;

namespace Retail360.Application.Inventory;

public sealed class StockService
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    private readonly IStockLedger _ledger;
    private readonly IInventoryProductLookup _products;
    private readonly IShelfStore _shelves;
    private readonly IClock _clock;

    public StockService(
        IStockLedger ledger,
        IInventoryProductLookup products,
        IShelfStore shelves,
        IClock clock)
    {
        _ledger = ledger;
        _products = products;
        _shelves = shelves;
        _clock = clock;
    }

    public async Task<IReadOnlyList<StockMovementResponse>> OpenAsync(
        OpeningStockCommand command,
        CancellationToken cancellationToken)
    {
        var operationId = RequireOperation(command.OperationId);
        var quantity = InventoryText.RequirePositiveQuantity(command.Quantity);
        var reason = InventoryText.RequireReason(command.Reason);
        await RequireProductAsync(command.ProductId, cancellationToken);
        await RequireShelfAsync(command.ShelfId, inbound: true, cancellationToken);
        return await _ledger.PostAsync(
            Posting(
                operationId,
                Fingerprint("opening", command.ProductId, command.ShelfId, quantity, reason),
                command.ProductId,
                [Line(command.ShelfId, quantity, StockDirection.In, StockMovementType.Opening, reason, null)]),
            cancellationToken);
    }

    public async Task<IReadOnlyList<StockMovementResponse>> AdjustAsync(
        AdjustmentCommand command,
        CancellationToken cancellationToken)
    {
        var operationId = RequireOperation(command.OperationId);
        var (quantity, direction) = InventoryText.SplitSignedQuantity(command.Quantity);
        var reason = InventoryText.RequireReason(command.Reason);
        await RequireProductAsync(command.ProductId, cancellationToken);
        await RequireShelfAsync(command.ShelfId, direction == StockDirection.In, cancellationToken);
        return await _ledger.PostAsync(
            Posting(
                operationId,
                Fingerprint("adjustment", command.ProductId, command.ShelfId, command.Quantity, reason),
                command.ProductId,
                [Line(command.ShelfId, quantity, direction, StockMovementType.Adjustment, reason, null)]),
            cancellationToken);
    }

    public async Task<IReadOnlyList<StockMovementResponse>> TransferAsync(
        TransferCommand command,
        CancellationToken cancellationToken)
    {
        var operationId = RequireOperation(command.OperationId);
        var quantity = InventoryText.RequirePositiveQuantity(command.Quantity);
        if (command.SourceShelfId == command.DestinationShelfId)
        {
            throw new InventoryRuleException(
                InventoryRuleFailure.Invalid,
                "Transfer source and destination must be different shelves.");
        }

        var reason = string.IsNullOrWhiteSpace(command.Reason) ? string.Empty : InventoryText.RequireReason(command.Reason);
        await RequireProductAsync(command.ProductId, cancellationToken);
        await RequireShelfAsync(command.SourceShelfId, inbound: false, cancellationToken);
        await RequireShelfAsync(command.DestinationShelfId, inbound: true, cancellationToken);
        return await _ledger.PostAsync(
            Posting(
                operationId,
                TransferFingerprint(command.ProductId, command.SourceShelfId, command.DestinationShelfId, quantity, reason),
                command.ProductId,
                [
                    Line(command.SourceShelfId, quantity, StockDirection.Out, StockMovementType.Transfer, reason, command.DestinationShelfId),
                    Line(command.DestinationShelfId, quantity, StockDirection.In, StockMovementType.Transfer, reason, command.SourceShelfId)
                ]),
            cancellationToken);
    }

    public Task<IReadOnlyList<ShelfBalanceResponse>> ListBalancesAsync(
        Guid? productId,
        Guid? shelfId,
        CancellationToken cancellationToken)
    {
        return _ledger.ListBalancesAsync(productId, shelfId, cancellationToken);
    }

    public Task<IReadOnlyList<StockMovementResponse>> ListMovementsAsync(
        Guid? productId,
        Guid? shelfId,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var (take, skip) = Page(limit, offset);
        return _ledger.ListMovementsAsync(productId, shelfId, take, skip, cancellationToken);
    }

    public Task<IReadOnlyList<ProductStockResponse>> ListProductStockAsync(
        string? status,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var normalized = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant();
        if (normalized is not ("all" or "low" or "out"))
        {
            throw new InventoryRuleException(InventoryRuleFailure.Invalid, "Status must be all, low, or out.");
        }

        var (take, skip) = Page(limit, offset);
        return _ledger.ListProductStockAsync(normalized, take, skip, cancellationToken);
    }

    public Task<IReadOnlyList<ReconciliationRow>> ReconcileAsync(CancellationToken cancellationToken)
    {
        return _ledger.ReconcileAsync(cancellationToken);
    }

    private StockPosting Posting(Guid operationId, string fingerprint, Guid productId, IReadOnlyList<StockLine> lines)
    {
        return new StockPosting(operationId, fingerprint, productId, lines, _clock.UtcNow);
    }

    private static Guid RequireOperation(Guid operationId)
    {
        if (operationId == Guid.Empty)
        {
            throw new InventoryRuleException(InventoryRuleFailure.Invalid, "Operation id is required.");
        }

        return operationId;
    }

    private static string Fingerprint(string kind, Guid productId, Guid shelfId, decimal quantity, string reason)
    {
        return string.Join(
            '|',
            kind,
            productId.ToString("N"),
            shelfId.ToString("N"),
            quantity.ToString("0.000", CultureInfo.InvariantCulture),
            reason);
    }

    private static string TransferFingerprint(
        Guid productId,
        Guid sourceShelfId,
        Guid destinationShelfId,
        decimal quantity,
        string reason)
    {
        return string.Join(
            '|',
            "transfer",
            productId.ToString("N"),
            sourceShelfId.ToString("N"),
            destinationShelfId.ToString("N"),
            quantity.ToString("0.000", CultureInfo.InvariantCulture),
            reason);
    }

    private static StockLine Line(
        Guid shelfId,
        decimal quantity,
        StockDirection direction,
        StockMovementType movementType,
        string reason,
        Guid? otherShelfId)
    {
        return new StockLine(shelfId, quantity, direction, movementType, reason, otherShelfId);
    }

    private async Task RequireProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (productId == Guid.Empty || await _products.FindAvailableAsync(productId, cancellationToken) is null)
        {
            throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Product was not found.");
        }
    }

    private async Task RequireShelfAsync(Guid shelfId, bool inbound, CancellationToken cancellationToken)
    {
        var shelf = shelfId == Guid.Empty ? null : await _shelves.FindAsync(shelfId, cancellationToken);
        if (shelf is null)
        {
            throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Shelf was not found.");
        }

        if (inbound && !shelf.IsActive)
        {
            throw new InventoryRuleException(
                InventoryRuleFailure.Conflict,
                "Shelf is inactive and cannot receive stock.");
        }
    }

    private static (int Take, int Skip) Page(int? limit, int? offset)
    {
        var take = limit ?? DefaultLimit;
        var skip = offset ?? 0;
        if (take < 1 || take > MaxLimit)
        {
            throw new InventoryRuleException(InventoryRuleFailure.Invalid, $"Limit must be between 1 and {MaxLimit}.");
        }

        if (skip < 0)
        {
            throw new InventoryRuleException(InventoryRuleFailure.Invalid, "Offset must be zero or greater.");
        }

        return (take, skip);
    }
}
