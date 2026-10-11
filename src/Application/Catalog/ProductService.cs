using Retail360.Domain.Catalog;

namespace Retail360.Application.Catalog;

public sealed class ProductService
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    private readonly IProductStore _products;
    private readonly ICategoryStore _categories;
    private readonly IBrandStore _brands;
    private readonly IUnitStore _units;
    private readonly IClock _clock;

    public ProductService(
        IProductStore products,
        ICategoryStore categories,
        IBrandStore brands,
        IUnitStore units,
        IClock clock)
    {
        _products = products;
        _categories = categories;
        _brands = brands;
        _units = units;
        _clock = clock;
    }

    public Task<IReadOnlyList<ProductResponse>> ListAsync(
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
            throw new CatalogRuleException(
                CatalogRuleFailure.Invalid,
                $"Limit must be between 1 and {MaxLimit}.");
        }

        if (skip < 0)
        {
            throw new CatalogRuleException(CatalogRuleFailure.Invalid, "Offset must be zero or greater.");
        }

        return _products.ListAsync(name, activeOnly, take, skip, cancellationToken);
    }

    public async Task<ProductResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _products.GetAsync(id, cancellationToken)
            ?? throw new CatalogRuleException(CatalogRuleFailure.NotFound, "Product was not found.");
    }

    public async Task<ProductResponse> FindActiveByBarcodeAsync(string barcode, CancellationToken cancellationToken)
    {
        var value = CatalogText.RequireBarcode(barcode);
        return await _products.FindActiveByBarcodeAsync(value, cancellationToken)
            ?? throw new CatalogRuleException(CatalogRuleFailure.NotFound, "Product was not found.");
    }

    public async Task<ProductResponse> CreateAsync(SaveProductCommand command, CancellationToken cancellationToken)
    {
        await EnsureReferencesAsync(command.CategoryId, command.BrandId, command.UnitId, cancellationToken);
        var product = Product.Create(
            command.Name,
            command.CategoryId,
            command.BrandId,
            command.UnitId,
            command.PurchasePrice,
            command.SellingPrice,
            command.MinimumStock,
            command.Barcodes,
            _clock.UtcNow);
        await _products.AddAsync(product, cancellationToken);
        return await GetAsync(product.Id, cancellationToken);
    }

    public async Task<ProductResponse> UpdateAsync(
        Guid id,
        SaveProductCommand command,
        CancellationToken cancellationToken)
    {
        var product = await RequireAsync(id, cancellationToken);
        await EnsureReferencesAsync(command.CategoryId, command.BrandId, command.UnitId, cancellationToken);
        product.Revise(
            command.Name,
            command.CategoryId,
            command.BrandId,
            command.UnitId,
            command.PurchasePrice,
            command.SellingPrice,
            command.MinimumStock,
            command.Barcodes,
            _clock.UtcNow);
        await _products.UpdateAsync(product, cancellationToken);
        return await GetAsync(product.Id, cancellationToken);
    }

    public async Task<ProductResponse> ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await RequireAsync(id, cancellationToken);
        product.Activate(_clock.UtcNow);
        await _products.UpdateAsync(product, cancellationToken);
        return await GetAsync(product.Id, cancellationToken);
    }

    public async Task<ProductResponse> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await RequireAsync(id, cancellationToken);
        product.Deactivate(_clock.UtcNow);
        await _products.UpdateAsync(product, cancellationToken);
        return await GetAsync(product.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await RequireAsync(id, cancellationToken);
        product.SoftDelete(_clock.UtcNow);
        await _products.UpdateAsync(product, cancellationToken);
    }

    private async Task<Product> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _products.FindAsync(id, cancellationToken)
            ?? throw new CatalogRuleException(CatalogRuleFailure.NotFound, "Product was not found.");
    }

    private async Task EnsureReferencesAsync(
        Guid? categoryId,
        Guid? brandId,
        Guid unitId,
        CancellationToken cancellationToken)
    {
        CatalogText.RequireId(unitId, "Unit");
        CatalogText.OptionalId(categoryId, "Category");
        CatalogText.OptionalId(brandId, "Brand");

        if (await _units.FindAsync(unitId, cancellationToken) is null)
        {
            throw new CatalogRuleException(CatalogRuleFailure.Invalid, "Unit was not found.");
        }

        if (categoryId is not null && await _categories.FindAsync(categoryId.Value, cancellationToken) is null)
        {
            throw new CatalogRuleException(CatalogRuleFailure.Invalid, "Category was not found.");
        }

        if (brandId is not null && await _brands.FindAsync(brandId.Value, cancellationToken) is null)
        {
            throw new CatalogRuleException(CatalogRuleFailure.Invalid, "Brand was not found.");
        }
    }
}
