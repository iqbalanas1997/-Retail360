namespace Retail360.Domain.Catalog;

public static class CatalogText
{
    public const int NameMaxLength = 200;

    public const int BarcodeMaxLength = 128;

    public const decimal MaxPrice = 9999999999.99m;

    public const decimal MaxQuantity = 99999999999.999m;

    public static string RequireName(string? value)
    {
        return RequireText(value, "Name", NameMaxLength);
    }

    public static string RequireBarcode(string? value)
    {
        return RequireText(value, "Barcode", BarcodeMaxLength);
    }

    public static decimal RequirePrice(decimal value)
    {
        if (value < 0 || value > MaxPrice || decimal.Round(value, 2) != value)
        {
            throw new CatalogRuleException(
                CatalogRuleFailure.Invalid,
                "Price must be zero or greater with at most 2 decimal places.");
        }

        return value;
    }

    public static decimal RequireQuantity(decimal value)
    {
        if (value < 0 || value > MaxQuantity || decimal.Round(value, 3) != value)
        {
            throw new CatalogRuleException(
                CatalogRuleFailure.Invalid,
                "Minimum stock must be zero or greater with at most 3 decimal places.");
        }

        return value;
    }

    public static Guid RequireId(Guid id, string field)
    {
        if (id == Guid.Empty)
        {
            throw new CatalogRuleException(CatalogRuleFailure.Invalid, $"{field} is required.");
        }

        return id;
    }

    public static Guid? OptionalId(Guid? id, string field)
    {
        if (id is null)
        {
            return null;
        }

        if (id == Guid.Empty)
        {
            throw new CatalogRuleException(CatalogRuleFailure.Invalid, $"{field} is invalid.");
        }

        return id;
    }

    public static IReadOnlyList<string> RequireBarcodes(IEnumerable<string>? barcodes)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var barcode in barcodes ?? [])
        {
            var value = RequireBarcode(barcode);
            if (!seen.Add(value))
            {
                throw new CatalogRuleException(
                    CatalogRuleFailure.Invalid,
                    "Barcode is duplicated on the product.");
            }

            values.Add(value);
        }

        return values;
    }

    private static string RequireText(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
        {
            throw new CatalogRuleException(
                CatalogRuleFailure.Invalid,
                $"{field} must be between 1 and {maxLength} characters.");
        }

        return trimmed;
    }
}
