using Retail360.Application.Catalog;
using Retail360.Domain.Inventory;

namespace Retail360.Application.Inventory;

public sealed class AreaService
{
    private readonly IAreaStore _areas;
    private readonly IClock _clock;

    public AreaService(IAreaStore areas, IClock clock)
    {
        _areas = areas;
        _clock = clock;
    }

    public async Task<IReadOnlyList<AreaResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var areas = await _areas.ListAsync(cancellationToken);
        return areas.Select(ToResponse).ToArray();
    }

    public async Task<AreaResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return ToResponse(await RequireAsync(id, cancellationToken));
    }

    public async Task<AreaResponse> CreateAsync(SaveAreaCommand command, CancellationToken cancellationToken)
    {
        var area = Area.Create(command.Name, _clock.UtcNow);
        await _areas.AddAsync(area, cancellationToken);
        return ToResponse(area);
    }

    public async Task<AreaResponse> RenameAsync(Guid id, string name, CancellationToken cancellationToken)
    {
        var area = await RequireAsync(id, cancellationToken);
        area.Rename(name, _clock.UtcNow);
        await _areas.UpdateAsync(area, cancellationToken);
        return ToResponse(area);
    }

    public async Task<AreaResponse> ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var area = await RequireAsync(id, cancellationToken);
        area.Activate(_clock.UtcNow);
        await _areas.UpdateAsync(area, cancellationToken);
        return ToResponse(area);
    }

    public async Task<AreaResponse> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var area = await RequireAsync(id, cancellationToken);
        area.Deactivate(_clock.UtcNow);
        await _areas.UpdateAsync(area, cancellationToken);
        return ToResponse(area);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await _areas.SoftDeleteIfNoShelfOrHistoryAsync(id, _clock.UtcNow, cancellationToken);
    }

    private async Task<Area> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _areas.FindAsync(id, cancellationToken)
            ?? throw new InventoryRuleException(InventoryRuleFailure.NotFound, "Area was not found.");
    }

    private static AreaResponse ToResponse(Area area)
    {
        return new AreaResponse(area.Id, area.Name, area.IsActive, area.CreatedAt, area.UpdatedAt);
    }
}
