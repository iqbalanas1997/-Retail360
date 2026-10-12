namespace Retail360.Domain.Inventory;

public static class InventoryText
{
    public const int NameMaxLength = 200;

    public const int ReasonMaxLength = 200;

    public const decimal MaxQuantity = 99999999999.999m;

    public const string Damaged = "damaged";

    public const string Expired = "expired";

    public const string Missing = "missing";

    public static string RequireName(string? value)
    {
        return RequireText(value, "Name", NameMaxLength);
    }

    public static string RequireReason(string? value)
    {
        return RequireText(value, "Reason", ReasonMaxLength);
    }

    public static decimal RequirePositiveQuantity(decimal value)
    {
        if (value <= 0 || value > MaxQuantity || decimal.Round(value, 3) != value)
        {
            throw new InventoryRuleException(
                InventoryRuleFailure.Invalid,
                "Quantity must be greater than zero with at most 3 decimal places.");
        }

        return value;
    }

    public static (decimal Quantity, StockDirection Direction) SplitSignedQuantity(decimal value)
    {
        if (value == 0 || Math.Abs(value) > MaxQuantity || decimal.Round(value, 3) != value)
        {
            throw new InventoryRuleException(
                InventoryRuleFailure.Invalid,
                "Adjustment quantity must be non-zero with at most 3 decimal places.");
        }

        return value > 0
            ? (value, StockDirection.In)
            : (-value, StockDirection.Out);
    }

    private static string RequireText(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
        {
            throw new InventoryRuleException(
                InventoryRuleFailure.Invalid,
                $"{field} must be between 1 and {maxLength} characters.");
        }

        return trimmed;
    }
}
