using Dapper;
using Retail360.Application.Reporting;
using Retail360.Domain.Expenses;
using Retail360.Domain.Reporting;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Reporting;

public sealed class ReportStore : IReportStore
{
    private readonly NpgsqlConnectionFactory _connections;

    public ReportStore(NpgsqlConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<DashboardResponse> DashboardAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var range = new
        {
            From = from.ToDateTime(TimeOnly.MinValue),
            To = to.ToDateTime(TimeOnly.MinValue),
            RecentLimit = ReportText.RecentLimit
        };
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);

        var sales = await connection.QuerySingleAsync<MoneyCountRow>(new CommandDefinition(
            """
            select count(*)::int as count, coalesce(sum(total), 0) as total
            from sales
            where is_deleted = false
              and (sold_at at time zone 'UTC')::date >= @From::date
              and (sold_at at time zone 'UTC')::date <= @To::date;
            """,
            range,
            cancellationToken: cancellationToken));
        var expenses = await connection.QuerySingleAsync<MoneyCountRow>(new CommandDefinition(
            """
            select count(*)::int as count, coalesce(sum(amount), 0) as total
            from expenses
            where is_deleted = false
              and expense_date >= @From::date
              and expense_date <= @To::date;
            """,
            range,
            cancellationToken: cancellationToken));
        var categories = (await connection.QueryAsync<CategoryRow>(new CommandDefinition(
            """
            select category, count(*)::int as count, coalesce(sum(amount), 0) as total
            from expenses
            where is_deleted = false
              and expense_date >= @From::date
              and expense_date <= @To::date
            group by category;
            """,
            range,
            cancellationToken: cancellationToken))).ToDictionary(row => row.Category, StringComparer.Ordinal);
        var recorded = await connection.QuerySingleAsync<MoneyCountRow>(new CommandDefinition(
            """
            select count(*)::int as count, coalesce(sum(total), 0) as total
            from purchases
            where is_deleted = false
              and purchase_date >= @From::date
              and purchase_date <= @To::date;
            """,
            range,
            cancellationToken: cancellationToken));
        var received = await connection.QuerySingleAsync<MoneyCountRow>(new CommandDefinition(
            """
            select count(*)::int as count, coalesce(sum(total), 0) as total
            from purchases
            where is_deleted = false
              and status = 'received'
              and received_at is not null
              and (received_at at time zone 'UTC')::date >= @From::date
              and (received_at at time zone 'UTC')::date <= @To::date;
            """,
            range,
            cancellationToken: cancellationToken));
        var recentSales = await connection.QueryAsync<RecentSaleRow>(new CommandDefinition(
            """
            select id, receipt_number, sold_at, total
            from sales
            where is_deleted = false
              and (sold_at at time zone 'UTC')::date >= @From::date
              and (sold_at at time zone 'UTC')::date <= @To::date
            order by sold_at desc, id desc
            limit @RecentLimit;
            """,
            range,
            cancellationToken: cancellationToken));
        var recentExpenses = await connection.QueryAsync<RecentExpenseRow>(new CommandDefinition(
            """
            select id, category, expense_date, amount
            from expenses
            where is_deleted = false
              and expense_date >= @From::date
              and expense_date <= @To::date
            order by expense_date desc, id desc
            limit @RecentLimit;
            """,
            range,
            cancellationToken: cancellationToken));

        return new DashboardResponse(
            from,
            to,
            new SalesSummary(sales.Count, sales.Total, ReportText.Average(sales.Total, sales.Count)),
            new ExpenseSummary(
                expenses.Count,
                expenses.Total,
                ExpenseText.Categories.Select(category =>
                {
                    categories.TryGetValue(category, out var row);
                    return new ExpenseCategoryTotal(category, row?.Count ?? 0, row?.Total ?? 0m);
                }).ToArray()),
            new PurchaseSummary(recorded.Count, recorded.Total, received.Count, received.Total),
            recentSales.Select(row => new RecentSale(row.Id, row.ReceiptNumber, row.SoldAt, row.Total)).ToArray(),
            recentExpenses.Select(row => new RecentExpense(
                row.Id,
                row.Category,
                DateOnly.FromDateTime(row.ExpenseDate),
                row.Amount)).ToArray());
    }

    public async Task<LowStockReportResponse> LowStockAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"""
            {LowStock}
            select count(*)::int
            from stock
            where on_hand > 0 and on_hand <= minimum_stock;
            """,
            cancellationToken: cancellationToken));
        var rows = await connection.QueryAsync<LowStockRow>(new CommandDefinition(
            $"""
            {LowStock}
            select id, name, on_hand, minimum_stock
            from stock
            where on_hand > 0 and on_hand <= minimum_stock
            order by lower(name), id
            limit @Limit;
            """,
            new { Limit = limit },
            cancellationToken: cancellationToken));
        return new LowStockReportResponse(
            count,
            rows.Select(row => new LowStockProduct(row.Id, row.Name, row.OnHand, row.MinimumStock)).ToArray());
    }

    private const string LowStock = """
        with stock as (
            select
                p.id,
                p.name,
                coalesce(sum(b.quantity), 0) as on_hand,
                p.minimum_stock
            from products p
            left join stock_balances b on b.product_id = p.id
            where p.is_deleted = false
              and p.is_active = true
            group by p.id, p.name, p.minimum_stock
        )
        """;

    private sealed class MoneyCountRow
    {
        public int Count { get; init; }

        public decimal Total { get; init; }
    }

    private sealed class CategoryRow
    {
        public string Category { get; init; } = string.Empty;

        public int Count { get; init; }

        public decimal Total { get; init; }
    }

    private sealed class RecentSaleRow
    {
        public Guid Id { get; init; }

        public int ReceiptNumber { get; init; }

        public DateTimeOffset SoldAt { get; init; }

        public decimal Total { get; init; }
    }

    private sealed class RecentExpenseRow
    {
        public Guid Id { get; init; }

        public string Category { get; init; } = string.Empty;

        public DateTime ExpenseDate { get; init; }

        public decimal Amount { get; init; }
    }

    private sealed class LowStockRow
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public decimal OnHand { get; init; }

        public decimal MinimumStock { get; init; }
    }
}
