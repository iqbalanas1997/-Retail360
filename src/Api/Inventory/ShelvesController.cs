using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail360.Api.Identity;
using Retail360.Application.Catalog;
using Retail360.Application.Inventory;
using Retail360.Domain.Inventory;

namespace Retail360.Api.Inventory;

[ApiController]
[Authorize(Policy = ShopPolicies.InventoryRead)]
[Route("api/shelves")]
public sealed class ShelvesController : ControllerBase
{
    private readonly ShelfService _shelves;

    public ShelvesController(ShelfService shelves)
    {
        _shelves = shelves;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ShelfResponse>>> List(
        [FromQuery] Guid? areaId,
        CancellationToken cancellationToken)
    {
        return Ok(await _shelves.ListAsync(areaId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ShelfResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _shelves.GetAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.InventoryManage)]
    [HttpPost]
    public async Task<ActionResult<ShelfResponse>> Create(SaveShelfCommand command, CancellationToken cancellationToken)
    {
        RequireBody(command);
        var created = await _shelves.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [Authorize(Policy = ShopPolicies.InventoryManage)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ShelfResponse>> Rename(
        Guid id,
        SaveNameCommand command,
        CancellationToken cancellationToken)
    {
        if (command is null)
        {
            throw new InventoryRuleException(InventoryRuleFailure.Invalid, "Request body is required.");
        }

        return Ok(await _shelves.RenameAsync(id, command.Name, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.InventoryManage)]
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<ShelfResponse>> Activate(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _shelves.ActivateAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.InventoryManage)]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<ShelfResponse>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _shelves.DeactivateAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.InventoryManage)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _shelves.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private static void RequireBody(SaveShelfCommand? command)
    {
        if (command is null)
        {
            throw new InventoryRuleException(InventoryRuleFailure.Invalid, "Request body is required.");
        }
    }
}
