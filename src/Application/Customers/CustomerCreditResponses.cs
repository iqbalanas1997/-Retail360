namespace Retail360.Application.Customers;

public sealed record RecordCustomerPaymentCommand(Guid OperationId, decimal Amount);

public sealed record CustomerBalanceResponse(Guid CustomerId, decimal Outstanding);

public sealed record CustomerCreditAllocationResponse(Guid SaleId, int ReceiptNumber, decimal Amount);

public sealed record CustomerPaymentResponse(
    Guid Id,
    Guid CustomerId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    decimal Outstanding,
    IReadOnlyList<CustomerCreditAllocationResponse> Allocations);

public sealed record CustomerLedgerEntryResponse(
    Guid Id,
    string EntryType,
    decimal Amount,
    decimal OpenAmount,
    DateTimeOffset OccurredAt,
    Guid? SaleId,
    int? ReceiptNumber,
    IReadOnlyList<CustomerCreditAllocationResponse> Allocations);
