using Retail360.Application.Catalog;
using Retail360.Domain.Purchasing;

namespace Retail360.Application.Purchasing;

public sealed class PurchaseService
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    private readonly IPurchaseStore _purchases;
    private readonly IClock _clock;

    public PurchaseService(IPurchaseStore purchases, IClock clock)
    {
        _purchases = purchases;
        _clock = clock;
    }

    public Task<IReadOnlyList<PurchaseResponse>> ListAsync(
        Guid? supplierId,
        string? status,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        if (supplierId == Guid.Empty)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, "Supplier is required.");
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normalizedStatus = PurchaseText.RequireStatus(status);
        }

        var take = limit ?? DefaultLimit;
        var skip = offset ?? 0;
        if (take < 1 || take > MaxLimit)
        {
            throw new PurchaseRuleException(
                PurchaseRuleFailure.Invalid,
                $"Limit must be between 1 and {MaxLimit}.");
        }

        if (skip < 0)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, "Offset must be zero or greater.");
        }

        return _purchases.ListAsync(supplierId, normalizedStatus, take, skip, cancellationToken);
    }

    public async Task<PurchaseResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _purchases.FindAsync(id, cancellationToken)
            ?? throw new PurchaseRuleException(PurchaseRuleFailure.NotFound, "Purchase was not found.");
    }

    public Task<PurchaseResponse> CreateAsync(SavePurchaseCommand command, CancellationToken cancellationToken)
    {
        RequireBody(command);
        var purchase = Purchase.Record(
            Guid.NewGuid(),
            command.SupplierId,
            command.PurchaseDate,
            command.Reference,
            command.Notes,
            LineInputs(command.Lines),
            _clock.UtcNow);
        return _purchases.AddAsync(purchase, cancellationToken);
    }

    public async Task<PurchaseResponse> UpdateAsync(
        Guid id,
        SavePurchaseCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        var current = await _purchases.FindAsync(id, cancellationToken)
            ?? throw new PurchaseRuleException(PurchaseRuleFailure.NotFound, "Purchase was not found.");
        if (current.Status != PurchaseText.Recorded)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Conflict, "A received purchase cannot be changed.");
        }

        var purchase = Purchase.Restore(
            current.Id,
            current.SupplierId,
            current.PurchaseDate,
            current.Reference,
            current.Notes,
            current.Status,
            current.Total,
            current.ReceiveOperationId,
            current.ReceivedAt,
            current.Lines.Select(line => PurchaseLine.Restore(
                line.Id,
                line.ProductId,
                line.Quantity,
                line.UnitCost,
                line.LineTotal,
                1,
                line.ShelfId,
                false)),
            current.CreatedAt,
            current.UpdatedAt,
            false);
        purchase.Replace(
            command.SupplierId,
            command.PurchaseDate,
            command.Reference,
            command.Notes,
            LineInputs(command.Lines),
            _clock.UtcNow);
        return await _purchases.ReplaceAsync(purchase, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = await _purchases.FindAsync(id, cancellationToken)
            ?? throw new PurchaseRuleException(PurchaseRuleFailure.NotFound, "Purchase was not found.");
        var purchase = Purchase.Restore(
            current.Id,
            current.SupplierId,
            current.PurchaseDate,
            current.Reference,
            current.Notes,
            current.Status,
            current.Total,
            current.ReceiveOperationId,
            current.ReceivedAt,
            [],
            current.CreatedAt,
            current.UpdatedAt,
            false);
        purchase.SoftDelete(_clock.UtcNow);
        await _purchases.DeleteAsync(purchase, cancellationToken);
    }

    public Task<PurchaseResponse> ReceiveAsync(
        Guid id,
        ReceivePurchaseCommand command,
        CancellationToken cancellationToken)
    {
        if (command is null)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, "Request body is required.");
        }

        var operationId = PurchaseText.RequireId(command.OperationId, "Operation id");
        if (command.Lines is null || command.Lines.Count == 0)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, "Receive each purchase line exactly once.");
        }

        var shelves = new Dictionary<Guid, Guid>();
        foreach (var line in command.Lines)
        {
            var lineId = PurchaseText.RequireId(line.LineId, "Purchase line");
            var shelfId = PurchaseText.RequireId(line.ShelfId, "Shelf");
            if (!shelves.TryAdd(lineId, shelfId))
            {
                throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, "Receive each purchase line exactly once.");
            }
        }

        return _purchases.ReceiveAsync(id, operationId, shelves, _clock.UtcNow, cancellationToken);
    }

    private static void RequireBody(SavePurchaseCommand? command)
    {
        if (command is null)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, "Request body is required.");
        }
    }

    private static IReadOnlyList<(Guid ProductId, decimal Quantity, decimal UnitCost)> LineInputs(
        IReadOnlyList<SavePurchaseLineCommand>? lines)
    {
        return (lines ?? []).Select(line => (line.ProductId, line.Quantity, line.UnitCost)).ToArray();
    }
}
