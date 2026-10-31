using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail360.Api.Identity;
using Retail360.Application.Customers;
using Retail360.Application.Sales;
using Retail360.Domain.Customers;
using Retail360.Domain.Identity;

namespace Retail360.Api.Customers;

[ApiController]
[Authorize(Policy = ShopPolicies.CustomerRead)]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
    private readonly CustomerService _customers;

    public CustomersController(CustomerService customers)
    {
        _customers = customers;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> List(
        [FromQuery] string? search,
        [FromQuery] bool activeOnly,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        var customers = await _customers.ListAsync(search, activeOnly, limit, offset, cancellationToken);
        return Ok(customers.Select(ForCaller).ToArray());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        return Ok(ForCaller(await _customers.GetAsync(id, cancellationToken)));
    }

    [HttpGet("{id:guid}/sales")]
    public async Task<ActionResult<IReadOnlyList<CustomerSaleSummary>>> ListSales(
        Guid id,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        return Ok(await _customers.ListSalesAsync(id, limit, offset, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.CustomerWrite)]
    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Create(
        SaveCustomerCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        var created = await _customers.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [Authorize(Policy = ShopPolicies.CustomerWrite)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> Update(
        Guid id,
        SaveCustomerCommand command,
        CancellationToken cancellationToken)
    {
        RequireBody(command);
        return Ok(await _customers.UpdateAsync(id, command, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.CustomerWrite)]
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<CustomerResponse>> Activate(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _customers.ActivateAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.CustomerWrite)]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<CustomerResponse>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _customers.DeactivateAsync(id, cancellationToken));
    }

    [Authorize(Policy = ShopPolicies.CustomerWrite)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _customers.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private CustomerResponse ForCaller(CustomerResponse customer)
    {
        if (User.IsInRole(ShopRoleNames.Admin) || User.IsInRole(ShopRoleNames.Manager))
        {
            return customer;
        }

        return customer with { Address = null, Notes = null };
    }

    private static void RequireBody(SaveCustomerCommand? command)
    {
        if (command is null)
        {
            throw new CustomerRuleException(CustomerRuleFailure.Invalid, "Request body is required.");
        }
    }
}
