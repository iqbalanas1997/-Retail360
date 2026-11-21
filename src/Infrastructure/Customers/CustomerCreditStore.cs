using System.Globalization;
using Dapper;
using Npgsql;
using Retail360.Application.Customers;
using Retail360.Domain.Audit;
using Retail360.Domain.Customers;
using Retail360.Domain.Sales;
using Retail360.Infrastructure.Audit;
using Retail360.Infrastructure.Database;

namespace Retail360.Infrastructure.Customers;

public sealed class CustomerCreditStore : ICustomerCreditStore
{
    private readonly NpgsqlConnectionFactory _connections;
    private readonly PostgresTransaction _transactions;

    public CustomerCreditStore(NpgsqlConnectionFactory connections, PostgresTransaction transactions)
    {
        _connections = connections;
        _transactions = transactions;
    }

    public async Task ChargeInTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Sale sale,
        Guid customerId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        if (sale.Total <= 0)
        {
            throw new SaleRuleException(SaleRuleFailure.Invalid, "A credit sale total must be greater than zero.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into customer_ledger_entries (
                id, operation_id, operation_fingerprint, customer_id, sale_id,
                entry_type, amount, occurred_at, created_at)
            values (
                @Id, @OperationId, @Fingerprint, @CustomerId, @SaleId,
                'charge', @Amount, @OccurredAt, @CreatedAt);
            """,
            new
            {
                Id = Guid.NewGuid(),
                OperationId = SaleText.ChargeOperationId(sale.OperationId, customerId),
                Fingerprint = CustomerCreditText.ChargeFingerprint(sale.Id, customerId, sale.Total),
                CustomerId = customerId,
                SaleId = sale.Id,
                Amount = sale.Total,
                OccurredAt = sale.SoldAt,
                CreatedAt = utcNow
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> OutstandingAsync(
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken cancellationToken)
    {
        if (customerIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<OutstandingRow>(new CommandDefinition(
            """
            select customer_id,
                   coalesce(sum(case when entry_type = 'charge' then amount else -amount end), 0) as outstanding
            from customer_ledger_entries
            where customer_id = any(@Ids)
            group by customer_id;
            """,
            new { Ids = customerIds.ToArray() },
            cancellationToken: cancellationToken));
        return rows.ToDictionary(row => row.CustomerId, row => row.Outstanding);
    }

    public async Task<CustomerBalanceResponse> GetBalanceAsync(Guid customerId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var outstanding = await OutstandingAsync(connection, null, customerId, cancellationToken);
        return new CustomerBalanceResponse(customerId, outstanding);
    }

    public async Task<IReadOnlyList<CustomerLedgerEntryResponse>> ListLedgerAsync(
        Guid customerId,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var entries = (await connection.QueryAsync<LedgerRow>(new CommandDefinition(
            """
            select e.id, e.entry_type, e.amount, e.occurred_at, e.sale_id, s.receipt_number
            from customer_ledger_entries e
            left join sales s on s.id = e.sale_id
            where e.customer_id = @CustomerId
            order by e.occurred_at, e.id
            limit @Limit offset @Offset;
            """,
            new { CustomerId = customerId, Limit = limit, Offset = offset },
            cancellationToken: cancellationToken))).ToArray();
        var allocations = await AllocationsAsync(connection, null, entries.Select(entry => entry.Id).ToArray(), cancellationToken);
        return entries.Select(entry => ToLedgerEntry(entry, allocations)).ToArray();
    }

    public async Task<CustomerPaymentResponse> RecordPaymentAsync(
        Guid customerId,
        Guid operationId,
        decimal amount,
        string fingerprint,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _transactions.ExecuteAsync(
                (connection, transaction, token) => RecordInTransactionAsync(
                    connection,
                    transaction,
                    customerId,
                    operationId,
                    amount,
                    fingerprint,
                    utcNow,
                    token),
                cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == "uq_customer_ledger_entries_operation_id")
        {
            return await ReplayOrRejectAsync(operationId, fingerprint, cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.CheckViolation)
        {
            throw new CustomerRuleException(
                CustomerRuleFailure.Conflict,
                "Payment allocations must equal the payment amount.");
        }
    }

    private async Task<CustomerPaymentResponse> RecordInTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid customerId,
        Guid operationId,
        decimal amount,
        string fingerprint,
        DateTimeOffset utcNow,
        CancellationToken token)
    {
        var existing = await connection.QuerySingleOrDefaultAsync<ExistingPayment>(new CommandDefinition(
            """
            select id, operation_fingerprint
            from customer_ledger_entries
            where operation_id = @OperationId
            for update;
            """,
            new { OperationId = operationId },
            transaction,
            cancellationToken: token));
        if (existing is not null)
        {
            return await SamePaymentOrRejectAsync(connection, transaction, existing.Id, existing.OperationFingerprint, fingerprint, token);
        }

        var customer = await connection.QuerySingleOrDefaultAsync<CustomerGate>(new CommandDefinition(
            """
            select is_deleted
            from customers
            where id = @Id
            for update;
            """,
            new { Id = customerId },
            transaction,
            cancellationToken: token));
        if (customer is null || customer.IsDeleted)
        {
            throw new CustomerRuleException(CustomerRuleFailure.NotFound, "Customer was not found.");
        }

        var open = (await connection.QueryAsync<OpenChargeRow>(new CommandDefinition(
            """
            select e.id, e.sale_id, s.receipt_number, e.occurred_at,
                   e.amount - coalesce((
                       select sum(a.amount)
                       from customer_payment_allocations a
                       where a.charge_entry_id = e.id
                   ), 0) as open_amount
            from customer_ledger_entries e
            join sales s on s.id = e.sale_id
            where e.customer_id = @CustomerId
              and e.entry_type = 'charge'
            order by e.occurred_at, e.sale_id;
            """,
            new { CustomerId = customerId },
            transaction,
            cancellationToken: token))).ToArray();
        var slices = CustomerCreditText.Allocate(
            amount,
            open.Where(row => row.OpenAmount > 0)
                .Select(row => new OpenCharge(row.Id, row.SaleId, row.ReceiptNumber, row.OpenAmount))
                .ToArray());
        var paymentId = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into customer_ledger_entries (
                id, operation_id, operation_fingerprint, customer_id, sale_id,
                entry_type, amount, occurred_at, created_at)
            values (
                @Id, @OperationId, @Fingerprint, @CustomerId, null,
                'payment', @Amount, @OccurredAt, @CreatedAt);
            """,
            new
            {
                Id = paymentId,
                OperationId = operationId,
                Fingerprint = fingerprint,
                CustomerId = customerId,
                Amount = amount,
                OccurredAt = utcNow,
                CreatedAt = utcNow
            },
            transaction,
            cancellationToken: token));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into customer_payment_allocations (
                id, payment_entry_id, charge_entry_id, amount, created_at)
            values (
                @Id, @PaymentEntryId, @ChargeEntryId, @Amount, @CreatedAt);
            """,
            slices.Select(slice => new
            {
                Id = Guid.NewGuid(),
                PaymentEntryId = paymentId,
                ChargeEntryId = slice.ChargeEntryId,
                slice.Amount,
                CreatedAt = utcNow
            }),
            transaction,
            cancellationToken: token));
        await AuditEntryWriter.AppendAsync(
            connection,
            transaction,
            AuditActions.CustomerPaymentRecorded,
            AuditEntityTypes.Customer,
            customerId.ToString("D"),
            utcNow,
            new Dictionary<string, string?>
            {
                ["amount"] = amount.ToString("0.00", CultureInfo.InvariantCulture),
                ["paymentEntryId"] = paymentId.ToString("D")
            },
            null,
            token);
        return await ReadPaymentAsync(connection, transaction, paymentId, token);
    }

    private async Task<CustomerPaymentResponse> ReplayOrRejectAsync(
        Guid operationId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var existing = await connection.QuerySingleOrDefaultAsync<ExistingPayment>(new CommandDefinition(
            """
            select id, operation_fingerprint
            from customer_ledger_entries
            where operation_id = @OperationId;
            """,
            new { OperationId = operationId },
            cancellationToken: cancellationToken));
        if (existing is null)
        {
            throw new CustomerRuleException(
                CustomerRuleFailure.Conflict,
                "This operation key was already used for a different payment.");
        }

        return await SamePaymentOrRejectAsync(
            connection,
            null,
            existing.Id,
            existing.OperationFingerprint,
            fingerprint,
            cancellationToken);
    }

    private async Task<CustomerPaymentResponse> SamePaymentOrRejectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid paymentId,
        string storedFingerprint,
        string fingerprint,
        CancellationToken token)
    {
        if (!string.Equals(storedFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new CustomerRuleException(
                CustomerRuleFailure.Conflict,
                "This operation key was already used for a different payment.");
        }

        return await ReadPaymentAsync(connection, transaction, paymentId, token);
    }

    private async Task<CustomerPaymentResponse> ReadPaymentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid paymentId,
        CancellationToken token)
    {
        var payment = await connection.QuerySingleAsync<LedgerRow>(new CommandDefinition(
            """
            select id, customer_id, entry_type, amount, occurred_at, sale_id, null::integer as receipt_number
            from customer_ledger_entries
            where id = @Id;
            """,
            new { Id = paymentId },
            transaction,
            cancellationToken: token));
        var allocations = await AllocationsAsync(connection, transaction, [paymentId], token);
        var outstanding = await OutstandingAsync(connection, transaction, payment.CustomerId, token);
        return new CustomerPaymentResponse(
            payment.Id,
            payment.CustomerId,
            payment.Amount,
            payment.OccurredAt,
            outstanding,
            allocations.GetValueOrDefault(paymentId) ?? []);
    }

    private static async Task<decimal> OutstandingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid customerId,
        CancellationToken token)
    {
        return await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            """
            select coalesce(sum(case when entry_type = 'charge' then amount else -amount end), 0)
            from customer_ledger_entries
            where customer_id = @CustomerId;
            """,
            new { CustomerId = customerId },
            transaction,
            cancellationToken: token));
    }

    private static async Task<Dictionary<Guid, IReadOnlyList<CustomerCreditAllocationResponse>>> AllocationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid[] entryIds,
        CancellationToken token)
    {
        if (entryIds.Length == 0)
        {
            return [];
        }

        var rows = (await connection.QueryAsync<AllocationRow>(new CommandDefinition(
            """
            select a.payment_entry_id, a.charge_entry_id, a.amount, c.sale_id, s.receipt_number
            from customer_payment_allocations a
            join customer_ledger_entries c on c.id = a.charge_entry_id
            join sales s on s.id = c.sale_id
            where a.payment_entry_id = any(@Ids)
               or a.charge_entry_id = any(@Ids)
            order by s.sold_at, s.id;
            """,
            new { Ids = entryIds },
            transaction,
            cancellationToken: token))).ToArray();
        var grouped = new Dictionary<Guid, List<CustomerCreditAllocationResponse>>();
        foreach (var row in rows)
        {
            Add(grouped, row.PaymentEntryId, row);
            if (row.ChargeEntryId != row.PaymentEntryId)
            {
                Add(grouped, row.ChargeEntryId, row);
            }
        }

        return grouped.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<CustomerCreditAllocationResponse>)pair.Value);
    }

    private static void Add(
        Dictionary<Guid, List<CustomerCreditAllocationResponse>> grouped,
        Guid entryId,
        AllocationRow row)
    {
        if (!grouped.TryGetValue(entryId, out var list))
        {
            list = [];
            grouped[entryId] = list;
        }

        list.Add(new CustomerCreditAllocationResponse(row.SaleId, row.ReceiptNumber, row.Amount));
    }

    private static CustomerLedgerEntryResponse ToLedgerEntry(
        LedgerRow entry,
        IReadOnlyDictionary<Guid, IReadOnlyList<CustomerCreditAllocationResponse>> allocations)
    {
        var applied = allocations.GetValueOrDefault(entry.Id) ?? [];
        var allocated = applied.Sum(allocation => allocation.Amount);
        var open = entry.EntryType == CustomerCreditText.Charge ? entry.Amount - allocated : 0;
        return new CustomerLedgerEntryResponse(
            entry.Id,
            entry.EntryType,
            entry.Amount,
            open,
            entry.OccurredAt,
            entry.SaleId,
            entry.ReceiptNumber,
            applied);
    }

    private sealed class OutstandingRow
    {
        public Guid CustomerId { get; init; }

        public decimal Outstanding { get; init; }
    }

    private sealed class CustomerGate
    {
        public bool IsDeleted { get; init; }
    }

    private sealed class ExistingPayment
    {
        public Guid Id { get; init; }

        public string OperationFingerprint { get; init; } = string.Empty;
    }

    private sealed class OpenChargeRow
    {
        public Guid Id { get; init; }

        public Guid SaleId { get; init; }

        public int ReceiptNumber { get; init; }

        public decimal OpenAmount { get; init; }
    }

    private sealed class LedgerRow
    {
        public Guid Id { get; init; }

        public Guid CustomerId { get; init; }

        public string EntryType { get; init; } = string.Empty;

        public decimal Amount { get; init; }

        public DateTimeOffset OccurredAt { get; init; }

        public Guid? SaleId { get; init; }

        public int? ReceiptNumber { get; init; }
    }

    private sealed class AllocationRow
    {
        public Guid PaymentEntryId { get; init; }

        public Guid ChargeEntryId { get; init; }

        public decimal Amount { get; init; }

        public Guid SaleId { get; init; }

        public int ReceiptNumber { get; init; }
    }
}
