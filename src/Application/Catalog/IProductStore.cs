using Retail360.Domain.Catalog;

namespace Retail360.Application.Catalog;

public interface IProductStore
{
    Task<Product?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<ProductResponse?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ProductResponse?> FindActiveByBarcodeAsync(string barcode, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductResponse>> ListAsync(
        string? name,
        bool activeOnly,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task AddAsync(Product product, CancellationToken cancellationToken);

    Task UpdateAsync(Product product, CancellationToken cancellationToken);
}
