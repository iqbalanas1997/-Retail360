using Retail360.Domain.Common;

namespace Retail360.Domain.Catalog;

public sealed class Product : BaseEntity
{
    private readonly List<ProductBarcode> _barcodes = [];

    public string Name { get; private set; } = string.Empty;

    public Guid? CategoryId { get; private set; }

    public Guid? BrandId { get; private set; }

    public Guid UnitId { get; private set; }

    public decimal PurchasePrice { get; private set; }

    public decimal SellingPrice { get; private set; }

    public decimal MinimumStock { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<ProductBarcode> Barcodes => _barcodes;

    public static Product Create(
        string name,
        Guid? categoryId,
        Guid? brandId,
        Guid unitId,
        decimal purchasePrice,
        decimal sellingPrice,
        decimal minimumStock,
        IEnumerable<string>? barcodes,
        DateTimeOffset now)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            IsDeleted = false
        };
        product.ApplyDetails(name, categoryId, brandId, unitId, purchasePrice, sellingPrice, minimumStock, now);
        product.IsActive = true;
        product.ReplaceBarcodes(barcodes, now);
        return product;
    }

    public static Product Restore(
        Guid id,
        string name,
        Guid? categoryId,
        Guid? brandId,
        Guid unitId,
        decimal purchasePrice,
        decimal sellingPrice,
        decimal minimumStock,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted,
        IEnumerable<ProductBarcode> barcodes)
    {
        var product = new Product
        {
            Id = id,
            Name = name,
            CategoryId = categoryId,
            BrandId = brandId,
            UnitId = unitId,
            PurchasePrice = purchasePrice,
            SellingPrice = sellingPrice,
            MinimumStock = minimumStock,
            IsActive = isActive,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            IsDeleted = isDeleted
        };
        product._barcodes.AddRange(barcodes);
        return product;
    }

    public void Revise(
        string name,
        Guid? categoryId,
        Guid? brandId,
        Guid unitId,
        decimal purchasePrice,
        decimal sellingPrice,
        decimal minimumStock,
        IEnumerable<string>? barcodes,
        DateTimeOffset now)
    {
        EnsureAvailable();
        ApplyDetails(name, categoryId, brandId, unitId, purchasePrice, sellingPrice, minimumStock, now);
        ReplaceBarcodes(barcodes, now);
    }

    public void Activate(DateTimeOffset now)
    {
        EnsureAvailable();
        IsActive = true;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        EnsureAvailable();
        IsActive = false;
        UpdatedAt = now;
    }

    public void SoftDelete(DateTimeOffset now)
    {
        EnsureAvailable();
        IsDeleted = true;
        UpdatedAt = now;
        foreach (var barcode in _barcodes)
        {
            barcode.MarkDeleted(now);
        }
    }

    private void ApplyDetails(
        string name,
        Guid? categoryId,
        Guid? brandId,
        Guid unitId,
        decimal purchasePrice,
        decimal sellingPrice,
        decimal minimumStock,
        DateTimeOffset now)
    {
        Name = CatalogText.RequireName(name);
        CategoryId = CatalogText.OptionalId(categoryId, "Category");
        BrandId = CatalogText.OptionalId(brandId, "Brand");
        UnitId = CatalogText.RequireId(unitId, "Unit");
        PurchasePrice = CatalogText.RequirePrice(purchasePrice);
        SellingPrice = CatalogText.RequirePrice(sellingPrice);
        MinimumStock = CatalogText.RequireQuantity(minimumStock);
        UpdatedAt = now;
    }

    private void ReplaceBarcodes(IEnumerable<string>? barcodes, DateTimeOffset now)
    {
        var desired = CatalogText.RequireBarcodes(barcodes);
        var desiredSet = desired.ToHashSet(StringComparer.Ordinal);
        foreach (var existing in _barcodes.Where(barcode => !barcode.IsDeleted).ToArray())
        {
            if (!desiredSet.Contains(existing.Barcode))
            {
                existing.MarkDeleted(now);
            }
        }

        foreach (var code in desired)
        {
            if (_barcodes.Any(barcode => !barcode.IsDeleted && barcode.Barcode == code))
            {
                continue;
            }

            _barcodes.Add(ProductBarcode.Create(Id, code, now));
        }
    }

    private void EnsureAvailable()
    {
        if (IsDeleted)
        {
            throw new CatalogRuleException(CatalogRuleFailure.NotFound, "Product is deleted.");
        }
    }
}
