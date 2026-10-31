using Retail360.Application.Catalog;
using Retail360.Application.Sales;
using Retail360.Domain.Customers;

namespace Retail360.Application.Customers;

public sealed class CustomerService
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    private readonly ICustomerStore _customers;
    private readonly ISaleStore _sales;
    private readonly IClock _clock;

    public CustomerService(ICustomerStore customers, ISaleStore sales, IClock clock)
    {
        _customers = customers;
        _sales = sales;
        _clock = clock;
    }

    public Task<IReadOnlyList<CustomerResponse>> ListAsync(
        string? search,
        bool activeOnly,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var take = limit ?? DefaultLimit;
        var skip = offset ?? 0;
        RequirePage(take, skip);
        return ListCoreAsync(search, activeOnly, take, skip, cancellationToken);
    }

    public async Task<CustomerResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return ToResponse(await RequireAsync(id, cancellationToken));
    }

    public async Task<IReadOnlyList<CustomerSaleSummary>> ListSalesAsync(
        Guid id,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        await RequireAsync(id, cancellationToken);
        var take = limit ?? DefaultLimit;
        var skip = offset ?? 0;
        RequirePage(take, skip);
        return await _sales.ListForCustomerAsync(id, take, skip, cancellationToken);
    }

    public async Task<CustomerResponse> CreateAsync(SaveCustomerCommand command, CancellationToken cancellationToken)
    {
        var customer = Customer.Create(
            command.Name,
            command.Phone,
            command.Email,
            command.Address,
            command.Notes,
            _clock.UtcNow);
        await _customers.AddAsync(customer, cancellationToken);
        return ToResponse(customer);
    }

    public async Task<CustomerResponse> UpdateAsync(
        Guid id,
        SaveCustomerCommand command,
        CancellationToken cancellationToken)
    {
        var customer = await RequireAsync(id, cancellationToken);
        customer.Update(
            command.Name,
            command.Phone,
            command.Email,
            command.Address,
            command.Notes,
            _clock.UtcNow);
        await _customers.UpdateAsync(customer, cancellationToken);
        return ToResponse(customer);
    }

    public async Task<CustomerResponse> ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await RequireAsync(id, cancellationToken);
        customer.Activate(_clock.UtcNow);
        await _customers.UpdateAsync(customer, cancellationToken);
        return ToResponse(customer);
    }

    public async Task<CustomerResponse> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await RequireAsync(id, cancellationToken);
        customer.Deactivate(_clock.UtcNow);
        await _customers.UpdateAsync(customer, cancellationToken);
        return ToResponse(customer);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await RequireAsync(id, cancellationToken);
        customer.SoftDelete(_clock.UtcNow);
        await _customers.UpdateAsync(customer, cancellationToken);
    }

    private async Task<IReadOnlyList<CustomerResponse>> ListCoreAsync(
        string? search,
        bool activeOnly,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var customers = await _customers.ListAsync(search, activeOnly, limit, offset, cancellationToken);
        return customers.Select(ToResponse).ToArray();
    }

    private async Task<Customer> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _customers.FindAsync(id, cancellationToken)
            ?? throw new CustomerRuleException(CustomerRuleFailure.NotFound, "Customer was not found.");
    }

    private static void RequirePage(int take, int skip)
    {
        if (take < 1 || take > MaxLimit)
        {
            throw new CustomerRuleException(
                CustomerRuleFailure.Invalid,
                $"Limit must be between 1 and {MaxLimit}.");
        }

        if (skip < 0)
        {
            throw new CustomerRuleException(CustomerRuleFailure.Invalid, "Offset must be zero or greater.");
        }
    }

    private static CustomerResponse ToResponse(Customer customer)
    {
        return new CustomerResponse(
            customer.Id,
            customer.Name,
            customer.Phone,
            customer.Email,
            customer.Address,
            customer.Notes,
            customer.IsActive,
            customer.CreatedAt,
            customer.UpdatedAt);
    }
}
