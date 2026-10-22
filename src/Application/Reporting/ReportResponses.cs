namespace Retail360.Application.Reporting;

public sealed record SalesSummary(int Count, decimal Total, decimal? Average);

public sealed record ExpenseCategoryTotal(string Category, int Count, decimal Total);

public sealed record ExpenseSummary(int Count, decimal Total, IReadOnlyList<ExpenseCategoryTotal> Categories);

public sealed record PurchaseSummary(int RecordedCount, decimal RecordedTotal, int ReceivedCount, decimal ReceivedTotal);

public sealed record RecentSale(Guid Id, int ReceiptNumber, DateTimeOffset SoldAt, decimal Total);

public sealed record RecentExpense(Guid Id, string Category, DateOnly ExpenseDate, decimal Amount);

public sealed record DashboardResponse(
    DateOnly From,
    DateOnly To,
    SalesSummary Sales,
    ExpenseSummary Expenses,
    PurchaseSummary Purchases,
    IReadOnlyList<RecentSale> RecentSales,
    IReadOnlyList<RecentExpense> RecentExpenses);

public sealed record LowStockProduct(Guid Id, string Name, decimal OnHand, decimal MinimumStock);

public sealed record LowStockReportResponse(int Count, IReadOnlyList<LowStockProduct> Products);
