using Retail360.Domain.Common;

namespace Retail360.Domain.Inventory;

public sealed class Area : BaseEntity
{
    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public static Area Create(string name, DateTimeOffset now)
    {
        return new Area
        {
            Id = Guid.NewGuid(),
            Name = InventoryText.RequireName(name),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };
    }

    public static Area Restore(
        Guid id,
        string name,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted)
    {
        return new Area
        {
            Id = id,
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
            throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Area is deleted.");
        }
    }
}
