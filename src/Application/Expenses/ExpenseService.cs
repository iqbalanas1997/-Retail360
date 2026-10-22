using Retail360.Application.Catalog;
using Retail360.Domain.Expenses;

namespace Retail360.Application.Expenses;

public sealed class ExpenseService
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    private readonly IExpenseStore _expenses;
    private readonly IClock _clock;

    public ExpenseService(IExpenseStore expenses, IClock clock)
    {
        _expenses = expenses;
        _clock = clock;
    }

    public Task<ExpenseListResponse> ListAsync(
        string? category,
        DateOnly? from,
        DateOnly? to,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var take = limit ?? DefaultLimit;
        var skip = offset ?? 0;
        if (take < 1 || take > MaxLimit)
        {
            throw new ExpenseRuleException(
                ExpenseRuleFailure.Invalid,
                $"Limit must be between 1 and {MaxLimit}.");
        }

        if (skip < 0)
        {
            throw new ExpenseRuleException(ExpenseRuleFailure.Invalid, "Offset must be zero or greater.");
        }

        var storedCategory = ExpenseText.OptionalCategoryFilter(category);
        ExpenseText.RequireDateRange(from, to);
        return ListCoreAsync(storedCategory, from, to, take, skip, cancellationToken);
    }

    public async Task<ExpenseResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return ToResponse(await RequireAsync(id, cancellationToken));
    }

    public async Task<ExpenseResponse> CreateAsync(CreateExpenseCommand command, CancellationToken cancellationToken)
    {
        var expense = Expense.Create(
            command.OperationId,
            command.Category,
            command.Amount,
            command.ExpenseDate,
            command.Notes,
            command.PaymentMethod,
            _clock.UtcNow);
        try
        {
            await _expenses.AddAsync(expense, cancellationToken);
            return ToResponse(expense);
        }
        catch (ExpenseRuleException exception) when (exception.Failure == ExpenseRuleFailure.Conflict)
        {
            return ToResponse(await ReplayOrRejectAsync(expense, cancellationToken));
        }
    }

    public async Task<ExpenseResponse> UpdateAsync(
        Guid id,
        SaveExpenseCommand command,
        CancellationToken cancellationToken)
    {
        var expense = await RequireAsync(id, cancellationToken);
        expense.Update(
            command.Category,
            command.Amount,
            command.ExpenseDate,
            command.Notes,
            command.PaymentMethod,
            _clock.UtcNow);
        await _expenses.UpdateAsync(expense, cancellationToken);
        return ToResponse(expense);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var expense = await RequireAsync(id, cancellationToken);
        expense.SoftDelete(_clock.UtcNow);
        await _expenses.UpdateAsync(expense, cancellationToken);
    }

    private async Task<ExpenseListResponse> ListCoreAsync(
        string? category,
        DateOnly? from,
        DateOnly? to,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var page = await _expenses.ListAsync(category, from, to, limit, offset, cancellationToken);
        return new ExpenseListResponse(page.Expenses.Select(ToResponse).ToArray(), page.Total);
    }

    private async Task<Expense> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _expenses.FindAsync(id, cancellationToken)
            ?? throw new ExpenseRuleException(ExpenseRuleFailure.NotFound, "Expense was not found.");
    }

    private async Task<Expense> ReplayOrRejectAsync(Expense attempted, CancellationToken cancellationToken)
    {
        var existing = await _expenses.FindByOperationAsync(attempted.OperationId, cancellationToken);
        if (existing is null)
        {
            throw new ExpenseRuleException(ExpenseRuleFailure.Conflict, "This operation key was already used.");
        }

        if (!string.Equals(existing.OperationFingerprint, attempted.OperationFingerprint, StringComparison.Ordinal)
            || existing.IsDeleted)
        {
            throw new ExpenseRuleException(
                ExpenseRuleFailure.Conflict,
                existing.IsDeleted
                    ? "This operation key was already used."
                    : "This operation key was already used for a different expense.");
        }

        return existing;
    }

    private static ExpenseResponse ToResponse(Expense expense)
    {
        return new ExpenseResponse(
            expense.Id,
            expense.Category,
            expense.Amount,
            expense.ExpenseDate,
            expense.Notes,
            expense.PaymentMethod,
            expense.CreatedAt,
            expense.UpdatedAt);
    }
}
