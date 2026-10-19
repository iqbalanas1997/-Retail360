namespace Retail360.Application.Sales;

public interface ISaleStore
{
    Task<SaleResponse?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<SaleResponse>> ListAsync(
        int? receiptNumber,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<SaleResponse> CompleteAsync(
        Guid operationId,
        string fingerprint,
        IReadOnlyList<CompleteSaleLineCommand> lines,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);
}
