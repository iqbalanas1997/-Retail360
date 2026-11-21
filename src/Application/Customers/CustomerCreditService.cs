using Retail360.Application.Catalog;
using Retail360.Domain.Customers;

namespace Retail360.Application.Customers;

public sealed class CustomerCreditService
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    private readonly ICustomerStore _customers;
    private readonly ICustomerCreditStore _credit;
    private readonly IClock _clock;

    public CustomerCreditService(ICustomerStore customers, ICustomerCreditStore credit, IClock clock)
    {
        _customers = customers;
        _credit = credit;
        _clock = clock;
    }

    public async Task<CustomerBalanceResponse> GetBalanceAsync(Guid customerId, CancellationToken cancellationToken)
    {
        await RequireCustomerAsync(customerId, cancellationToken);
        return await _credit.GetBalanceAsync(customerId, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerLedgerEntryResponse>> ListLedgerAsync(
        Guid customerId,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        await RequireCustomerAsync(customerId, cancellationToken);
        var take = limit ?? DefaultLimit;
        var skip = offset ?? 0;
        if (take < 1 || take > MaxLimit)
        {
            throw new CustomerRuleException(
                CustomerRuleFailure.Invalid,
                $"Limit must be between 1 and {MaxLimit}.");
        }

        if (skip < 0)
        {
            throw new CustomerRuleException(CustomerRuleFailure.Invalid, "Offset must be zero or greater.");
        }

        return await _credit.ListLedgerAsync(customerId, take, skip, cancellationToken);
    }

    public Task<CustomerPaymentResponse> RecordPaymentAsync(
        Guid customerId,
        RecordCustomerPaymentCommand? command,
        CancellationToken cancellationToken)
    {
        if (command is null)
        {
            throw new CustomerRuleException(CustomerRuleFailure.Invalid, "Request body is required.");
        }

        if (customerId == Guid.Empty)
        {
            throw new CustomerRuleException(CustomerRuleFailure.NotFound, "Customer was not found.");
        }

        if (command.OperationId == Guid.Empty)
        {
            throw new CustomerRuleException(CustomerRuleFailure.Invalid, "Operation id is required.");
        }

        var amount = CustomerCreditText.RequireAmount(command.Amount);
        return _credit.RecordPaymentAsync(
            customerId,
            command.OperationId,
            amount,
            CustomerCreditText.PaymentFingerprint(customerId, amount),
            _clock.UtcNow,
            cancellationToken);
    }

    private async Task RequireCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        if (customerId == Guid.Empty
            || await _customers.FindAsync(customerId, cancellationToken) is null)
        {
            throw new CustomerRuleException(CustomerRuleFailure.NotFound, "Customer was not found.");
        }
    }
}
