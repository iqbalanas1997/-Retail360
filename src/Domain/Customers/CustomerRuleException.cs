namespace Retail360.Domain.Customers;

public enum CustomerRuleFailure
{
    Invalid,
    NotFound,
    Conflict
}

public sealed class CustomerRuleException : Exception
{
    public CustomerRuleException(CustomerRuleFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public CustomerRuleFailure Failure { get; }
}
