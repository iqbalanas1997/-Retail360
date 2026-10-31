namespace Retail360.Domain.Audit;

public static class AuditActions
{
    public const string UserCreated = "user.created";

    public const string UserRoleChanged = "user.role_changed";

    public const string UserActivated = "user.activated";

    public const string UserDeactivated = "user.deactivated";

    public const string UserDeleted = "user.deleted";

    public const string UserPasswordChanged = "user.password_changed";

    public const string ProductCreated = "product.created";

    public const string ProductUpdated = "product.updated";

    public const string ProductDeleted = "product.deleted";

    public const string StockOpening = "stock.opening";

    public const string StockAdjustment = "stock.adjustment";

    public const string StockTransfer = "stock.transfer";

    public const string PurchaseCreated = "purchase.created";

    public const string PurchaseReceived = "purchase.received";

    public const string SaleCompleted = "sale.completed";

    public const string ExpenseCreated = "expense.created";

    public const string ExpenseUpdated = "expense.updated";

    public const string ExpenseDeleted = "expense.deleted";

    public const string SupplierCreated = "supplier.created";

    public const string SupplierUpdated = "supplier.updated";

    public const string SupplierActivated = "supplier.activated";

    public const string SupplierDeactivated = "supplier.deactivated";

    public const string SupplierDeleted = "supplier.deleted";

    public const string CustomerCreated = "customer.created";

    public const string CustomerUpdated = "customer.updated";

    public const string CustomerActivated = "customer.activated";

    public const string CustomerDeactivated = "customer.deactivated";

    public const string CustomerDeleted = "customer.deleted";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        UserCreated,
        UserRoleChanged,
        UserActivated,
        UserDeactivated,
        UserDeleted,
        UserPasswordChanged,
        ProductCreated,
        ProductUpdated,
        ProductDeleted,
        StockOpening,
        StockAdjustment,
        StockTransfer,
        PurchaseCreated,
        PurchaseReceived,
        SaleCompleted,
        ExpenseCreated,
        ExpenseUpdated,
        ExpenseDeleted,
        SupplierCreated,
        SupplierUpdated,
        SupplierActivated,
        SupplierDeactivated,
        SupplierDeleted,
        CustomerCreated,
        CustomerUpdated,
        CustomerActivated,
        CustomerDeactivated,
        CustomerDeleted
    };
}

public static class AuditEntityTypes
{
    public const string User = "user";

    public const string Product = "product";

    public const string Stock = "stock";

    public const string Purchase = "purchase";

    public const string Sale = "sale";

    public const string Expense = "expense";

    public const string Supplier = "supplier";

    public const string Customer = "customer";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        User,
        Product,
        Stock,
        Purchase,
        Sale,
        Expense,
        Supplier,
        Customer
    };
}
