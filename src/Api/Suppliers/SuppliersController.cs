using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail360.Api.Identity;
using Retail360.Application.Suppliers;
using Retail360.Domain.Suppliers;

namespace Retail360.Api.Suppliers;

[ApiController]
[Authorize(Policy = ShopPolicies.SupplierRead)]
[Route("api/suppliers")]
public sealed class SuppliersController : ControllerBase
{
    private readonly SupplierService _suppliers;

    public SuppliersController(SupplierService suppliers)
    {
        _suppliers = suppliers;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SupplierResponse>>> List(
        [FromQuery] string? name,
        [FromQuery] bool activeOnly,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        return Ok(await _suppliers.ListAsync(name, activeOnly, limit, offset, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupplierResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _suppliers.GetAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.SupplierWrite)]
    [HttpPost]
    public async Task<ActionResult<SupplierResponse>> Create(
        SaveSupplierCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        var created = await _suppliers.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [Authorize(Policy = ShopPolicies.SupplierWrite)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SupplierResponse>> Update(
        Guid id,
        SaveSupplierCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        return Ok(await _suppliers.UpdateAsync(id, command, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.SupplierWrite)]
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<SupplierResponse>> Activate(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _suppliers.ActivateAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.SupplierWrite)]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<SupplierResponse>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _suppliers.DeactivateAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.SupplierWrite)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _suppliers.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private static void RequireBody(SaveSupplierCommand? command)
    {
        if (command is null)
        {
            throw new SupplierRuleException(SupplierRuleFailure.Invalid, "Request body is required.");
        }
    }
}
