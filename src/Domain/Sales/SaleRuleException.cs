namespace Retail360.Domain.Sales;

public enum SaleRuleFailure
{
    Invalid,
    NotFound,
    Conflict
}

public class SaleRuleException : Exception
{
    public SaleRuleException(SaleRuleFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public SaleRuleFailure Failure { get; }
}

public sealed record SaleStockShortage(
    Guid ProductId,
    string ProductName,
    Guid ShelfId,
    string ShelfName,
    decimal Required,
    decimal Available);

public sealed class SaleStockException : SaleRuleException
{
    public SaleStockException(IReadOnlyList<SaleStockShortage> shortages)
        : base(SaleRuleFailure.Conflict, "Shelf does not have enough stock.")
    {
        Shortages = shortages;
    }

    public IReadOnlyList<SaleStockShortage> Shortages { get; }
}
