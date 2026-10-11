using Retail360.Domain.Catalog;

namespace Retail360.Application.Catalog;

public sealed class UnitService
{
    private readonly IUnitStore _units;
    private readonly IClock _clock;

    public UnitService(IUnitStore units, IClock clock)
    {
        _units = units;
        _clock = clock;
    }

    public async Task<IReadOnlyList<NamedCatalogResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var units = await _units.ListAsync(cancellationToken);
        return units.Select(ToResponse).ToArray();
    }

    public async Task<NamedCatalogResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var unit = await RequireAsync(id, cancellationToken);
        return ToResponse(unit);
    }

    public async Task<NamedCatalogResponse> CreateAsync(SaveNameCommand command, CancellationToken cancellationToken)
    {
        var unit = Unit.Create(command.Name, _clock.UtcNow);
        await EnsureNameAvailable(unit.Name, unit.Id, cancellationToken);
        await _units.AddAsync(unit, cancellationToken);
        return ToResponse(unit);
    }

    public async Task<NamedCatalogResponse> UpdateAsync(
        Guid id,
        SaveNameCommand command,
        CancellationToken cancellationToken)
    {
        var unit = await RequireAsync(id, cancellationToken);
        unit.Rename(command.Name, _clock.UtcNow);
        await EnsureNameAvailable(unit.Name, unit.Id, cancellationToken);
        await _units.UpdateAsync(unit, cancellationToken);
        return ToResponse(unit);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var unit = await RequireAsync(id, cancellationToken);
        if (await _units.IsReferencedByActiveProductAsync(id, cancellationToken))
        {
            throw new CatalogRuleException(
                CatalogRuleFailure.Conflict,
                "Unit is still used by a product.");
        }

        unit.SoftDelete(_clock.UtcNow);
        await _units.UpdateAsync(unit, cancellationToken);
    }

    private async Task<Unit> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _units.FindAsync(id, cancellationToken)
            ?? throw new CatalogRuleException(CatalogRuleFailure.NotFound, "Unit was not found.");
    }

    private async Task EnsureNameAvailable(string name, Guid currentId, CancellationToken cancellationToken)
    {
        var existing = await _units.FindByNameAsync(name, cancellationToken);
        if (existing is not null && existing.Id != currentId)
        {
            throw new CatalogRuleException(
                CatalogRuleFailure.Conflict,
                "A unit with this name already exists.");
        }
    }

    private static NamedCatalogResponse ToResponse(Unit unit)
    {
        return new NamedCatalogResponse(unit.Id, unit.Name, unit.CreatedAt, unit.UpdatedAt);
    }
}
