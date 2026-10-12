namespace Retail360.Domain.Inventory;

public enum InventoryRuleFailure
{
    Invalid,
    NotFound,
    Conflict
}

public sealed class InventoryRuleException : Exception
{
    public InventoryRuleException(InventoryRuleFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public InventoryRuleFailure Failure { get; }
}
