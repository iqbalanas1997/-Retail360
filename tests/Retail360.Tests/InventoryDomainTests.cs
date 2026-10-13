using Retail360.Domain.Inventory;
using Xunit;

namespace Retail360.Tests;

public class InventoryDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Area_create_trims_the_name_and_starts_active()
    {
        var area = Area.Create("  Front  ", Now);

        Assert.Equal("Front", area.Name);
        Assert.True(area.IsActive);
        Assert.False(area.IsDeleted);
    }

    [Fact]
    public void Area_rejects_a_blank_name_and_a_deleted_rename()
    {
        var blank = Assert.Throws<InventoryRuleException>(() => Area.Create("   ", Now));
        var area = Area.Create("Front", Now);
        area.SoftDelete(Now);

        var deleted = Assert.Throws<InventoryRuleException>(() => area.Rename("Back", Now));

        Assert.Equal(InventoryRuleFailure.Invalid, blank.Failure);
        Assert.Equal(InventoryRuleFailure.NotFound, deleted.Failure);
    }

    [Fact]
    public void Shelf_requires_an_area_and_can_be_deactivated()
    {
        var missing = Assert.Throws<InventoryRuleException>(() => Shelf.Create(Guid.Empty, "A1", Now));
        var shelf = Shelf.Create(Guid.NewGuid(), " A1 ", Now);
        shelf.Deactivate(Now);

        Assert.Equal(InventoryRuleFailure.Invalid, missing.Failure);
        Assert.Equal("A1", shelf.Name);
        Assert.False(shelf.IsActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.2345)]
    public void Positive_quantity_rejects_zero_negative_and_extra_precision(decimal quantity)
    {
        var error = Assert.Throws<InventoryRuleException>(() => InventoryText.RequirePositiveQuantity(quantity));

        Assert.Equal(InventoryRuleFailure.Invalid, error.Failure);
    }

    [Fact]
    public void Signed_adjustment_splits_direction_and_rejects_zero()
    {
        var inbound = InventoryText.SplitSignedQuantity(1.250m);
        var outbound = InventoryText.SplitSignedQuantity(-2.000m);
        var zero = Assert.Throws<InventoryRuleException>(() => InventoryText.SplitSignedQuantity(0m));

        Assert.Equal((1.250m, StockDirection.In), inbound);
        Assert.Equal((2.000m, StockDirection.Out), outbound);
        Assert.Equal(InventoryRuleFailure.Invalid, zero.Failure);
        Assert.Equal(InventoryText.MaxQuantity, InventoryText.RequirePositiveQuantity(InventoryText.MaxQuantity));
        Assert.Throws<InventoryRuleException>(() =>
            InventoryText.RequirePositiveQuantity(InventoryText.MaxQuantity + 0.001m));
        Assert.Throws<InventoryRuleException>(() => InventoryText.SplitSignedQuantity(-1.2345m));
        Assert.Equal("damaged", InventoryText.Damaged);
        Assert.Equal("expired", InventoryText.Expired);
        Assert.Equal("missing", InventoryText.Missing);
    }
}
