using Microsoft.AspNetCore.Mvc;
using Retail360.Application.Catalog;
using Retail360.Domain.Catalog;

namespace Retail360.Api.Catalog;

[ApiController]
[Route("api/categories")]
public sealed class CategoriesController : ControllerBase
{
    private readonly CategoryService _categories;

    public CategoriesController(CategoryService categories)
    {
        _categories = categories;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NamedCatalogResponse>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _categories.ListAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NamedCatalogResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _categories.GetAsync(id, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<NamedCatalogResponse>> Create(
        SaveNameCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        var created = await _categories.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<NamedCatalogResponse>> Update(
        Guid id,
        SaveNameCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        return Ok(await _categories.UpdateAsync(id, command, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _categories.DeleteAsync(id, cancellationToken);
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
