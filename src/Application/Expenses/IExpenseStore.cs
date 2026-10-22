using Retail360.Domain.Expenses;

namespace Retail360.Application.Expenses;

public interface IExpenseStore
{
    Task<Expense?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<Expense?> FindByOperationAsync(Guid operationId, CancellationToken cancellationToken);

    Task<ExpensePage> ListAsync(
        string? category,
        DateOnly? from,
        DateOnly? to,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task AddAsync(Expense expense, CancellationToken cancellationToken);

    Task UpdateAsync(Expense expense, CancellationToken cancellationToken);
}

public sealed record ExpensePage(IReadOnlyList<Expense> Expenses, decimal Total);
