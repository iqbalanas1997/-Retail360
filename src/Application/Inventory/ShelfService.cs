using Retail360.Application.Catalog;
using Retail360.Domain.Inventory;

namespace Retail360.Application.Inventory;

public sealed class ShelfService
{
    private readonly IShelfStore _shelves;
    private readonly IAreaStore _areas;
    private readonly IClock _clock;

    public ShelfService(IShelfStore shelves, IAreaStore areas, IClock clock)
    {
        _shelves = shelves;
        _areas = areas;
        _clock = clock;
    }

    public Task<IReadOnlyList<ShelfResponse>> ListAsync(Guid? areaId, CancellationToken cancellationToken)
    {
        return _shelves.ListAsync(areaId, cancellationToken);
    }

    public async Task<ShelfResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _shelves.GetAsync(id, cancellationToken)
            ?? throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Shelf was not found.");
    }

    public async Task<ShelfResponse> CreateAsync(SaveShelfCommand command, CancellationToken cancellationToken)
    {
        if (await _areas.FindAsync(command.AreaId, cancellationToken) is null)
        {
            throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Area was not found.");
        }

        var shelf = Shelf.Create(command.AreaId, command.Name, _clock.UtcNow);
        await _shelves.AddAsync(shelf, cancellationToken);
        return await GetAsync(shelf.Id, cancellationToken);
    }

    public async Task<ShelfResponse> RenameAsync(Guid id, string name, CancellationToken cancellationToken)
    {
        var shelf = await RequireAsync(id, cancellationToken);
        shelf.Rename(name, _clock.UtcNow);
        await _shelves.UpdateAsync(shelf, cancellationToken);
        return await GetAsync(shelf.Id, cancellationToken);
    }

    public async Task<ShelfResponse> ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var shelf = await RequireAsync(id, cancellationToken);
        shelf.Activate(_clock.UtcNow);
        await _shelves.UpdateAsync(shelf, cancellationToken);
        return await GetAsync(shelf.Id, cancellationToken);
    }

    public async Task<ShelfResponse> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var shelf = await RequireAsync(id, cancellationToken);
        shelf.Deactivate(_clock.UtcNow);
        await _shelves.UpdateAsync(shelf, cancellationToken);
        return await GetAsync(shelf.Id, cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        return _shelves.SoftDeleteIfNoHistoryAsync(id, _clock.UtcNow, cancellationToken);
    }

    private async Task<Shelf> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _shelves.FindAsync(id, cancellationToken)
            ?? throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Shelf was not found.");
    }
}
