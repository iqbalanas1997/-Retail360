using System.Globalization;

namespace Retail360.Domain.Expenses;

public static class ExpenseText
{
    public const int NotesMaxLength = 1000;

    public const decimal MaxAmount = 9999999999.99m;

    public const string Cash = "cash";

    public const string Rent = "rent";

    public const string Utilities = "utilities";

    public const string Wages = "wages";

    public const string Transport = "transport";

    public const string Supplies = "supplies";

    public const string Repairs = "repairs";

    public const string Other = "other";

    public static readonly string[] Categories =
    [
        Rent,
        Utilities,
        Wages,
        Transport,
        Supplies,
        Repairs,
        Other
    ];

    public static Guid RequireOperationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ExpenseRuleException(ExpenseRuleFailure.Invalid, "Operation id is required.");
        }

        return value;
    }

    public static string RequireCategory(string? value)
    {
        var category = value?.Trim() ?? string.Empty;
        if (!Categories.Contains(category, StringComparer.Ordinal))
        {
            throw new ExpenseRuleException(
                ExpenseRuleFailure.Invalid,
                "Category must be rent, utilities, wages, transport, supplies, repairs, or other.");
        }

        return category;
    }

    public static string? OptionalCategoryFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return RequireCategory(value);
    }

    public static decimal RequireAmount(decimal value)
    {
        if (value <= 0 || value > MaxAmount || decimal.Round(value, 2) != value)
        {
            throw new ExpenseRuleException(
                ExpenseRuleFailure.Invalid,
                "Amount must be greater than zero with at most 2 decimal places.");
        }

        return value;
    }

    public static DateOnly RequireDate(DateOnly value)
    {
        if (value == default)
        {
            throw new ExpenseRuleException(ExpenseRuleFailure.Invalid, "Expense date is required.");
        }

        return value;
    }

    public static void RequireDateRange(DateOnly? from, DateOnly? to)
    {
        if (from is not null && to is not null && from.Value > to.Value)
        {
            throw new ExpenseRuleException(
                ExpenseRuleFailure.Invalid,
                "The start date must be on or before the end date.");
        }
    }

    public static string? OptionalNotes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var notes = value.Trim();
        if (notes.Length > NotesMaxLength)
        {
            throw new ExpenseRuleException(
                ExpenseRuleFailure.Invalid,
                $"Notes must be empty or at most {NotesMaxLength} characters.");
        }

        return notes;
    }

    public static string RequirePaymentMethod(string? value)
    {
        var method = value?.Trim() ?? string.Empty;
        if (!string.Equals(method, Cash, StringComparison.Ordinal))
        {
            throw new ExpenseRuleException(ExpenseRuleFailure.Invalid, "Payment method must be cash.");
        }

        return Cash;
    }

    public static string Fingerprint(string category, decimal amount, DateOnly expenseDate, string? notes, string paymentMethod)
    {
        return string.Join(
            '|',
            category,
            amount.ToString("0.00", CultureInfo.InvariantCulture),
            expenseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            notes ?? string.Empty,
            paymentMethod);
    }
}
