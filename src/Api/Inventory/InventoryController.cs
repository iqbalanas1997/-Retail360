using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail360.Api.Identity;
using Retail360.Application.Inventory;
using Retail360.Domain.Inventory;

namespace Retail360.Api.Inventory;

[ApiController]
[Authorize(Policy = ShopPolicies.InventoryRead)]
[Route("api/inventory")]
public sealed class InventoryController : ControllerBase
{
    private readonly StockService _stock;

    public InventoryController(StockService stock)
    {
        _stock = stock;
    }

    [Authorize(Policy = ShopPolicies.InventoryManage)]
    [HttpPost("opening")]
    public async Task<ActionResult<IReadOnlyList<StockMovementResponse>>> Open(
        OpeningStockCommand command,
        CancellationToken cancellationToken)
    {
        Require(command);
        return Ok(await _stock.OpenAsync(command, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.InventoryManage)]
    [HttpPost("adjustments")]
    public async Task<ActionResult<IReadOnlyList<StockMovementResponse>>> Adjust(
        AdjustmentCommand command,
        CancellationToken cancellationToken)
    {
        Require(command);
        return Ok(await _stock.AdjustAsync(command, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.InventoryManage)]
    [HttpPost("transfers")]
    public async Task<ActionResult<IReadOnlyList<StockMovementResponse>>> Transfer(
        TransferCommand command,
        CancellationToken cancellationToken)
    {
        Require(command);
        return Ok(await _stock.TransferAsync(command, cancellationToken));
    }

    [HttpGet("balances")]
    public async Task<ActionResult<IReadOnlyList<ShelfBalanceResponse>>> Balances(
        [FromQuery] Guid? productId,
        [FromQuery] Guid? shelfId,
        CancellationToken cancellationToken)
    {
        return Ok(await _stock.ListBalancesAsync(productId, shelfId, cancellationToken));
    }

    [HttpGet("movements")]
    public async Task<ActionResult<IReadOnlyList<StockMovementResponse>>> Movements(
        [FromQuery] Guid? productId,
        [FromQuery] Guid? shelfId,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        return Ok(await _stock.ListMovementsAsync(productId, shelfId, limit, offset, cancellationToken));
    }

    [HttpGet("stock")]
    public async Task<ActionResult<IReadOnlyList<ProductStockResponse>>> Stock(
        [FromQuery] string? status,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        return Ok(await _stock.ListProductStockAsync(status, limit, offset, cancellationToken));
    }

    [HttpGet("reconciliation")]
    public async Task<ActionResult<IReadOnlyList<ReconciliationRow>>> Reconcile(CancellationToken cancellationToken)
    {
        return Ok(await _stock.ReconcileAsync(cancellationToken));
    }

    private static void Require(object? command)
    {
        if (command is null)
        {
            throw new InventoryRuleException(InventoryRuleFailure.Invalid, "Request body is required.");
        }
    }
}
