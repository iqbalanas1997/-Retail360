namespace Retail360.Domain.Catalog;

public enum CatalogRuleFailure
{
    Invalid,
    NotFound,
    Conflict
}

public sealed class CatalogRuleException : Exception
{
    public CatalogRuleException(CatalogRuleFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public CatalogRuleFailure Failure { get; }
}
