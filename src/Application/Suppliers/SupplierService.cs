using Retail360.Application.Catalog;
using Retail360.Domain.Suppliers;

namespace Retail360.Application.Suppliers;

public sealed class SupplierService
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    private readonly ISupplierStore _suppliers;
    private readonly IClock _clock;

    public SupplierService(ISupplierStore suppliers, IClock clock)
    {
        _suppliers = suppliers;
        _clock = clock;
    }

    public Task<IReadOnlyList<SupplierResponse>> ListAsync(
        string? name,
        bool activeOnly,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var take = limit ?? DefaultLimit;
        var skip = offset ?? 0;
        if (take < 1 || take > MaxLimit)
        {
            throw new SupplierRuleException(
                SupplierRuleFailure.Invalid,
                $"Limit must be between 1 and {MaxLimit}.");
        }

        if (skip < 0)
        {
            throw new SupplierRuleException(SupplierRuleFailure.Invalid, "Offset must be zero or greater.");
        }

        return ListCoreAsync(name, activeOnly, take, skip, cancellationToken);
    }

    public async Task<SupplierResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return ToResponse(await RequireAsync(id, cancellationToken));
    }

    public async Task<SupplierResponse> CreateAsync(SaveSupplierCommand command, CancellationToken cancellationToken)
    {
        var supplier = Supplier.Create(
            command.Name,
            command.ContactPerson,
            command.Phone,
            command.Email,
            command.Address,
            command.Notes,
            _clock.UtcNow);
        await EnsureNameAvailable(supplier.Name, supplier.Id, cancellationToken);
        await _suppliers.AddAsync(supplier, cancellationToken);
        return ToResponse(supplier);
    }

    public async Task<SupplierResponse> UpdateAsync(
        Guid id,
        SaveSupplierCommand command,
        CancellationToken cancellationToken)
    {
        var supplier = await RequireAsync(id, cancellationToken);
        supplier.Update(
            command.Name,
            command.ContactPerson,
            command.Phone,
            command.Email,
            command.Address,
            command.Notes,
            _clock.UtcNow);
        await EnsureNameAvailable(supplier.Name, supplier.Id, cancellationToken);
        await _suppliers.UpdateAsync(supplier, cancellationToken);
        return ToResponse(supplier);
    }

    public async Task<SupplierResponse> ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var supplier = await RequireAsync(id, cancellationToken);
        supplier.Activate(_clock.UtcNow);
        await _suppliers.UpdateAsync(supplier, cancellationToken);
        return ToResponse(supplier);
    }

    public async Task<SupplierResponse> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var supplier = await RequireAsync(id, cancellationToken);
        supplier.Deactivate(_clock.UtcNow);
        await _suppliers.UpdateAsync(supplier, cancellationToken);
        return ToResponse(supplier);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var supplier = await RequireAsync(id, cancellationToken);
        supplier.SoftDelete(_clock.UtcNow);
        await _suppliers.UpdateAsync(supplier, cancellationToken);
    }

    private async Task<IReadOnlyList<SupplierResponse>> ListCoreAsync(
        string? name,
        bool activeOnly,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var suppliers = await _suppliers.ListAsync(name, activeOnly, limit, offset, cancellationToken);
        return suppliers.Select(ToResponse).ToArray();
    }

    private async Task<Supplier> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _suppliers.FindAsync(id, cancellationToken)
            ?? throw new SupplierRuleException(SupplierRuleFailure.NotFound, "Supplier was not found.");
    }

    private async Task EnsureNameAvailable(string name, Guid currentId, CancellationToken cancellationToken)
    {
        var existing = await _suppliers.FindByNameAsync(name, cancellationToken);
        if (existing is not null && existing.Id != currentId)
        {
            throw new SupplierRuleException(
                SupplierRuleFailure.Conflict,
                "A supplier with this name already exists.");
        }
    }

    private static SupplierResponse ToResponse(Supplier supplier)
    {
        return new SupplierResponse(
            supplier.Id,
            supplier.Name,
            supplier.ContactPerson,
            supplier.Phone,
            supplier.Email,
            supplier.Address,
            supplier.Notes,
            supplier.IsActive,
            supplier.CreatedAt,
            supplier.UpdatedAt);
    }
}
