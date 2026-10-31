using Dapper;
using Retail360.Application.Customers;
using Retail360.Domain.Audit;
using Retail360.Domain.Customers;
using Retail360.Infrastructure.Audit;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Customers;

public sealed class CustomerStore : ICustomerStore
{
    private const string SelectColumns = """
        select id, name, phone, email, address, notes, is_active, created_at, updated_at, is_deleted
        from customers
        """;

    private readonly NpgsqlConnectionFactory _connections;
    private readonly PostgresTransaction _transactions;

    public CustomerStore(NpgsqlConnectionFactory connections, PostgresTransaction transactions)
    {
        _connections = connections;
        _transactions = transactions;
    }

    public async Task<Customer?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<CustomerRow>(new CommandDefinition(
            $"{SelectColumns} where id = @Id and is_deleted = false;",
            new { Id = id },
            cancellationToken: cancellationToken));
        return row is null ? null : ToCustomer(row);
    }

    public async Task<IReadOnlyList<Customer>> ListAsync(
        string? search,
        bool activeOnly,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<CustomerRow>(new CommandDefinition(
            $"""
            {SelectColumns}
            where is_deleted = false
              and (@ActiveOnly = false or is_active = true)
              and (
                    @Pattern is null
                    or name ilike @Pattern escape '\'
                    or phone ilike @Pattern escape '\'
                    or email ilike @Pattern escape '\'
                  )
            order by lower(name), id
            limit @Limit offset @Offset;
            """,
            new
            {
                ActiveOnly = activeOnly,
                Pattern = ContainsPattern(search),
                Limit = limit,
                Offset = offset
            },
            cancellationToken: cancellationToken));
        return rows.Select(ToCustomer).ToArray();
    }

    public Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            insert into customers (
                id, name, phone, email, address, notes, is_active,
                created_at, updated_at, is_deleted)
            values (
                @Id, @Name, @Phone, @Email, @Address, @Notes, @IsActive,
                @CreatedAt, @UpdatedAt, @IsDeleted);
            """,
            customer,
            requireRow: false,
            cancellationToken);
    }

    public Task UpdateAsync(Customer customer, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            """
            update customers
            set name = @Name,
                phone = @Phone,
                email = @Email,
                address = @Address,
                notes = @Notes,
                is_active = @IsActive,
                updated_at = @UpdatedAt,
                is_deleted = @IsDeleted
            where id = @Id and is_deleted = false;
            """,
            customer,
            requireRow: true,
            cancellationToken);
    }

    private async Task ExecuteAsync(
        string sql,
        Customer customer,
        bool requireRow,
        CancellationToken cancellationToken)
    {
        await _transactions.ExecuteAsync(async (connection, transaction, token) =>
        {
            bool? previousActive = null;
            if (requireRow)
            {
                previousActive = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
                    """
                    select is_active
                    from customers
                    where id = @Id and is_deleted = false
                    for update;
                    """,
                    new { customer.Id },
                    transaction,
                    cancellationToken: token));
                if (previousActive is null)
                {
                    throw new CustomerRuleException(CustomerRuleFailure.NotFound, "Customer was not found.");
                }
            }

            var rows = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                customer,
                transaction,
                cancellationToken: token));
            if (requireRow && rows == 0)
            {
                throw new CustomerRuleException(CustomerRuleFailure.NotFound, "Customer was not found.");
            }

            var action = !requireRow
                ? AuditActions.CustomerCreated
                : customer.IsDeleted
                    ? AuditActions.CustomerDeleted
                    : previousActive is bool wasActive && wasActive != customer.IsActive
                        ? customer.IsActive ? AuditActions.CustomerActivated : AuditActions.CustomerDeactivated
                        : AuditActions.CustomerUpdated;
            await AuditEntryWriter.AppendAsync(
                connection,
                transaction,
                action,
                AuditEntityTypes.Customer,
                customer.Id.ToString("D"),
                customer.UpdatedAt,
                new Dictionary<string, string?> { ["name"] = customer.Name },
                null,
                token);
            return 0;
        }, cancellationToken);
    }

    private static string? ContainsPattern(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var escaped = search.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static Customer ToCustomer(CustomerRow row)
    {
        return Customer.Restore(
            row.Id,
            row.Name,
            row.Phone,
            row.Email,
            row.Address,
            row.Notes,
            row.IsActive,
            row.CreatedAt,
            row.UpdatedAt,
            row.IsDeleted);
    }

    private sealed class CustomerRow
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string? Phone { get; init; }

        public string? Email { get; init; }

        public string? Address { get; init; }

        public string? Notes { get; init; }

        public bool IsActive { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public bool IsDeleted { get; init; }
    }
}
