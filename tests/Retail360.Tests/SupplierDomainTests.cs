using Retail360.Domain.Suppliers;
using Xunit;

namespace Retail360.Tests;

public class SupplierDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_trims_the_name_and_keeps_optional_contact_fields()
    {
        var supplier = Supplier.Create(
            "  Sufi Traders  ",
            "  Amina  ",
            "  0300-1234567  ",
            "  shop@example.com  ",
            "  Shop 4, Karachi  ",
            "  Delivers rice  ",
            Now);

        Assert.Equal("Sufi Traders", supplier.Name);
        Assert.Equal("Amina", supplier.ContactPerson);
        Assert.Equal("0300-1234567", supplier.Phone);
        Assert.Equal("shop@example.com", supplier.Email);
        Assert.Equal("Shop 4, Karachi", supplier.Address);
        Assert.Equal("Delivers rice", supplier.Notes);
        Assert.True(supplier.IsActive);
        Assert.False(supplier.IsDeleted);
        Assert.NotEqual(Guid.Empty, supplier.Id);
    }

    [Fact]
    public void Create_treats_blank_optional_fields_as_missing()
    {
        var supplier = Supplier.Create("Sufi", "  ", "", null, " ", "\t", Now);

        Assert.Null(supplier.ContactPerson);
        Assert.Null(supplier.Phone);
        Assert.Null(supplier.Email);
        Assert.Null(supplier.Address);
        Assert.Null(supplier.Notes);
    }

    [Fact]
    public void Create_rejects_a_missing_name_and_overlong_fields()
    {
        Assert.Equal(SupplierRuleFailure.Invalid, Reject(null, null, null, null, null, null));
        Assert.Equal(SupplierRuleFailure.Invalid, Reject("  ", null, null, null, null, null));
        Assert.Equal(SupplierRuleFailure.Invalid, Reject(new string('a', 201), null, null, null, null, null));
        Assert.Equal(SupplierRuleFailure.Invalid, Reject("Sufi", new string('a', 201), null, null, null, null));
        Assert.Equal(SupplierRuleFailure.Invalid, Reject("Sufi", null, new string('1', 41), null, null, null));
        Assert.Equal(SupplierRuleFailure.Invalid, Reject("Sufi", null, null, null, new string('a', 401), null));
        Assert.Equal(SupplierRuleFailure.Invalid, Reject("Sufi", null, null, null, null, new string('a', 1001)));
    }

    [Fact]
    public void Create_rejects_an_email_that_is_not_one_address()
    {
        Assert.Equal(SupplierRuleFailure.Invalid, Reject("Sufi", null, null, "not-an-email", null, null));
        Assert.Equal(SupplierRuleFailure.Invalid, Reject("Sufi", null, null, "a@b", null, null));
        Assert.Equal(SupplierRuleFailure.Invalid, Reject("Sufi", null, null, "shop@example.", null, null));
        Assert.Equal(SupplierRuleFailure.Invalid, Reject("Sufi", null, null, "@example.com", null, null));
        Assert.Equal(SupplierRuleFailure.Invalid, Reject("Sufi", null, null, "a b@example.com", null, null));
        Assert.Equal(SupplierRuleFailure.Invalid, Reject("Sufi", null, null, "a@b@example.com", null, null));
        Assert.Equal(SupplierRuleFailure.Invalid, Reject("Sufi", null, null, new string('a', 190) + "@example.com", null, null));
    }

    [Fact]
    public void Inactive_supplier_can_be_edited_and_a_deleted_supplier_cannot()
    {
        var supplier = Supplier.Create("Sufi", null, null, null, null, null, Now);
        supplier.Deactivate(Now.AddMinutes(1));
        supplier.Update("Sufi Traders", null, "03001234567", null, null, null, Now.AddMinutes(2));

        Assert.False(supplier.IsActive);
        Assert.Equal("Sufi Traders", supplier.Name);
        Assert.Equal("03001234567", supplier.Phone);

        supplier.SoftDelete(Now.AddMinutes(3));
        var error = Assert.Throws<SupplierRuleException>(() =>
            supplier.Update("Again", null, null, null, null, null, Now.AddMinutes(4)));
        Assert.Equal(SupplierRuleFailure.NotFound, error.Failure);
    }

    private static SupplierRuleFailure Reject(
        string? name,
        string? contactPerson,
        string? phone,
        string? email,
        string? address,
        string? notes)
    {
        var error = Assert.Throws<SupplierRuleException>(() =>
            Supplier.Create(name, contactPerson, phone, email, address, notes, Now));
        return error.Failure;
    }
}
