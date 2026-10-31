using Retail360.Domain.Audit;
using Xunit;

namespace Retail360.Tests;

public class AuditRuleTests
{
    [Fact]
    public void Limit_offset_and_dates_follow_the_list_rules()
    {
        Assert.Equal(50, AuditText.RequireLimit(null));
        Assert.Equal(1, AuditText.RequireLimit(1));
        Assert.Equal(200, AuditText.RequireLimit(200));
        Assert.Equal(0, AuditText.RequireOffset(null));
        AuditText.RequireRange(null, new DateOnly(2026, 10, 22));
        AuditText.RequireRange(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22));

        Assert.Null(AuditText.OptionalAction("  "));
        Assert.Equal(AuditActions.UserCreated, AuditText.OptionalAction(" user.created "));
        Assert.Null(AuditText.OptionalEntityType(null));
        Assert.Equal(AuditEntityTypes.Product, AuditText.OptionalEntityType("product"));
        Assert.Null(AuditText.OptionalActor(" "));
        Assert.Equal("user-1", AuditText.OptionalActor(" user-1 "));

        Assert.Equal("Limit must be from 1 to 200.", Message(() => AuditText.RequireLimit(0)));
        Assert.Equal("Limit must be from 1 to 200.", Message(() => AuditText.RequireLimit(201)));
        Assert.Equal("Offset cannot be negative.", Message(() => AuditText.RequireOffset(-1)));
        Assert.Equal(
            "The start date must be on or before the end date.",
            Message(() => AuditText.RequireRange(new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 22))));
        Assert.Equal("Action is not recognized.", Message(() => AuditText.OptionalAction("nope")));
        Assert.Equal("Entity type is not recognized.", Message(() => AuditText.OptionalEntityType("barcode")));
        Assert.Equal("Acting user is invalid.", Message(() => AuditText.OptionalActor(new string('a', 451))));
    }

    private static string Message(Action action)
    {
        var exception = Assert.Throws<AuditRuleException>(action);
        Assert.Equal(AuditRuleFailure.Invalid, exception.Failure);
        return exception.Message;
    }
}
