using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail360.Api.Identity;
using Retail360.Application.Catalog;
using Retail360.Application.Inventory;
using Retail360.Domain.Inventory;

namespace Retail360.Api.Inventory;

[ApiController]
[Authorize(Policy = ShopPolicies.InventoryRead)]
[Route("api/areas")]
public sealed class AreasController : ControllerBase
{
    private readonly AreaService _areas;

    public AreasController(AreaService areas)
    {
        _areas = areas;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AreaResponse>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _areas.ListAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AreaResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _areas.GetAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.InventoryManage)]
    [HttpPost]
    public async Task<ActionResult<AreaResponse>> Create(SaveAreaCommand command, CancellationToken cancellationToken)
    {
        RequireBody(command);
        var created = await _areas.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [Authorize(Policy = ShopPolicies.InventoryManage)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AreaResponse>> Rename(
        Guid id,
        SaveNameCommand command,
        CancellationToken cancellationToken)
    {
        if (command is null)
        {
            throw new InventoryRuleException(InventoryRuleFailure.Invalid, "Request body is required.");
        }

        return Ok(await _areas.RenameAsync(id, command.Name, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.InventoryManage)]
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<AreaResponse>> Activate(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _areas.ActivateAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.InventoryManage)]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<AreaResponse>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _areas.DeactivateAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.InventoryManage)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _areas.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private static void RequireBody(SaveAreaCommand? command)
    {
        if (command is null)
        {
            throw new InventoryRuleException(InventoryRuleFailure.Invalid, "Request body is required.");
        }
    }
}
