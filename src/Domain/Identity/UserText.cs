namespace Retail360.Domain.Identity;

public static class UserText
{
    public const int UserNameMaxLength = 256;

    public const int PasswordMinLength = 8;

    public const int PasswordMaxLength = 128;

    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    public static string RequireUserName(string? value)
    {
        var userName = value?.Trim() ?? string.Empty;
        if (userName.Length == 0 || userName.Length > UserNameMaxLength)
        {
            throw new UserRuleException(
                UserRuleFailure.Invalid,
                $"User name must be between 1 and {UserNameMaxLength} characters.");
        }

        return userName;
    }

    public static string RequireRole(string? value)
    {
        var role = value?.Trim() ?? string.Empty;
        if (role is not (ShopRoleNames.Admin or ShopRoleNames.Manager or ShopRoleNames.Cashier or ShopRoleNames.InventoryStaff))
        {
            throw new UserRuleException(
                UserRuleFailure.Invalid,
                "Role must be Admin, Manager, Cashier, or Inventory Staff.");
        }

        return role;
    }

    public static string RequirePassword(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < PasswordMinLength || value.Length > PasswordMaxLength)
        {
            throw new UserRuleException(
                UserRuleFailure.Invalid,
                $"Password must be between {PasswordMinLength} and {PasswordMaxLength} characters.");
        }

        return value;
    }

    public static string? OptionalPassword(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return RequirePassword(value);
    }

    public static int RequireLimit(int? limit)
    {
        var take = limit ?? DefaultLimit;
        if (take < 1 || take > MaxLimit)
        {
            throw new UserRuleException(
                UserRuleFailure.Invalid,
                $"Limit must be between 1 and {MaxLimit}.");
        }

        return take;
    }

    public static int RequireOffset(int? offset)
    {
        var skip = offset ?? 0;
        if (skip < 0)
        {
            throw new UserRuleException(UserRuleFailure.Invalid, "Offset must be zero or greater.");
        }

        return skip;
    }
}
