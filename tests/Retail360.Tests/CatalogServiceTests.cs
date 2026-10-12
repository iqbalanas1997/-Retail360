using Retail360.Application.Catalog;
using Retail360.Domain.Catalog;
using Xunit;

namespace Retail360.Tests;

public class CatalogServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_product_without_category_or_brand()
    {
        var fixture = new CatalogFixture();
        var unit = Unit.Create("Kg", Now);
        await fixture.Units.AddAsync(unit, CancellationToken.None);

        var created = await fixture.Products.CreateAsync(
            new SaveProductCommand("Rice", null, null, unit.Id, 80.00m, 120.00m, 2.500m, ["8901"]),
            CancellationToken.None);

        Assert.Null(created.CategoryId);
        Assert.Null(created.BrandId);
        Assert.Equal(unit.Id, created.UnitId);
        Assert.Equal("Kg", created.UnitName);
        Assert.Equal("8901", Assert.Single(created.Barcodes));
    }

    [Fact]
    public async Task Create_product_rejects_a_missing_unit_and_keeps_category_optional()
    {
        var fixture = new CatalogFixture();

        var error = await Assert.ThrowsAsync<CatalogRuleException>(() =>
            fixture.Products.CreateAsync(
                new SaveProductCommand("Rice", null, null, Guid.NewGuid(), 1.00m, 2.00m, 0m, null),
                CancellationToken.None));

        Assert.Equal(CatalogRuleFailure.NotFound, error.Failure);
        Assert.Empty(fixture.ProductStore.Products);
    }

    [Fact]
    public async Task Create_product_rejects_a_missing_category_or_brand()
    {
        var fixture = new CatalogFixture();
        var unit = Unit.Create("Kg", Now);
        await fixture.Units.AddAsync(unit, CancellationToken.None);

        var missingCategory = await Assert.ThrowsAsync<CatalogRuleException>(() =>
            fixture.Products.CreateAsync(
                new SaveProductCommand("Rice", Guid.NewGuid(), null, unit.Id, 1.00m, 2.00m, 0m, null),
                CancellationToken.None));
        var missingBrand = await Assert.ThrowsAsync<CatalogRuleException>(() =>
            fixture.Products.CreateAsync(
                new SaveProductCommand("Rice", null, Guid.NewGuid(), unit.Id, 1.00m, 2.00m, 0m, null),
                CancellationToken.None));

        Assert.Equal(CatalogRuleFailure.NotFound, missingCategory.Failure);
        Assert.Equal(CatalogRuleFailure.NotFound, missingBrand.Failure);
        Assert.Empty(fixture.ProductStore.Products);
    }

    [Fact]
    public async Task Delete_category_is_refused_while_a_product_uses_it()
    {
        var fixture = new CatalogFixture();
        var category = await fixture.Categories.CreateAsync(new SaveNameCommand("Grocery"), CancellationToken.None);
        var unit = Unit.Create("Piece", Now);
        await fixture.Units.AddAsync(unit, CancellationToken.None);
        await fixture.Products.CreateAsync(
            new SaveProductCommand("Soap", category.Id, null, unit.Id, 10.00m, 15.00m, 0m, null),
            CancellationToken.None);

        var error = await Assert.ThrowsAsync<CatalogRuleException>(() =>
            fixture.Categories.DeleteAsync(category.Id, CancellationToken.None));

        Assert.Equal(CatalogRuleFailure.Conflict, error.Failure);
        Assert.False((await fixture.CategoriesStore.FindAsync(category.Id, CancellationToken.None))!.IsDeleted);
    }

    [Fact]
    public async Task Deactivate_leaves_the_product_available_to_catalog_get()
    {
        var fixture = new CatalogFixture();
        var unit = Unit.Create("Piece", Now);
        await fixture.Units.AddAsync(unit, CancellationToken.None);
        var created = await fixture.Products.CreateAsync(
            new SaveProductCommand("Soap", null, null, unit.Id, 10.00m, 15.00m, 0m, ["555"]),
            CancellationToken.None);

        var deactivated = await fixture.Products.DeactivateAsync(created.Id, CancellationToken.None);
        var loaded = await fixture.Products.GetAsync(created.Id, CancellationToken.None);
        var byBarcode = await Assert.ThrowsAsync<CatalogRuleException>(() =>
            fixture.Products.FindActiveByBarcodeAsync("555", CancellationToken.None));

        Assert.False(deactivated.IsActive);
        Assert.False(loaded.IsActive);
        Assert.Equal(CatalogRuleFailure.NotFound, byBarcode.Failure);
    }

    [Fact]
    public async Task Duplicate_barcode_is_rejected_and_the_second_product_is_not_stored()
    {
        var fixture = new CatalogFixture();
        var unit = Unit.Create("Piece", Now);
        await fixture.Units.AddAsync(unit, CancellationToken.None);
        await fixture.Products.CreateAsync(
            new SaveProductCommand("Soap", null, null, unit.Id, 10.00m, 15.00m, 0m, ["555"]),
            CancellationToken.None);

        var error = await Assert.ThrowsAsync<CatalogRuleException>(() =>
            fixture.Products.CreateAsync(
                new SaveProductCommand("Other soap", null, null, unit.Id, 10.00m, 15.00m, 0m, ["555"]),
                CancellationToken.None));

        Assert.Equal(CatalogRuleFailure.Conflict, error.Failure);
        Assert.Single(fixture.ProductStore.Products);
    }

    [Fact]
    public async Task Soft_delete_removes_the_product_from_get_list_and_barcode_lookup()
    {
        var fixture = new CatalogFixture();
        var unit = Unit.Create("Piece", Now);
        await fixture.Units.AddAsync(unit, CancellationToken.None);
        var created = await fixture.Products.CreateAsync(
            new SaveProductCommand("Soap", null, null, unit.Id, 10.00m, 15.00m, 0m, ["555"]),
            CancellationToken.None);

        await fixture.Products.DeleteAsync(created.Id, CancellationToken.None);
        var missing = await Assert.ThrowsAsync<CatalogRuleException>(() =>
            fixture.Products.GetAsync(created.Id, CancellationToken.None));
        var byBarcode = await Assert.ThrowsAsync<CatalogRuleException>(() =>
            fixture.Products.FindActiveByBarcodeAsync("555", CancellationToken.None));
        var listed = await fixture.Products.ListAsync(null, false, 50, 0, CancellationToken.None);

        Assert.Equal(CatalogRuleFailure.NotFound, missing.Failure);
        Assert.Equal(CatalogRuleFailure.NotFound, byBarcode.Failure);
        Assert.Empty(listed);
    }

    [Fact]
    public async Task List_keeps_inactive_products_unless_active_only_is_requested()
    {
        var fixture = new CatalogFixture();
        var unit = Unit.Create("Piece", Now);
        await fixture.Units.AddAsync(unit, CancellationToken.None);
        var created = await fixture.Products.CreateAsync(
            new SaveProductCommand("Soap", null, null, unit.Id, 10.00m, 15.00m, 0m, ["555"]),
            CancellationToken.None);
        await fixture.Products.DeactivateAsync(created.Id, CancellationToken.None);

        var management = await fixture.Products.ListAsync("soap", false, 50, 0, CancellationToken.None);
        var offered = await fixture.Products.ListAsync("soap", true, 50, 0, CancellationToken.None);

        Assert.False(Assert.Single(management).IsActive);
        Assert.Empty(offered);
    }

    [Fact]
    public async Task Duplicate_category_name_is_rejected_without_regard_to_case()
    {
        var fixture = new CatalogFixture();
        await fixture.Categories.CreateAsync(new SaveNameCommand("Grocery"), CancellationToken.None);

        var error = await Assert.ThrowsAsync<CatalogRuleException>(() =>
            fixture.Categories.CreateAsync(new SaveNameCommand(" grocery "), CancellationToken.None));

        Assert.Equal(CatalogRuleFailure.Conflict, error.Failure);
    }

    private sealed class CatalogFixture
    {
        public CatalogFixture()
        {
            CategoriesStore = new FakeCategoryStore();
            var brands = new FakeBrandStore();
            Units = new FakeUnitStore();
            ProductStore = new FakeProductStore(CategoriesStore, Units);
            var clock = new FixedClock(Now);
            Categories = new CategoryService(CategoriesStore, clock);
            Products = new ProductService(ProductStore, CategoriesStore, brands, Units, clock);
        }

        public FakeCategoryStore CategoriesStore { get; }

        public FakeUnitStore Units { get; }

        public FakeProductStore ProductStore { get; }

        public CategoryService Categories { get; }

        public ProductService Products { get; }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeCategoryStore : ICategoryStore
    {
        private readonly List<Category> _categories = [];

        public Task<Category?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(_categories.SingleOrDefault(category => category.Id == id && !category.IsDeleted));
        }

        public Task<Category?> FindByNameAsync(string name, CancellationToken cancellationToken)
        {
            return Task.FromResult(_categories.SingleOrDefault(category =>
                !category.IsDeleted && string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<Category>>(_categories.Where(category => !category.IsDeleted).ToArray());
        }

        public Task<bool> IsReferencedByActiveProductAsync(Guid categoryId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ReferencedCategoryIds.Contains(categoryId));
        }

        public Task AddAsync(Category category, CancellationToken cancellationToken)
        {
            _categories.Add(category);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Category category, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public HashSet<Guid> ReferencedCategoryIds { get; } = [];
    }

    private sealed class FakeBrandStore : IBrandStore
    {
        public Task<Brand?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Brand?>(null);

        public Task<Brand?> FindByNameAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Brand?>(null);

        public Task<IReadOnlyList<Brand>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Brand>>([]);

        public Task<bool> IsReferencedByActiveProductAsync(Guid brandId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task AddAsync(Brand brand, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpdateAsync(Brand brand, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeUnitStore : IUnitStore
    {
        private readonly List<Unit> _units = [];

        public Task<Unit?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(_units.SingleOrDefault(unit => unit.Id == id && !unit.IsDeleted));
        }

        public Task<Unit?> FindByNameAsync(string name, CancellationToken cancellationToken) => Task.FromResult<Unit?>(null);

        public Task<IReadOnlyList<Unit>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Unit>>(_units);

        public Task<bool> IsReferencedByActiveProductAsync(Guid unitId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task AddAsync(Unit unit, CancellationToken cancellationToken)
        {
            _units.Add(unit);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Unit unit, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeProductStore : IProductStore
    {
        private readonly FakeCategoryStore _categories;
        private readonly FakeUnitStore _units;

        public FakeProductStore(FakeCategoryStore categories, FakeUnitStore units)
        {
            _categories = categories;
            _units = units;
        }

        public List<Product> Products { get; } = [];

        public Task<Product?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Products.SingleOrDefault(product => product.Id == id && !product.IsDeleted));
        }

        public async Task<ProductResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            var product = await FindAsync(id, cancellationToken);
            return product is null ? null : await ToResponseAsync(product, cancellationToken);
        }

        public async Task<ProductResponse?> FindActiveByBarcodeAsync(string barcode, CancellationToken cancellationToken)
        {
            var product = Products.SingleOrDefault(item =>
                !item.IsDeleted
                && item.IsActive
                && item.Barcodes.Any(itemBarcode => !itemBarcode.IsDeleted && itemBarcode.Barcode == barcode));
            return product is null ? null : await ToResponseAsync(product, cancellationToken);
        }

        public async Task<IReadOnlyList<ProductResponse>> ListAsync(
            string? name,
            bool activeOnly,
            int limit,
            int offset,
            CancellationToken cancellationToken)
        {
            IEnumerable<Product> query = Products.Where(product => !product.IsDeleted);
            if (activeOnly)
            {
                query = query.Where(product => product.IsActive);
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                var term = name.Trim();
                query = query.Where(product => product.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            var page = query.Skip(offset).Take(limit).ToArray();
            var responses = new List<ProductResponse>(page.Length);
            foreach (var product in page)
            {
                responses.Add(await ToResponseAsync(product, cancellationToken));
            }

            return responses;
        }

        public Task AddAsync(Product product, CancellationToken cancellationToken)
        {
            EnsureBarcodesAvailable(product);
            Products.Add(product);
            if (product.CategoryId is not null)
            {
                _categories.ReferencedCategoryIds.Add(product.CategoryId.Value);
            }

            return Task.CompletedTask;
        }

        public Task UpdateAsync(Product product, CancellationToken cancellationToken)
        {
            EnsureBarcodesAvailable(product);
            return Task.CompletedTask;
        }

        private void EnsureBarcodesAvailable(Product product)
        {
            var codes = product.Barcodes
                .Where(barcode => !barcode.IsDeleted)
                .Select(barcode => barcode.Barcode)
                .ToHashSet(StringComparer.Ordinal);
            var taken = Products.Any(other =>
                other.Id != product.Id
                && !other.IsDeleted
                && other.Barcodes.Any(barcode => !barcode.IsDeleted && codes.Contains(barcode.Barcode)));
            if (taken)
            {
                throw new CatalogRuleException(
                    CatalogRuleFailure.Conflict,
                    "Barcode is already assigned to a product.");
            }
        }

        private async Task<ProductResponse> ToResponseAsync(Product product, CancellationToken cancellationToken)
        {
            var unit = await _units.FindAsync(product.UnitId, cancellationToken);
            Category? category = null;
            if (product.CategoryId is not null)
            {
                category = await _categories.FindAsync(product.CategoryId.Value, cancellationToken);
            }

            return new ProductResponse(
                product.Id,
                product.Name,
                product.CategoryId,
                category?.Name,
                product.BrandId,
                null,
                product.UnitId,
                unit?.Name ?? string.Empty,
                product.PurchasePrice,
                product.SellingPrice,
                product.MinimumStock,
                product.IsActive,
                product.Barcodes.Where(barcode => !barcode.IsDeleted).Select(barcode => barcode.Barcode).ToArray(),
                product.CreatedAt,
                product.UpdatedAt);
        }
    }
}
