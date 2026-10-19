namespace Retail360.Domain.Suppliers;

public enum SupplierRuleFailure
{
    Invalid,
    NotFound,
    Conflict
}

public sealed class SupplierRuleException : Exception
{
    public SupplierRuleException(SupplierRuleFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public SupplierRuleFailure Failure { get; }
}
