using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail360.Api.Identity;
using Retail360.Application.Sales;
using Retail360.Domain.Sales;

namespace Retail360.Api.Sales;

[ApiController]
[Authorize(Policy = ShopPolicies.SaleRead)]
[Route("api/sales")]
public sealed class SalesController : ControllerBase
{
    private readonly SaleService _sales;

    public SalesController(SaleService sales)
    {
        _sales = sales;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SaleResponse>>> List(
        [FromQuery] int? receiptNumber,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        return Ok(await _sales.ListAsync(receiptNumber, limit, offset, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SaleResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _sales.GetAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.SaleComplete)]
    [HttpPost]
    public async Task<ActionResult<SaleResponse>> Complete(
        [FromBody] CompleteSaleCommand? command,
        CancellationToken cancellationToken)
    {
        if (command is null)
        {
            throw new SaleRuleException(SaleRuleFailure.Invalid, "Request body is required.");
        }

        var created = await _sales.CompleteAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }
}
