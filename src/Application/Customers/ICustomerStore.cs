using Retail360.Domain.Customers;

namespace Retail360.Application.Customers;

public interface ICustomerStore
{
    Task<Customer?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Customer>> ListAsync(
        string? search,
        bool activeOnly,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task AddAsync(Customer customer, CancellationToken cancellationToken);

    Task UpdateAsync(Customer customer, CancellationToken cancellationToken);
}
