using Retail360.Domain.Catalog;
using Xunit;

namespace Retail360.Tests;

public class CatalogDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_allows_a_product_without_category_or_brand()
    {
        var product = Product.Create("Rice", null, null, Guid.NewGuid(), 80.50m, 100.00m, 1.500m, ["8901234567890"], Now);

        Assert.Null(product.CategoryId);
        Assert.Null(product.BrandId);
        Assert.Equal(80.50m, product.PurchasePrice);
        Assert.Equal(100.00m, product.SellingPrice);
        Assert.Equal(1.500m, product.MinimumStock);
        Assert.True(product.IsActive);
        Assert.False(product.IsDeleted);
        Assert.Equal("8901234567890", Assert.Single(product.Barcodes).Barcode);
    }

    [Fact]
    public void Create_rejects_a_negative_price()
    {
        var error = Assert.Throws<CatalogRuleException>(() =>
            Product.Create("Rice", null, null, Guid.NewGuid(), -0.01m, 2.00m, 0m, null, Now));

        Assert.Equal(CatalogRuleFailure.Invalid, error.Failure);
    }

    [Fact]
    public void Create_rejects_a_price_with_more_than_two_decimal_places()
    {
        var error = Assert.Throws<CatalogRuleException>(() =>
            Product.Create("Rice", null, null, Guid.NewGuid(), 1.234m, 2.00m, 0m, null, Now));

        Assert.Equal(CatalogRuleFailure.Invalid, error.Failure);
    }

    [Fact]
    public void Create_accepts_scale_limits_and_rejects_the_next_value()
    {
        var product = Product.Create(
            "Rice",
            null,
            null,
            Guid.NewGuid(),
            CatalogText.MaxPrice,
            0m,
            CatalogText.MaxQuantity,
            null,
            Now);
        var trailingZero = Product.Create("Rice", null, null, Guid.NewGuid(), 1.10m, 2.00m, 1.2300m, null, Now);

        Assert.Equal(CatalogText.MaxPrice, product.PurchasePrice);
        Assert.Equal(CatalogText.MaxQuantity, product.MinimumStock);
        Assert.Equal(1.230m, trailingZero.MinimumStock);
        Assert.Throws<CatalogRuleException>(() =>
            Product.Create("Rice", null, null, Guid.NewGuid(), CatalogText.MaxPrice + 0.01m, 2.00m, 0m, null, Now));
        Assert.Throws<CatalogRuleException>(() =>
            Product.Create("Rice", null, null, Guid.NewGuid(), 1.00m, 2.00m, CatalogText.MaxQuantity + 0.001m, null, Now));
    }

    [Fact]
    public void Create_rejects_a_minimum_stock_with_more_than_three_decimal_places()
    {
        var error = Assert.Throws<CatalogRuleException>(() =>
            Product.Create("Rice", null, null, Guid.NewGuid(), 1.00m, 2.00m, 1.2345m, null, Now));

        Assert.Equal(CatalogRuleFailure.Invalid, error.Failure);
    }

    [Fact]
    public void Create_rejects_a_missing_unit_and_duplicate_barcodes()
    {
        Assert.Throws<CatalogRuleException>(() =>
            Product.Create("Rice", null, null, Guid.Empty, 1.00m, 2.00m, 0m, null, Now));
        Assert.Throws<CatalogRuleException>(() =>
            Product.Create("Rice", null, null, Guid.NewGuid(), 1.00m, 2.00m, 0m, ["111", "111"], Now));
    }

    [Fact]
    public void Deactivate_does_not_delete_the_product()
    {
        var product = Product.Create("Rice", null, null, Guid.NewGuid(), 1.00m, 2.00m, 0m, ["111"], Now);

        product.Deactivate(Now.AddMinutes(1));

        Assert.False(product.IsActive);
        Assert.False(product.IsDeleted);
        Assert.False(Assert.Single(product.Barcodes).IsDeleted);
    }

    [Fact]
    public void Revise_replaces_removed_barcodes_and_keeps_the_product()
    {
        var product = Product.Create("Rice", null, null, Guid.NewGuid(), 1.00m, 2.00m, 0m, ["111"], Now);

        product.Revise("Rice", null, null, product.UnitId, 1.00m, 2.50m, 0m, ["222"], Now.AddMinutes(1));

        Assert.False(product.IsDeleted);
        Assert.True(product.IsActive);
        Assert.Equal(2.50m, product.SellingPrice);
        var removed = Assert.Single(product.Barcodes, barcode => barcode.Barcode == "111");
        var current = Assert.Single(product.Barcodes, barcode => barcode.Barcode == "222");
        Assert.True(removed.IsDeleted);
        Assert.False(current.IsDeleted);
    }

    [Fact]
    public void Soft_delete_hides_the_product_and_its_barcodes()
    {
        var product = Product.Create("Rice", null, null, Guid.NewGuid(), 1.00m, 2.00m, 0m, ["111"], Now);

        product.SoftDelete(Now.AddMinutes(1));

        Assert.True(product.IsDeleted);
        Assert.True(Assert.Single(product.Barcodes).IsDeleted);
    }

    [Fact]
    public void Category_name_is_trimmed_and_required()
    {
        var category = Category.Create("  Rice  ", Now);

        Assert.Equal("Rice", category.Name);
        Assert.Throws<CatalogRuleException>(() => Category.Create(" ", Now));
    }
}
