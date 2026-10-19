using System.Security.Cryptography;

namespace Retail360.Domain.Purchasing;

public static class PurchaseText
{
    public const int MaxLines = 100;

    public const int ReferenceMaxLength = 40;

    public const int NotesMaxLength = 1000;

    public const decimal MaxMoney = 9999999999.99m;

    public const decimal MaxQuantity = 99999999999.999m;

    public const string Recorded = "recorded";

    public const string Received = "received";

    public static string? OptionalReference(string? value)
    {
        return OptionalText(value, "Reference", ReferenceMaxLength);
    }

    public static string? OptionalNotes(string? value)
    {
        return OptionalText(value, "Notes", NotesMaxLength);
    }

    public static decimal RequireQuantity(decimal value)
    {
        if (value <= 0 || value > MaxQuantity || decimal.Round(value, 3) != value)
        {
            throw new PurchaseRuleException(
                PurchaseRuleFailure.Invalid,
                "Quantity must be greater than zero with at most 3 decimal places.");
        }

        return value;
    }

    public static decimal RequireUnitCost(decimal value)
    {
        if (value < 0 || value > MaxMoney || decimal.Round(value, 2) != value)
        {
            throw new PurchaseRuleException(
                PurchaseRuleFailure.Invalid,
                "Unit cost must be zero or greater with at most 2 decimal places.");
        }

        return value;
    }

    public static decimal LineTotal(decimal quantity, decimal unitCost)
    {
        var total = decimal.Round(
            RequireQuantity(quantity) * RequireUnitCost(unitCost),
            2,
            MidpointRounding.AwayFromZero);
        if (total > MaxMoney)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, "Line total exceeds the maximum.");
        }

        return total;
    }

    public static decimal RequireTotal(decimal value)
    {
        if (value < 0 || value > MaxMoney || decimal.Round(value, 2) != value)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, "Purchase total exceeds the maximum.");
        }

        return value;
    }

    public static Guid RequireId(Guid id, string field)
    {
        if (id == Guid.Empty)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, $"{field} is required.");
        }

        return id;
    }

    public static DateOnly RequireDate(DateOnly value)
    {
        if (value == default)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, "Purchase date is required.");
        }

        return value;
    }

    public static string RequireStatus(string? value)
    {
        var status = value?.Trim() ?? string.Empty;
        if (status is not (Recorded or Received))
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, "Status must be recorded or received.");
        }

        return status;
    }

    public static Guid StockOperationId(Guid receiveOperationId, Guid productId)
    {
        var material = new byte[32];
        receiveOperationId.TryWriteBytes(material);
        productId.TryWriteBytes(material.AsSpan(16));
        var hash = SHA256.HashData(material);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string? OptionalText(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.Length > maxLength)
        {
            throw new PurchaseRuleException(
                PurchaseRuleFailure.Invalid,
                $"{field} must be empty or at most {maxLength} characters.");
        }

        return text;
    }
}
