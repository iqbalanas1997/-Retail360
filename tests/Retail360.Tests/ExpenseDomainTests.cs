using Retail360.Domain.Expenses;
using Xunit;

namespace Retail360.Tests;

public class ExpenseDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_keeps_the_amount_date_and_trimmed_notes()
    {
        var operationId = Guid.NewGuid();
        var expense = Expense.Create(operationId, " rent ", 12.50m, new DateOnly(2026, 10, 22), "  Rent ", "cash", Now);

        Assert.Equal(operationId, expense.OperationId);
        Assert.Equal(ExpenseText.Rent, expense.Category);
        Assert.Equal(12.50m, expense.Amount);
        Assert.Equal(new DateOnly(2026, 10, 22), expense.ExpenseDate);
        Assert.Equal("Rent", expense.Notes);
        Assert.Equal(ExpenseText.Cash, expense.PaymentMethod);
        Assert.Equal(Now, expense.CreatedAt);
        Assert.False(expense.IsDeleted);
        Assert.Equal(
            ExpenseText.Fingerprint(ExpenseText.Rent, 12.50m, new DateOnly(2026, 10, 22), "Rent", ExpenseText.Cash),
            expense.OperationFingerprint);
    }

    [Fact]
    public void Zero_negative_and_extra_digits_are_rejected()
    {
        Assert.Equal(
            "Amount must be greater than zero with at most 2 decimal places.",
            Reject(0m).Message);
        Assert.Equal(
            "Amount must be greater than zero with at most 2 decimal places.",
            Reject(-1m).Message);
        Assert.Equal(
            "Amount must be greater than zero with at most 2 decimal places.",
            Reject(1.235m).Message);
    }

    private static ExpenseRuleException Reject(decimal amount)
    {
        return Assert.Throws<ExpenseRuleException>(() =>
            Expense.Create(Guid.NewGuid(), ExpenseText.Rent, amount, new DateOnly(2026, 10, 22), null, ExpenseText.Cash, Now));
    }

    [Fact]
    public void Required_fields_and_an_unknown_category_are_rejected()
    {
        var missingOperation = Assert.Throws<ExpenseRuleException>(() =>
            Expense.Create(Guid.Empty, ExpenseText.Rent, 1m, new DateOnly(2026, 10, 22), null, ExpenseText.Cash, Now));
        var missingCategory = Assert.Throws<ExpenseRuleException>(() =>
            Expense.Create(Guid.NewGuid(), " ", 1m, new DateOnly(2026, 10, 22), null, ExpenseText.Cash, Now));
        var missingDate = Assert.Throws<ExpenseRuleException>(() =>
            Expense.Create(Guid.NewGuid(), ExpenseText.Other, 1m, default, null, ExpenseText.Cash, Now));
        var payment = Assert.Throws<ExpenseRuleException>(() =>
            Expense.Create(Guid.NewGuid(), ExpenseText.Other, 1m, new DateOnly(2026, 10, 22), null, "card", Now));
        var range = Assert.Throws<ExpenseRuleException>(() =>
            ExpenseText.RequireDateRange(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 1)));

        Assert.Equal("Operation id is required.", missingOperation.Message);
        Assert.Equal("Category must be rent, utilities, wages, transport, supplies, repairs, or other.", missingCategory.Message);
        Assert.Equal("Expense date is required.", missingDate.Message);
        Assert.Equal("Payment method must be cash.", payment.Message);
        Assert.Equal("The start date must be on or before the end date.", range.Message);
    }

    [Fact]
    public void Update_replaces_the_fields_and_keeps_the_original_operation()
    {
        var expense = Expense.Create(
            Guid.NewGuid(),
            ExpenseText.Utilities,
            10.00m,
            new DateOnly(2026, 10, 1),
            null,
            ExpenseText.Cash,
            Now);
        var fingerprint = expense.OperationFingerprint;
        var later = Now.AddMinutes(5);

        expense.Update(ExpenseText.Wages, 10.10m, new DateOnly(2026, 10, 2), "Weekly", ExpenseText.Cash, later);
        expense.SoftDelete(later.AddMinutes(1));
        var hidden = Assert.Throws<ExpenseRuleException>(() =>
            expense.Update(ExpenseText.Wages, 1m, new DateOnly(2026, 10, 2), null, ExpenseText.Cash, later));

        Assert.Equal(ExpenseText.Wages, expense.Category);
        Assert.Equal(10.10m, expense.Amount);
        Assert.Equal("Weekly", expense.Notes);
        Assert.Equal(fingerprint, expense.OperationFingerprint);
        Assert.Equal(Now, expense.CreatedAt);
        Assert.True(expense.IsDeleted);
        Assert.Equal(ExpenseRuleFailure.NotFound, hidden.Failure);
    }
}
