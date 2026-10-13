using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail360.Api.Identity;
using Retail360.Application.Catalog;
using Retail360.Domain.Catalog;

namespace Retail360.Api.Catalog;

[ApiController]
[Authorize(Policy = ShopPolicies.CatalogRead)]
[Route("api/units")]
public sealed class UnitsController : ControllerBase
{
    private readonly UnitService _units;

    public UnitsController(UnitService units)
    {
        _units = units;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NamedCatalogResponse>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _units.ListAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NamedCatalogResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _units.GetAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.CatalogWrite)]
    [HttpPost]
    public async Task<ActionResult<NamedCatalogResponse>> Create(
        SaveNameCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        var created = await _units.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [Authorize(Policy = ShopPolicies.CatalogWrite)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<NamedCatalogResponse>> Update(
        Guid id,
        SaveNameCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        return Ok(await _units.UpdateAsync(id, command, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.CatalogWrite)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _units.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private static void RequireBody(SaveNameCommand? command)
    {
        if (command is null)
        {
            throw new CatalogRuleException(CatalogRuleFailure.Invalid, "Request body is required.");
        }
    }
}
