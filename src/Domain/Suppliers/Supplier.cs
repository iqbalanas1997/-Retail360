using Retail360.Domain.Common;

namespace Retail360.Domain.Suppliers;

public sealed class Supplier : BaseEntity
{
    public string Name { get; private set; } = string.Empty;

    public string? ContactPerson { get; private set; }

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public string? Address { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; }

    public static Supplier Create(
        string? name,
        string? contactPerson,
        string? phone,
        string? email,
        string? address,
        string? notes,
        DateTimeOffset now)
    {
        return new Supplier
        {
            Id = Guid.NewGuid(),
            Name = SupplierText.RequireName(name),
            ContactPerson = SupplierText.OptionalContactPerson(contactPerson),
            Phone = SupplierText.OptionalPhone(phone),
            Email = SupplierText.OptionalEmail(email),
            Address = SupplierText.OptionalAddress(address),
            Notes = SupplierText.OptionalNotes(notes),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };
    }

    public static Supplier Restore(
        Guid id,
        string name,
        string? contactPerson,
        string? phone,
        string? email,
        string? address,
        string? notes,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted)
    {
        return new Supplier
        {
            Id = id,
            Name = name,
            ContactPerson = contactPerson,
            Phone = phone,
            Email = email,
            Address = address,
            Notes = notes,
            IsActive = isActive,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            IsDeleted = isDeleted
        };
    }

    public void Update(
        string? name,
        string? contactPerson,
        string? phone,
        string? email,
        string? address,
        string? notes,
        DateTimeOffset now)
    {
        EnsureAvailable();
        Name = SupplierText.RequireName(name);
        ContactPerson = SupplierText.OptionalContactPerson(contactPerson);
        Phone = SupplierText.OptionalPhone(phone);
        Email = SupplierText.OptionalEmail(email);
        Address = SupplierText.OptionalAddress(address);
        Notes = SupplierText.OptionalNotes(notes);
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
            throw new SupplierRuleException(SupplierRuleFailure.NotFound, "Supplier was not found.");
        }
    }
}
