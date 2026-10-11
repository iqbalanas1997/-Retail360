namespace Retail360.Domain.Catalog;

public sealed class Brand : NamedCatalogEntity
{
    public static Brand Create(string name, DateTimeOffset now)
    {
        var brand = new Brand();
        brand.InitializeNew(name, now);
        return brand;
    }

    public static Brand Restore(
        Guid id,
        string name,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted)
    {
        var brand = new Brand();
        brand.RestoreState(id, name, createdAt, updatedAt, isDeleted);
        return brand;
    }
}
