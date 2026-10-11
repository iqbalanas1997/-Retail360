namespace Retail360.Domain.Catalog;

public sealed class Unit : NamedCatalogEntity
{
    public static Unit Create(string name, DateTimeOffset now)
    {
        var unit = new Unit();
        unit.InitializeNew(name, now);
        return unit;
    }

    public static Unit Restore(
        Guid id,
        string name,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted)
    {
        var unit = new Unit();
        unit.RestoreState(id, name, createdAt, updatedAt, isDeleted);
        return unit;
    }
}
