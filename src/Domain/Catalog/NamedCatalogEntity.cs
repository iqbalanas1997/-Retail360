using Retail360.Domain.Common;

namespace Retail360.Domain.Catalog;

public abstract class NamedCatalogEntity : BaseEntity
{
    public string Name { get; private set; } = string.Empty;

    protected void InitializeNew(string name, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Name = CatalogText.RequireName(name);
        CreatedAt = now;
        UpdatedAt = now;
        IsDeleted = false;
    }

    protected void RestoreState(
        Guid id,
        string name,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        IsDeleted = isDeleted;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        EnsureAvailable();
        Name = CatalogText.RequireName(name);
        UpdatedAt = now;
    }

    public void SoftDelete(DateTimeOffset now)
    {
        EnsureAvailable();
        IsDeleted = true;
        UpdatedAt = now;
    }

    private void EnsureAvailable()
    {
        if (IsDeleted)
        {
            throw new CatalogRuleException(CatalogRuleFailure.NotFound, "The catalog record is deleted.");
        }
    }
}
