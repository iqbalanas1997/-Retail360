namespace Retail360.Domain.Reporting;

public enum ReportPeriod
{
    Today,
    Yesterday,
    Last7Days,
    ThisMonth
}

public static class ReportText
{
    public const int DefaultLowStockLimit = 10;

    public const int MaxLowStockLimit = 50;

    public const int RecentLimit = 8;

    public static (DateOnly From, DateOnly To) Range(ReportPeriod period, DateOnly today)
    {
        if (today == default)
        {
            throw new ReportRuleException(ReportRuleFailure.Invalid, "Start date and end date are required.");
        }

        return period switch
        {
            ReportPeriod.Today => (today, today),
            ReportPeriod.Yesterday => (today.AddDays(-1), today.AddDays(-1)),
            ReportPeriod.Last7Days => (today.AddDays(-6), today),
            ReportPeriod.ThisMonth => (new DateOnly(today.Year, today.Month, 1), today),
            _ => throw new ReportRuleException(ReportRuleFailure.Invalid, "The report period is not recognized.")
        };
    }

    public static void RequireRange(DateOnly from, DateOnly to)
    {
        if (from == default || to == default)
        {
            throw new ReportRuleException(ReportRuleFailure.Invalid, "Start date and end date are required.");
        }

        if (from > to)
        {
            throw new ReportRuleException(
                ReportRuleFailure.Invalid,
                "The start date must be on or before the end date.");
        }
    }

    public static int RequireLowStockLimit(int? limit)
    {
        var value = limit ?? DefaultLowStockLimit;
        if (value < 1 || value > MaxLowStockLimit)
        {
            throw new ReportRuleException(ReportRuleFailure.Invalid, "Limit must be from 1 to 50.");
        }

        return value;
    }

    public static decimal? Average(decimal total, int count)
    {
        if (count == 0)
        {
            return null;
        }

        return decimal.Round(total / count, 2, MidpointRounding.AwayFromZero);
    }
}
