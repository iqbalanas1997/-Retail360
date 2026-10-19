namespace Retail360.Domain.Suppliers;

public static class SupplierText
{
    public const int NameMaxLength = 200;

    public const int ContactPersonMaxLength = 200;

    public const int PhoneMaxLength = 40;

    public const int EmailMaxLength = 200;

    public const int AddressMaxLength = 400;

    public const int NotesMaxLength = 1000;

    public static string RequireName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > NameMaxLength)
        {
            throw new SupplierRuleException(
                SupplierRuleFailure.Invalid,
                $"Name must be between 1 and {NameMaxLength} characters.");
        }

        return name;
    }

    public static string? OptionalContactPerson(string? value)
    {
        return OptionalText(value, "Contact person", ContactPersonMaxLength);
    }

    public static string? OptionalPhone(string? value)
    {
        return OptionalText(value, "Phone", PhoneMaxLength);
    }

    public static string? OptionalAddress(string? value)
    {
        return OptionalText(value, "Address", AddressMaxLength);
    }

    public static string? OptionalNotes(string? value)
    {
        return OptionalText(value, "Notes", NotesMaxLength);
    }

    public static string? OptionalEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var email = value.Trim();
        if (email.Length > EmailMaxLength || !IsEmail(email))
        {
            throw new SupplierRuleException(
                SupplierRuleFailure.Invalid,
                $"Email must be empty or a single address of at most {EmailMaxLength} characters.");
        }

        return email;
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
            throw new SupplierRuleException(
                SupplierRuleFailure.Invalid,
                $"{field} must be between 1 and {maxLength} characters.");
        }

        return text;
    }

    private static bool IsEmail(string value)
    {
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
        {
            return false;
        }

        if (value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var domain = value[(at + 1)..];
        var dot = domain.IndexOf('.');
        return domain.Length >= 3 && dot > 0 && !domain.EndsWith('.');
    }
}
