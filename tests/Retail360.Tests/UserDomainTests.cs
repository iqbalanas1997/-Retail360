using Retail360.Domain.Identity;
using Xunit;

namespace Retail360.Tests;

public sealed class UserDomainTests
{
    [Fact]
    public void User_name_and_role_use_the_existing_shop_roles()
    {
        Assert.Equal("cashier", UserText.RequireUserName("  cashier  "));
        Assert.Equal(ShopRoleNames.InventoryStaff, UserText.RequireRole("Inventory Staff"));
        Assert.Equal("correct-horse", UserText.RequirePassword("correct-horse"));
        Assert.Null(UserText.OptionalPassword(null));
        Assert.Null(UserText.OptionalPassword(string.Empty));
        Assert.Equal(50, UserText.RequireLimit(null));
        Assert.Equal(0, UserText.RequireOffset(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_user_names_are_rejected(string userName)
    {
        var error = Assert.Throws<UserRuleException>(() => UserText.RequireUserName(userName));

        Assert.Equal(UserRuleFailure.Invalid, error.Failure);
        Assert.Equal("User name must be between 1 and 256 characters.", error.Message);
    }

    [Fact]
    public void Unknown_roles_and_short_passwords_are_rejected()
    {
        var role = Assert.Throws<UserRuleException>(() => UserText.RequireRole("Owner"));
        var password = Assert.Throws<UserRuleException>(() => UserText.RequirePassword("short"));
        var limit = Assert.Throws<UserRuleException>(() => UserText.RequireLimit(0));

        Assert.Equal("Role must be Admin, Manager, Cashier, or Inventory Staff.", role.Message);
        Assert.Equal("Password must be between 8 and 128 characters.", password.Message);
        Assert.Equal("Limit must be between 1 and 200.", limit.Message);
    }
}
