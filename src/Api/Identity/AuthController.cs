using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Retail360.Application.Identity;

namespace Retail360.Api.Identity;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly ShopSignInService _signIn;

    public AuthController(ShopSignInService signIn)
    {
        _signIn = signIn;
    }

    [AllowAnonymous]
    [EnableRateLimiting("sign-in")]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginCommand? command, CancellationToken cancellationToken)
    {
        if (command is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        var response = await _signIn.SignInAsync(command, cancellationToken);
        if (response is null)
        {
            return Unauthorized(new { error = ShopSignInService.InvalidCredentialsMessage });
        }

        return Ok(response);
    }

    [Authorize]
    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordCommand? command,
        CancellationToken cancellationToken)
    {
        if (command is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { error = "Authentication is required." });
        }

        await _signIn.ChangePasswordAsync(userId, command, cancellationToken);
        return NoContent();
    }
}
