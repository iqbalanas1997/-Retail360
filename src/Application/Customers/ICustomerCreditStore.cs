namespace Retail360.Application.Customers;

public interface ICustomerCreditStore
{
    Task<IReadOnlyDictionary<Guid, decimal>> OutstandingAsync(
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken cancellationToken);

    Task<CustomerBalanceResponse> GetBalanceAsync(Guid customerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerLedgerEntryResponse>> ListLedgerAsync(
        Guid customerId,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<CustomerPaymentResponse> RecordPaymentAsync(
        Guid customerId,
        Guid operationId,
        decimal amount,
        string fingerprint,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);
}
