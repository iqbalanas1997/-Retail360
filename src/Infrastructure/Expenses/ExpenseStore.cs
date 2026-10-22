using Dapper;
using Npgsql;
using Retail360.Application.Expenses;
using Retail360.Domain.Expenses;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Expenses;

public sealed class ExpenseStore : IExpenseStore
{
    private const string SelectColumns = """
        select id, operation_id, operation_fingerprint, category, amount, expense_date, notes,
               payment_method, created_at, updated_at, is_deleted
        from expenses
        """;

    private const string Filter = """
        where is_deleted = false
          and (@Category::text is null or category = @Category)
          and (@From::date is null or expense_date >= @From)
          and (@To::date is null or expense_date <= @To)
        """;

    private readonly NpgsqlConnectionFactory _connections;
    private readonly PostgresTransaction _transactions;

    public ExpenseStore(NpgsqlConnectionFactory connections, PostgresTransaction transactions)
    {
        _connections = connections;
        _transactions = transactions;
    }

    public async Task<Expense?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ExpenseRow>(new CommandDefinition(
            $"{SelectColumns} where id = @Id and is_deleted = false;",
            new { Id = id },
            cancellationToken: cancellationToken));
        return row is null ? null : ToExpense(row);
    }

    public async Task<Expense?> FindByOperationAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ExpenseRow>(new CommandDefinition(
            $"{SelectColumns} where operation_id = @OperationId;",
            new { OperationId = operationId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToExpense(row);
    }

    public async Task<ExpensePage> ListAsync(
        string? category,
        DateOnly? from,
        DateOnly? to,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var filter = new
        {
            Category = category,
            From = from?.ToDateTime(TimeOnly.MinValue),
            To = to?.ToDateTime(TimeOnly.MinValue)
        };
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ExpenseRow>(new CommandDefinition(
            $"""
            {SelectColumns}
            {Filter}
            order by expense_date desc, id desc
            limit @Limit offset @Offset;
            """,
            new { filter.Category, filter.From, filter.To, Limit = limit, Offset = offset },
            cancellationToken: cancellationToken));
        var total = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            $"select coalesce(sum(amount), 0) from expenses {Filter};",
            filter,
            cancellationToken: cancellationToken));
        return new ExpensePage(rows.Select(ToExpense).ToArray(), total);
    }

    public async Task AddAsync(Expense expense, CancellationToken cancellationToken)
    {
        try
        {
            await _transactions.ExecuteAsync(async (connection, transaction, token) =>
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    insert into expenses (
                        id, operation_id, operation_fingerprint, category, amount, expense_date, notes,
                        payment_method, created_at, updated_at, is_deleted)
                    values (
                        @Id, @OperationId, @OperationFingerprint, @Category, @Amount, @ExpenseDate, @Notes,
                        @PaymentMethod, @CreatedAt, @UpdatedAt, @IsDeleted);
                    """,
                    Parameters(expense),
                    transaction,
                    cancellationToken: token));
                return 0;
            }, cancellationToken);
        }
        catch (ExpenseRuleException)
        {
            throw;
        }
        catch (Exception exception) when (UniqueConflict(exception) is { } conflict)
        {
            throw conflict;
        }
    }

    public async Task UpdateAsync(Expense expense, CancellationToken cancellationToken)
    {
        await _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            var rows = await connection.ExecuteAsync(new CommandDefinition(
                """
                update expenses
                set category = @Category,
                    amount = @Amount,
                    expense_date = @ExpenseDate,
                    notes = @Notes,
                    payment_method = @PaymentMethod,
                    updated_at = @UpdatedAt,
                    is_deleted = @IsDeleted
                where id = @Id and is_deleted = false;
                """,
                Parameters(expense),
                transaction,
                cancellationToken: token));
            if (rows == 0)
            {
                throw new ExpenseRuleException(ExpenseRuleFailure.NotFound, "Expense was not found.");
            }

            return 0;
        }, cancellationToken);
    }

    private static ExpenseRuleException? UniqueConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return new ExpenseRuleException(
                    ExpenseRuleFailure.Conflict,
                    "This operation key was already used.");
            }
        }

        return null;
    }

    private static object Parameters(Expense expense)
    {
        return new
        {
            expense.Id,
            expense.OperationId,
            expense.OperationFingerprint,
            expense.Category,
            expense.Amount,
            ExpenseDate = expense.ExpenseDate.ToDateTime(TimeOnly.MinValue),
            expense.Notes,
            expense.PaymentMethod,
            expense.CreatedAt,
            expense.UpdatedAt,
            expense.IsDeleted
        };
    }

    private static Expense ToExpense(ExpenseRow row)
    {
        return Expense.Restore(
            row.Id,
            row.OperationId,
            row.OperationFingerprint,
            row.Category,
            row.Amount,
            DateOnly.FromDateTime(row.ExpenseDate),
            row.Notes,
            row.PaymentMethod,
            row.CreatedAt,
            row.UpdatedAt,
            row.IsDeleted);
    }

    private sealed class ExpenseRow
    {
        public Guid Id { get; init; }

        public Guid OperationId { get; init; }

        public string OperationFingerprint { get; init; } = string.Empty;

        public string Category { get; init; } = string.Empty;

        public decimal Amount { get; init; }

        public DateTime ExpenseDate { get; init; }

        public string? Notes { get; init; }

        public string PaymentMethod { get; init; } = string.Empty;

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public bool IsDeleted { get; init; }
    }
}
