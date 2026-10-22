namespace Retail360.Application.Expenses;

public sealed record CreateExpenseCommand(
    Guid OperationId,
    string? Category,
    decimal Amount,
    DateOnly ExpenseDate,
    string? Notes,
    string? PaymentMethod);

public sealed record SaveExpenseCommand(
    string? Category,
    decimal Amount,
    DateOnly ExpenseDate,
    string? Notes,
    string? PaymentMethod);

public sealed record ExpenseResponse(
    Guid Id,
    string Category,
    decimal Amount,
    DateOnly ExpenseDate,
    string? Notes,
    string PaymentMethod,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ExpenseListResponse(IReadOnlyList<ExpenseResponse> Expenses, decimal Total);
