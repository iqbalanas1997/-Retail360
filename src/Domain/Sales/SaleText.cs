using System.Globalization;
using System.Security.Cryptography;

namespace Retail360.Domain.Sales;

public static class SaleText
{
    public const int MaxLines = 100;

    public const decimal MaxMoney = 9999999999.99m;

    public const decimal MaxQuantity = 99999999999.999m;

    public const string Cash = "cash";

    public const string Completed = "completed";

    public static decimal RequireQuantity(decimal value)
    {
        if (value <= 0 || value > MaxQuantity || decimal.Round(value, 3) != value)
        {
            throw new SaleRuleException(
                SaleRuleFailure.Invalid,
                "Quantity must be greater than zero with at most 3 decimal places.");
        }

        return value;
    }

    public static decimal RequireUnitPrice(decimal value)
    {
        if (value < 0 || value > MaxMoney || decimal.Round(value, 2) != value)
        {
            throw new SaleRuleException(
                SaleRuleFailure.Invalid,
                "Unit price must be zero or greater with at most 2 decimal places.");
        }

        return value;
    }

    public static decimal LineTotal(decimal quantity, decimal unitPrice)
    {
        var total = decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
        if (total > MaxMoney)
        {
            throw new SaleRuleException(SaleRuleFailure.Invalid, "Line total exceeds the maximum.");
        }

        return total;
    }

    public static decimal RequireTotal(decimal value)
    {
        if (value < 0 || value > MaxMoney || decimal.Round(value, 2) != value)
        {
            throw new SaleRuleException(SaleRuleFailure.Invalid, "Sale total exceeds the maximum.");
        }

        return value;
    }

    public static Guid RequireId(Guid id, string field)
    {
        if (id == Guid.Empty)
        {
            throw new SaleRuleException(SaleRuleFailure.Invalid, $"{field} is required.");
        }

        return id;
    }

    public static string Fingerprint(IReadOnlyList<(Guid ProductId, Guid ShelfId, decimal Quantity)> lines)
    {
        return string.Join(
            '|',
            lines.Select(line =>
                line.ProductId.ToString("N")
                + "|"
                + line.ShelfId.ToString("N")
                + "|"
                + line.Quantity.ToString("0.000", CultureInfo.InvariantCulture)));
    }

    public static Guid StockOperationId(Guid saleOperationId, Guid productId)
    {
        var material = new byte[33];
        material[0] = 1;
        saleOperationId.TryWriteBytes(material.AsSpan(1));
        productId.TryWriteBytes(material.AsSpan(17));
        var hash = SHA256.HashData(material);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16));
    }
}
