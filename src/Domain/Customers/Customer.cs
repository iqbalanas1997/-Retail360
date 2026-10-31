using Retail360.Domain.Common;

namespace Retail360.Domain.Customers;

public sealed class Customer : BaseEntity
{
    public string Name { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public string? Address { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; }

    public static Customer Create(
        string? name,
        string? phone,
        string? email,
        string? address,
        string? notes,
        DateTimeOffset now)
    {
        return new Customer
        {
            Id = Guid.NewGuid(),
            Name = CustomerText.RequireName(name),
            Phone = CustomerText.OptionalPhone(phone),
            Email = CustomerText.OptionalEmail(email),
            Address = CustomerText.OptionalAddress(address),
            Notes = CustomerText.OptionalNotes(notes),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };
    }

    public static Customer Restore(
        Guid id,
        string name,
        string? phone,
        string? email,
        string? address,
        string? notes,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted)
    {
        return new Customer
        {
            Id = id,
            Name = name,
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
        string? phone,
        string? email,
        string? address,
        string? notes,
        DateTimeOffset now)
    {
        EnsureAvailable();
        Name = CustomerText.RequireName(name);
        Phone = CustomerText.OptionalPhone(phone);
        Email = CustomerText.OptionalEmail(email);
        Address = CustomerText.OptionalAddress(address);
        Notes = CustomerText.OptionalNotes(notes);
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
            throw new CustomerRuleException(CustomerRuleFailure.NotFound, "Customer was not found.");
        }
    }
}
