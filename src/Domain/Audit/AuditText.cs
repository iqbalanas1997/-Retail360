namespace Retail360.Domain.Audit;

public static class AuditText
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    public static int RequireLimit(int? limit)
    {
        var value = limit ?? DefaultLimit;
        if (value < 1 || value > MaxLimit)
        {
            throw new AuditRuleException(AuditRuleFailure.Invalid, "Limit must be from 1 to 200.");
        }

        return value;
    }

    public static int RequireOffset(int? offset)
    {
        var value = offset ?? 0;
        if (value < 0)
        {
            throw new AuditRuleException(AuditRuleFailure.Invalid, "Offset cannot be negative.");
        }

        return value;
    }

    public static void RequireRange(DateOnly? from, DateOnly? to)
    {
        if (from is not null && to is not null && from.Value > to.Value)
        {
            throw new AuditRuleException(
                AuditRuleFailure.Invalid,
                "The start date must be on or before the end date.");
        }
    }

    public static string? OptionalActor(string? actorUserId)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return null;
        }

        var value = actorUserId.Trim();
        if (value.Length > 450)
        {
            throw new AuditRuleException(AuditRuleFailure.Invalid, "Acting user is invalid.");
        }

        return value;
    }

    public static string? OptionalAction(string? action)
    {
        return OptionalKnown(action, AuditActions.All, "Action is not recognized.");
    }

    public static string? OptionalEntityType(string? entityType)
    {
        return OptionalKnown(entityType, AuditEntityTypes.All, "Entity type is not recognized.");
    }

    private static string? OptionalKnown(string? value, IReadOnlySet<string> allowed, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var stored = value.Trim();
        if (!allowed.Contains(stored))
        {
            throw new AuditRuleException(AuditRuleFailure.Invalid, message);
        }

        return stored;
    }
}
