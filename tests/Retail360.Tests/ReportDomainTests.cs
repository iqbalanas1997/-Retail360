using Retail360.Domain.Reporting;
using Xunit;

namespace Retail360.Tests;

public sealed class ReportDomainTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    [Fact]
    public void Periods_use_inclusive_calendar_dates()
    {
        Assert.Equal((Today, Today), ReportText.Range(ReportPeriod.Today, Today));
        Assert.Equal((new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 21)), ReportText.Range(ReportPeriod.Yesterday, Today));
        Assert.Equal((new DateOnly(2026, 10, 16), Today), ReportText.Range(ReportPeriod.Last7Days, Today));
        Assert.Equal((new DateOnly(2026, 10, 1), Today), ReportText.Range(ReportPeriod.ThisMonth, Today));
        Assert.Equal(
            (new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1)),
            ReportText.Range(ReportPeriod.ThisMonth, new DateOnly(2026, 10, 1)));
        Assert.Equal(
            (new DateOnly(2024, 2, 29), new DateOnly(2024, 2, 29)),
            ReportText.Range(ReportPeriod.Yesterday, new DateOnly(2024, 3, 1)));
    }

    [Fact]
    public void A_reversed_range_is_rejected()
    {
        var reversed = Assert.Throws<ReportRuleException>(() =>
            ReportText.RequireRange(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 21)));
        var missing = Assert.Throws<ReportRuleException>(() => ReportText.RequireRange(default, Today));

        Assert.Equal(ReportRuleFailure.Invalid, reversed.Failure);
        Assert.Equal("The start date must be on or before the end date.", reversed.Message);
        Assert.Equal("Start date and end date are required.", missing.Message);
    }

    [Fact]
    public void Average_uses_decimal_rounding_and_is_absent_when_there_are_no_sales()
    {
        Assert.Null(ReportText.Average(0m, 0));
        Assert.Equal(5.15m, ReportText.Average(10.30m, 2));
        Assert.Equal(5.01m, ReportText.Average(10.01m, 2));
        Assert.Equal(10, ReportText.RequireLowStockLimit(null));
        Assert.Equal(ReportRuleFailure.Invalid, Assert.Throws<ReportRuleException>(() => ReportText.RequireLowStockLimit(0)).Failure);
    }
}
