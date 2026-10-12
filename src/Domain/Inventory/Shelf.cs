using Retail360.Domain.Common;

namespace Retail360.Domain.Inventory;

public sealed class Shelf : BaseEntity
{
    public Guid AreaId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public static Shelf Create(Guid areaId, string name, DateTimeOffset now)
    {
        if (areaId == Guid.Empty)
        {
            throw new InventoryRuleException(InventoryRuleFailure.Invalid, "Area is required.");
        }

        return new Shelf
        {
            Id = Guid.NewGuid(),
            AreaId = areaId,
            Name = InventoryText.RequireName(name),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };
    }

    public static Shelf Restore(
        Guid id,
        Guid areaId,
        string name,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted)
    {
        return new Shelf
        {
            Id = id,
            AreaId = areaId,
            Name = name,
            IsActive = isActive,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            IsDeleted = isDeleted
        };
    }

    public void Rename(string name, DateTimeOffset now)
    {
        EnsureAvailable();
        Name = InventoryText.RequireName(name);
        UpdatedAt = now;
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
    }

    private void EnsureAvailable()
    {
        if (IsDeleted)
        {
            throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Shelf is deleted.");
        }
    }
}
