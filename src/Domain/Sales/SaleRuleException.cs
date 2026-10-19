namespace Retail360.Domain.Sales;

public enum SaleRuleFailure
{
    Invalid,
    NotFound,
    Conflict
}

public sealed class SaleRuleException : Exception
{
    public SaleRuleException(SaleRuleFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public SaleRuleFailure Failure { get; }
}
