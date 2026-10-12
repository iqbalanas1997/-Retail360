namespace Retail360.Application.Inventory;

public interface IInventoryProductLookup
{
    Task<InventoryProduct?> FindAvailableAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record InventoryProduct(Guid Id, string Name, decimal MinimumStock, bool IsActive);

public interface IStockLedger
{
    Task<IReadOnlyList<StockMovementResponse>> PostAsync(StockPosting posting, CancellationToken cancellationToken);

    Task<IReadOnlyList<ShelfBalanceResponse>> ListBalancesAsync(
        Guid? productId,
        Guid? shelfId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StockMovementResponse>> ListMovementsAsync(
        Guid? productId,
        Guid? shelfId,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductStockResponse>> ListProductStockAsync(
        string? status,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReconciliationRow>> ReconcileAsync(CancellationToken cancellationToken);
}
