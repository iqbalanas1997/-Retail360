namespace Retail360.Domain.Audit;

public enum AuditRuleFailure
{
    Invalid
}

public sealed class AuditRuleException : Exception
{
    public AuditRuleException(AuditRuleFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public AuditRuleFailure Failure { get; }
}
