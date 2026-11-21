using Retail360.Domain.Customers;
using Retail360.Domain.Sales;
using Xunit;

namespace Retail360.Tests;

public class CustomerCreditDomainTests
{
    [Fact]
    public void Cash_fingerprint_stays_free_of_the_credit_marker()
    {
        var product = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var shelf = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var customer = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var lines = new[] { (product, shelf, 1.250m) };

        var cash = SaleText.Fingerprint(lines);
        var named = SaleText.Fingerprint(lines, customer);

        Assert.DoesNotContain("payment", cash, StringComparison.Ordinal);
        Assert.DoesNotContain("payment", named, StringComparison.Ordinal);
        Assert.EndsWith("|payment|credit", SaleText.CreditFingerprint(named), StringComparison.Ordinal);
        Assert.Equal(SaleText.Cash, SaleText.RequirePaymentMethod(null));
        Assert.Equal(SaleText.Cash, SaleText.RequirePaymentMethod("  cash  "));
        Assert.Equal(SaleText.Credit, SaleText.RequirePaymentMethod("credit"));
    }

    [Fact]
    public void Allocation_fills_the_oldest_charge_without_exceeding_it()
    {
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        var slices = CustomerCreditText.Allocate(15.00m, [
            new OpenCharge(older, Guid.NewGuid(), 4, 10.00m),
            new OpenCharge(newer, Guid.NewGuid(), 5, 10.00m)
        ]);

        Assert.Equal(10.00m, slices[0].Amount);
        Assert.Equal(older, slices[0].ChargeEntryId);
        Assert.Equal(5.00m, slices[1].Amount);
        Assert.Equal(15.00m, slices.Sum(slice => slice.Amount));
    }

    [Fact]
    public void Allocation_rejects_an_amount_above_the_balance_or_with_extra_digits()
    {
        var charges = new[] { new OpenCharge(Guid.NewGuid(), Guid.NewGuid(), 1, 5.00m) };

        var over = Assert.Throws<CustomerRuleException>(() => CustomerCreditText.Allocate(5.01m, charges));
        var digits = Assert.Throws<CustomerRuleException>(() => CustomerCreditText.Allocate(1.001m, charges));

        Assert.Equal(CustomerRuleFailure.Conflict, over.Failure);
        Assert.Equal(CustomerRuleFailure.Invalid, digits.Failure);
    }
}
