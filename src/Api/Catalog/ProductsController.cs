using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail360.Api.Identity;
using Retail360.Application.Catalog;
using Retail360.Domain.Catalog;

namespace Retail360.Api.Catalog;

[ApiController]
[Authorize(Policy = ShopPolicies.CatalogRead)]
[Route("api/products")]
public sealed class ProductsController : ControllerBase
{
    private readonly ProductService _products;

    public ProductsController(ProductService products)
    {
        _products = products;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> List(
        [FromQuery] string? name,
        [FromQuery] bool activeOnly,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        return Ok(await _products.ListAsync(name, activeOnly, limit, offset, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _products.GetAsync(id, cancellationToken));
    }

    [HttpGet("by-barcode/{barcode}")]
    public async Task<ActionResult<ProductResponse>> FindByBarcode(
        string barcode,
        CancellationToken cancellationToken)
    {
        return Ok(await _products.FindActiveByBarcodeAsync(barcode, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.CatalogWrite)]
    [HttpPost]
    public async Task<ActionResult<ProductResponse>> Create(
        SaveProductCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        var created = await _products.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [Authorize(Policy = ShopPolicies.CatalogWrite)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductResponse>> Update(
        Guid id,
        SaveProductCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        return Ok(await _products.UpdateAsync(id, command, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.CatalogWrite)]
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<ProductResponse>> Activate(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _products.ActivateAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.CatalogWrite)]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<ProductResponse>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _products.DeactivateAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.CatalogWrite)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _products.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private static void RequireBody(SaveProductCommand? command)
    {
        if (command is null)
        {
            throw new CatalogRuleException(CatalogRuleFailure.Invalid, "Request body is required.");
        }
    }
}
