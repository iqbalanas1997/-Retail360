namespace Retail360.Domain.Purchasing;

public enum PurchaseRuleFailure
{
    Invalid,
    NotFound,
    Conflict
}

public sealed class PurchaseRuleException : Exception
{
    public PurchaseRuleException(PurchaseRuleFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public PurchaseRuleFailure Failure { get; }
}
