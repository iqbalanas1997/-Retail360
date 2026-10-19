using Retail360.Domain.Suppliers;

namespace Retail360.Application.Suppliers;

public interface ISupplierStore
{
    Task<Supplier?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<Supplier?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<Supplier>> ListAsync(
        string? name,
        bool activeOnly,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task AddAsync(Supplier supplier, CancellationToken cancellationToken);

    Task UpdateAsync(Supplier supplier, CancellationToken cancellationToken);
}
