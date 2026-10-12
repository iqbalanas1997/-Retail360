using Retail360.Domain.Inventory;

namespace Retail360.Application.Inventory;

public interface IAreaStore
{
    Task<Area?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Area>> ListAsync(CancellationToken cancellationToken);

    Task<bool> HasShelfOrHistoryAsync(Guid areaId, CancellationToken cancellationToken);

    Task SoftDeleteIfNoShelfOrHistoryAsync(Guid areaId, DateTimeOffset deletedAt, CancellationToken cancellationToken);

    Task AddAsync(Area area, CancellationToken cancellationToken);

    Task UpdateAsync(Area area, CancellationToken cancellationToken);
}
