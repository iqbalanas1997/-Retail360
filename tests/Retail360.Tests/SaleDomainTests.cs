using Retail360.Domain.Sales;
using Xunit;

namespace Retail360.Tests;

public sealed class SaleDomainTests
{
    [Fact]
    public void Line_total_rounds_half_away_from_zero()
    {
        var line = SaleLine.Create(Guid.NewGuid(), Guid.NewGuid(), "Tea", Guid.NewGuid(), 1, 1.250m, 0.10m);
        var sale = Sale.Create(Guid.NewGuid(), 1, Guid.NewGuid(), "fingerprint", DateTimeOffset.Parse("2026-10-20T00:00:00Z"), [line]);

        Assert.Equal(0.13m, line.LineTotal);
        Assert.Equal(0.13m, sale.Total);
        Assert.Equal("Tea", line.ProductName);
    }

    [Fact]
    public void Extra_quantity_digits_and_an_empty_sale_are_rejected()
    {
        var quantity = Assert.Throws<SaleRuleException>(() => SaleText.RequireQuantity(1.2501m));
        var empty = Assert.Throws<SaleRuleException>(() => Sale.Create(
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            "fingerprint",
            DateTimeOffset.Parse("2026-10-20T00:00:00Z"),
            []));

        Assert.Equal(SaleRuleFailure.Invalid, quantity.Failure);
        Assert.Equal(SaleRuleFailure.Invalid, empty.Failure);
        Assert.Equal("A sale needs at least one line.", empty.Message);
    }
}
