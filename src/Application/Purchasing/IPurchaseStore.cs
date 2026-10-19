using Retail360.Domain.Purchasing;

namespace Retail360.Application.Purchasing;

public interface IPurchaseStore
{
    Task<PurchaseResponse?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PurchaseResponse>> ListAsync(
        Guid? supplierId,
        string? status,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<PurchaseResponse> AddAsync(Purchase purchase, CancellationToken cancellationToken);

    Task<PurchaseResponse> ReplaceAsync(Purchase purchase, CancellationToken cancellationToken);

    Task DeleteAsync(Purchase purchase, CancellationToken cancellationToken);

    Task<PurchaseResponse> ReceiveAsync(
        Guid purchaseId,
        Guid operationId,
        IReadOnlyDictionary<Guid, Guid> shelves,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);
}
