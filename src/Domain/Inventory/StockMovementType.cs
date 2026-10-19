namespace Retail360.Domain.Inventory;

public enum StockDirection
{
    In,
    Out
}

public enum StockMovementType
{
    Opening,
    Adjustment,
    Transfer,
    Purchase
}

public static class StockCodes
{
    public static string ToCode(StockDirection direction)
    {
        return direction == StockDirection.In ? "in" : "out";
    }

    public static string ToCode(StockMovementType movementType)
    {
        return movementType switch
        {
            StockMovementType.Opening => "opening",
            StockMovementType.Adjustment => "adjustment",
            StockMovementType.Transfer => "transfer",
            StockMovementType.Purchase => "purchase",
            _ => throw new InventoryRuleException(InventoryRuleFailure.Invalid, "Movement type is invalid.")
        };
    }

    public static StockDirection ParseDirection(string value)
    {
        return value switch
        {
            "in" => StockDirection.In,
            "out" => StockDirection.Out,
            _ => throw new InventoryRuleException(InventoryRuleFailure.Invalid, "Movement direction is invalid.")
        };
    }

    public static StockMovementType ParseType(string value)
    {
        return value switch
        {
            "opening" => StockMovementType.Opening,
            "adjustment" => StockMovementType.Adjustment,
            "transfer" => StockMovementType.Transfer,
            "purchase" => StockMovementType.Purchase,
            _ => throw new InventoryRuleException(InventoryRuleFailure.Invalid, "Movement type is invalid.")
        };
    }
}
