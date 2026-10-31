using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail360.Application.Identity;

namespace Retail360.Api.Identity;

[ApiController]
[Authorize(Policy = ShopPolicies.UserManage)]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly UserService _users;

    public UsersController(UserService users)
    {
        _users = users;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> List(
        [FromQuery] string? name,
        [FromQuery] bool activeOnly,
        [FromQuery] int? limit,
        [FromQuery] int? offset,
        CancellationToken cancellationToken)
    {
        return Ok(await _users.ListAsync(name, activeOnly, limit, offset, cancellationToken));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserResponse>> Get(string id, CancellationToken cancellationToken)
    {
        return Ok(await _users.GetAsync(id, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(SaveUserCommand? command, CancellationToken cancellationToken)
    {
        if (command is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        var created = await _users.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<UserResponse>> Update(
        string id,
        SaveUserCommand? command,
        CancellationToken cancellationToken)
    {
        if (command is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        return Ok(await _users.UpdateAsync(ActorId(), id, command, cancellationToken));
    }

    [HttpPost("{id}/activate")]
    public async Task<ActionResult<UserResponse>> Activate(string id, CancellationToken cancellationToken)
    {
        return Ok(await _users.SetActiveAsync(ActorId(), id, true, cancellationToken));
    }

    [HttpPost("{id}/deactivate")]
    public async Task<ActionResult<UserResponse>> Deactivate(string id, CancellationToken cancellationToken)
    {
        return Ok(await _users.SetActiveAsync(ActorId(), id, false, cancellationToken));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        await _users.RemoveAsync(ActorId(), id, cancellationToken);
        return NoContent();
    }

    private string ActorId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    }
}
