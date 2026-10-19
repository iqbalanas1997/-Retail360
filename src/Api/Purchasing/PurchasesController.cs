using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail360.Api.Identity;
using Retail360.Application.Purchasing;
using Retail360.Domain.Purchasing;

namespace Retail360.Api.Purchasing;

[ApiController]
[Authorize(Policy = ShopPolicies.PurchaseRead)]
[Route("api/purchases")]
public sealed class PurchasesController : ControllerBase
{
    private readonly PurchaseService _purchases;

    public PurchasesController(PurchaseService purchases)
    {
        _purchases = purchases;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PurchaseResponse>>> List(
        [FromQuery] Guid? supplierId,
        [FromQuery] string? status,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        return Ok(await _purchases.ListAsync(supplierId, status, limit, offset, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PurchaseResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _purchases.GetAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.PurchaseRecord)]
    [HttpPost]
    public async Task<ActionResult<PurchaseResponse>> Create(
        SavePurchaseCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        var created = await _purchases.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [Authorize(Policy = ShopPolicies.PurchaseRecord)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PurchaseResponse>> Update(
        Guid id,
        SavePurchaseCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        return Ok(await _purchases.UpdateAsync(id, command, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.PurchaseRecord)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _purchases.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = ShopPolicies.PurchaseReceive)]
    [HttpPost("{id:guid}/receive")]
    public async Task<ActionResult<PurchaseResponse>> Receive(
        Guid id,
        ReceivePurchaseCommand command,
        CancellationToken cancellationToken)
    {
        return Ok(await _purchases.ReceiveAsync(id, command, cancellationToken));
    }

    private static void RequireBody(SavePurchaseCommand? command)
    {
        if (command is null)
        {
            throw new PurchaseRuleException(PurchaseRuleFailure.Invalid, "Request body is required.");
        }
    }
}
