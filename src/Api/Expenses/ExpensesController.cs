using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail360.Api.Identity;
using Retail360.Application.Expenses;
using Retail360.Domain.Expenses;

namespace Retail360.Api.Expenses;

[ApiController]
[Authorize(Policy = ShopPolicies.ExpenseRead)]
[Route("api/expenses")]
public sealed class ExpensesController : ControllerBase
{
    private readonly ExpenseService _expenses;

    public ExpensesController(ExpenseService expenses)
    {
        _expenses = expenses;
    }

    [HttpGet]
    public async Task<ActionResult<ExpenseListResponse>> List(
        [FromQuery] string? category,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        return Ok(await _expenses.ListAsync(category, from, to, limit, offset, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExpenseResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _expenses.GetAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.ExpenseWrite)]
    [HttpPost]
    public async Task<ActionResult<ExpenseResponse>> Create(
        CreateExpenseCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        var created = await _expenses.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [Authorize(Policy = ShopPolicies.ExpenseWrite)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ExpenseResponse>> Update(
        Guid id,
        SaveExpenseCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        return Ok(await _expenses.UpdateAsync(id, command, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.ExpenseWrite)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _expenses.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private static void RequireBody(object? command)
    {
        if (command is null)
        {
            throw new ExpenseRuleException(ExpenseRuleFailure.Invalid, "Request body is required.");
        }
    }
}
