namespace Retail360.Domain.Reporting;

public enum ReportRuleFailure
{
    Invalid
}

public sealed class ReportRuleException : Exception
{
    public ReportRuleException(ReportRuleFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public ReportRuleFailure Failure { get; }
}
