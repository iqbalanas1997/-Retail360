using Retail360.Domain.Common;

namespace Retail360.Domain.Catalog;

public sealed class ProductBarcode : BaseEntity
{
    public Guid ProductId { get; private set; }

    public string Barcode { get; private set; } = string.Empty;

    public static ProductBarcode Create(Guid productId, string barcode, DateTimeOffset now)
    {
        return new ProductBarcode
        {
            Id = Guid.NewGuid(),
            ProductId = CatalogText.RequireId(productId, "Product"),
            Barcode = CatalogText.RequireBarcode(barcode),
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };
    }

    public static ProductBarcode Restore(
        Guid id,
        Guid productId,
        string barcode,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted)
    {
        return new ProductBarcode
        {
            Id = id,
            ProductId = productId,
            Barcode = barcode,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            IsDeleted = isDeleted
        };
    }

    public void MarkDeleted(DateTimeOffset now)
    {
        IsDeleted = true;
        UpdatedAt = now;
    }
}
