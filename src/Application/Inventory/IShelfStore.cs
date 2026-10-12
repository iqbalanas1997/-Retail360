using Retail360.Domain.Inventory;

namespace Retail360.Application.Inventory;

public interface IShelfStore
{
    Task<Shelf?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<ShelfResponse?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ShelfResponse>> ListAsync(Guid? areaId, CancellationToken cancellationToken);

    Task<bool> HasHistoryAsync(Guid shelfId, CancellationToken cancellationToken);

    Task SoftDeleteIfNoHistoryAsync(Guid shelfId, DateTimeOffset deletedAt, CancellationToken cancellationToken);

    Task AddAsync(Shelf shelf, CancellationToken cancellationToken);

    Task UpdateAsync(Shelf shelf, CancellationToken cancellationToken);
}
