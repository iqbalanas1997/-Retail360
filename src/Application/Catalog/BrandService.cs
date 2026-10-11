using Retail360.Domain.Catalog;

namespace Retail360.Application.Catalog;

public sealed class BrandService
{
    private readonly IBrandStore _brands;
    private readonly IClock _clock;

    public BrandService(IBrandStore brands, IClock clock)
    {
        _brands = brands;
        _clock = clock;
    }

    public async Task<IReadOnlyList<NamedCatalogResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var brands = await _brands.ListAsync(cancellationToken);
        return brands.Select(ToResponse).ToArray();
    }

    public async Task<NamedCatalogResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var brand = await RequireAsync(id, cancellationToken);
        return ToResponse(brand);
    }

    public async Task<NamedCatalogResponse> CreateAsync(SaveNameCommand command, CancellationToken cancellationToken)
    {
        var brand = Brand.Create(command.Name, _clock.UtcNow);
        await EnsureNameAvailable(brand.Name, brand.Id, cancellationToken);
        await _brands.AddAsync(brand, cancellationToken);
        return ToResponse(brand);
    }

    public async Task<NamedCatalogResponse> UpdateAsync(
        Guid id,
        SaveNameCommand command,
        CancellationToken cancellationToken)
    {
        var brand = await RequireAsync(id, cancellationToken);
        brand.Rename(command.Name, _clock.UtcNow);
        await EnsureNameAvailable(brand.Name, brand.Id, cancellationToken);
        await _brands.UpdateAsync(brand, cancellationToken);
        return ToResponse(brand);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var brand = await RequireAsync(id, cancellationToken);
        if (await _brands.IsReferencedByActiveProductAsync(id, cancellationToken))
        {
            throw new CatalogRuleException(
                CatalogRuleFailure.Conflict,
                "Brand is still used by a product.");
        }

        brand.SoftDelete(_clock.UtcNow);
        await _brands.UpdateAsync(brand, cancellationToken);
    }

    private async Task<Brand> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _brands.FindAsync(id, cancellationToken)
            ?? throw new CatalogRuleException(CatalogRuleFailure.NotFound, "Brand was not found.");
    }

    private async Task EnsureNameAvailable(string name, Guid currentId, CancellationToken cancellationToken)
    {
        var existing = await _brands.FindByNameAsync(name, cancellationToken);
        if (existing is not null && existing.Id != currentId)
        {
            throw new CatalogRuleException(
                CatalogRuleFailure.Conflict,
                "A brand with this name already exists.");
        }
    }

    private static NamedCatalogResponse ToResponse(Brand brand)
    {
        return new NamedCatalogResponse(brand.Id, brand.Name, brand.CreatedAt, brand.UpdatedAt);
    }
}
