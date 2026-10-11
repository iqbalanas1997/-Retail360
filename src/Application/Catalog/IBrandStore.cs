using Retail360.Domain.Catalog;

namespace Retail360.Application.Catalog;

public interface IBrandStore
{
    Task<Brand?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<Brand?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<Brand>> ListAsync(CancellationToken cancellationToken);

    Task<bool> IsReferencedByActiveProductAsync(Guid brandId, CancellationToken cancellationToken);

    Task AddAsync(Brand brand, CancellationToken cancellationToken);

    Task UpdateAsync(Brand brand, CancellationToken cancellationToken);
}
