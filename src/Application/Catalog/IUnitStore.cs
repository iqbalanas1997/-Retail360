using Retail360.Domain.Catalog;

namespace Retail360.Application.Catalog;

public interface IUnitStore
{
    Task<Unit?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<Unit?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<Unit>> ListAsync(CancellationToken cancellationToken);

    Task<bool> IsReferencedByActiveProductAsync(Guid unitId, CancellationToken cancellationToken);

    Task AddAsync(Unit unit, CancellationToken cancellationToken);

    Task UpdateAsync(Unit unit, CancellationToken cancellationToken);
}
