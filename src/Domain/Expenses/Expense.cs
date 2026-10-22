using Retail360.Domain.Common;

namespace Retail360.Domain.Expenses;

public sealed class Expense : BaseEntity
{
    public Guid OperationId { get; private set; }

    public string OperationFingerprint { get; private set; } = string.Empty;

    public string Category { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public DateOnly ExpenseDate { get; private set; }

    public string? Notes { get; private set; }

    public string PaymentMethod { get; private set; } = string.Empty;

    public static Expense Create(
        Guid operationId,
        string? category,
        decimal amount,
        DateOnly expenseDate,
        string? notes,
        string? paymentMethod,
        DateTimeOffset now)
    {
        var storedCategory = ExpenseText.RequireCategory(category);
        var storedAmount = ExpenseText.RequireAmount(amount);
        var storedDate = ExpenseText.RequireDate(expenseDate);
        var storedNotes = ExpenseText.OptionalNotes(notes);
        var storedPayment = ExpenseText.RequirePaymentMethod(paymentMethod);
        return new Expense
        {
            Id = Guid.NewGuid(),
            OperationId = ExpenseText.RequireOperationId(operationId),
            OperationFingerprint = ExpenseText.Fingerprint(
                storedCategory,
                storedAmount,
                storedDate,
                storedNotes,
                storedPayment),
            Category = storedCategory,
            Amount = storedAmount,
            ExpenseDate = storedDate,
            Notes = storedNotes,
            PaymentMethod = storedPayment,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };
    }

    public static Expense Restore(
        Guid id,
        Guid operationId,
        string operationFingerprint,
        string category,
        decimal amount,
        DateOnly expenseDate,
        string? notes,
        string paymentMethod,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted)
    {
        return new Expense
        {
            Id = id,
            OperationId = operationId,
            OperationFingerprint = operationFingerprint,
            Category = category,
            Amount = amount,
            ExpenseDate = expenseDate,
            Notes = notes,
            PaymentMethod = paymentMethod,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            IsDeleted = isDeleted
        };
    }

    public void Update(
        string? category,
        decimal amount,
        DateOnly expenseDate,
        string? notes,
        string? paymentMethod,
        DateTimeOffset now)
    {
        EnsureAvailable();
        Category = ExpenseText.RequireCategory(category);
        Amount = ExpenseText.RequireAmount(amount);
        ExpenseDate = ExpenseText.RequireDate(expenseDate);
        Notes = ExpenseText.OptionalNotes(notes);
        PaymentMethod = ExpenseText.RequirePaymentMethod(paymentMethod);
        UpdatedAt = now;
    }

    public void SoftDelete(DateTimeOffset now)
    {
        EnsureAvailable();
        IsDeleted = true;
        UpdatedAt = now;
    }

    private void EnsureAvailable()
    {
        if (IsDeleted)
        {
            throw new ExpenseRuleException(ExpenseRuleFailure.NotFound, "Expense was not found.");
        }
    }
}
