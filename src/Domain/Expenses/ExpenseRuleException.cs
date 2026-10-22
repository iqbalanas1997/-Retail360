namespace Retail360.Domain.Expenses;

public enum ExpenseRuleFailure
{
    Invalid,
    NotFound,
    Conflict
}

public sealed class ExpenseRuleException : Exception
{
    public ExpenseRuleException(ExpenseRuleFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public ExpenseRuleFailure Failure { get; }
}
