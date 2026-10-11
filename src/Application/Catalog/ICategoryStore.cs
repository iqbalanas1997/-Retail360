using Retail360.Domain.Catalog;

namespace Retail360.Application.Catalog;

public interface ICategoryStore
{
    Task<Category?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<Category?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken);

    Task<bool> IsReferencedByActiveProductAsync(Guid categoryId, CancellationToken cancellationToken);

    Task AddAsync(Category category, CancellationToken cancellationToken);

    Task UpdateAsync(Category category, CancellationToken cancellationToken);
}
