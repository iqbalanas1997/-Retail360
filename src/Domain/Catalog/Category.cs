namespace Retail360.Domain.Catalog;

public sealed class Category : NamedCatalogEntity
{
    public static Category Create(string name, DateTimeOffset now)
    {
        var category = new Category();
        category.InitializeNew(name, now);
        return category;
    }

    public static Category Restore(
        Guid id,
        string name,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted)
    {
        var category = new Category();
        category.RestoreState(id, name, createdAt, updatedAt, isDeleted);
        return category;
    }
}
