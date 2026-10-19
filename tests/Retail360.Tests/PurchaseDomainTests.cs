using Retail360.Domain.Purchasing;
using Xunit;

namespace Retail360.Tests;

public class PurchaseDomainTests
{
    [Fact]
    public void Server_rounds_the_line_total_and_clears_blank_notes()
    {
        var purchase = Purchase.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 10, 20),
            "  INV-1 ",
            "   ",
            [(Guid.NewGuid(), 1.250m, 0.10m)],
            DateTimeOffset.UnixEpoch);

        Assert.Equal("INV-1", purchase.Reference);
        Assert.Null(purchase.Notes);
        Assert.Equal(0.13m, Assert.Single(purchase.Lines).LineTotal);
        Assert.Equal(0.13m, purchase.Total);
        Assert.Equal(PurchaseText.Recorded, purchase.Status);
    }

    [Fact]
    public void Extra_fractional_digits_are_rejected_before_storage()
    {
        var quantity = Assert.Throws<PurchaseRuleException>(() => Purchase.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 10, 20),
            null,
            null,
            [(Guid.NewGuid(), 1.0001m, 1.00m)],
            DateTimeOffset.UnixEpoch));
        var cost = Assert.Throws<PurchaseRuleException>(() => Purchase.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 10, 20),
            null,
            null,
            [(Guid.NewGuid(), 1.000m, 1.235m)],
            DateTimeOffset.UnixEpoch));
        var empty = Assert.Throws<PurchaseRuleException>(() => Purchase.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 10, 20),
            null,
            null,
            [],
            DateTimeOffset.UnixEpoch));

        Assert.Equal(PurchaseRuleFailure.Invalid, quantity.Failure);
        Assert.Equal(PurchaseRuleFailure.Invalid, cost.Failure);
        Assert.Equal(PurchaseRuleFailure.Invalid, empty.Failure);
        Assert.Contains("3 decimal places", quantity.Message, StringComparison.Ordinal);
        Assert.Contains("2 decimal places", cost.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_received_purchase_cannot_be_changed_or_deleted()
    {
        var purchase = Purchase.Restore(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 10, 20),
            null,
            null,
            PurchaseText.Received,
            1.00m,
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            [],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            false);

        var changed = Assert.Throws<PurchaseRuleException>(() => purchase.Replace(
            Guid.NewGuid(),
            new DateOnly(2026, 10, 21),
            null,
            null,
            [(Guid.NewGuid(), 1.000m, 1.00m)],
            DateTimeOffset.UnixEpoch));
        var deleted = Assert.Throws<PurchaseRuleException>(() => purchase.SoftDelete(DateTimeOffset.UnixEpoch));

        Assert.Equal(PurchaseRuleFailure.Conflict, changed.Failure);
        Assert.Equal(PurchaseRuleFailure.Conflict, deleted.Failure);
    }
}
