using Retail360.Application.Catalog;
using Retail360.Domain.Sales;

namespace Retail360.Application.Sales;

public sealed class SaleService
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    private readonly ISaleStore _sales;
    private readonly IClock _clock;

    public SaleService(ISaleStore sales, IClock clock)
    {
        _sales = sales;
        _clock = clock;
    }

    public async Task<SaleResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _sales.FindAsync(id, cancellationToken)
            ?? throw new SaleRuleException(SaleRuleFailure.NotFound, "Sale was not found.");
    }

    public Task<IReadOnlyList<SaleResponse>> ListAsync(
        int? receiptNumber,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        if (receiptNumber is <= 0)
        {
            throw new SaleRuleException(SaleRuleFailure.Invalid, "Receipt number must be greater than zero.");
        }

        var pageSize = limit ?? DefaultLimit;
        if (pageSize is < 1 or > MaxLimit)
        {
            throw new SaleRuleException(SaleRuleFailure.Invalid, "Limit must be between 1 and 200.");
        }

        var pageOffset = offset ?? 0;
        if (pageOffset < 0)
        {
            throw new SaleRuleException(SaleRuleFailure.Invalid, "Offset must be zero or greater.");
        }

        return _sales.ListAsync(receiptNumber, pageSize, pageOffset, cancellationToken);
    }

    public Task<SaleResponse> CompleteAsync(CompleteSaleCommand? command, CancellationToken cancellationToken)
    {
        if (command is null)
        {
            throw new SaleRuleException(SaleRuleFailure.Invalid, "Request body is required.");
        }

        var operationId = SaleText.RequireId(command.OperationId, "Operation id");
        var lines = command.Lines ?? [];
        if (lines.Count == 0)
        {
            throw new SaleRuleException(SaleRuleFailure.Invalid, "A sale needs at least one line.");
        }

        if (lines.Count > SaleText.MaxLines)
        {
            throw new SaleRuleException(
                SaleRuleFailure.Invalid,
                $"A sale can have at most {SaleText.MaxLines} lines.");
        }

        var prepared = lines.Select(line =>
        {
            SaleText.RequireId(line.ProductId, "Product");
            SaleText.RequireId(line.ShelfId, "Shelf");
            SaleText.RequireQuantity(line.Quantity);
            return line;
        }).ToArray();
        var fingerprint = SaleText.Fingerprint(prepared.Select(line => (line.ProductId, line.ShelfId, line.Quantity)).ToArray());
        return _sales.CompleteAsync(operationId, fingerprint, prepared, _clock.UtcNow, cancellationToken);
    }
}
