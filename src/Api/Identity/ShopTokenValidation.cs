using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Retail360.Application.Catalog;
using Retail360.Application.Identity;

namespace Retail360.Api.Identity;

public static class ShopTokenValidation
{
    public static async Task ReplaceRolesFromDatabaseAsync(TokenValidatedContext context)
    {
        var userId = context.Principal?.FindFirst("sub")?.Value;
        var users = context.HttpContext.RequestServices.GetRequiredService<IShopUserDirectory>();
        var clock = context.HttpContext.RequestServices.GetRequiredService<IClock>();
        var account = string.IsNullOrEmpty(userId)
            ? null
            : await users.FindActiveByIdAsync(userId, clock.UtcNow, context.HttpContext.RequestAborted);
        if (account is null)
        {
            context.Fail("The access token is not valid.");
            return;
        }

        var roles = await users.ListRoleNamesAsync(account.Id, context.HttpContext.RequestAborted);
        var identity = new ClaimsIdentity(
            authenticationType: JwtBearerDefaults.AuthenticationScheme,
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, account.Id));
        identity.AddClaim(new Claim(ClaimTypes.Name, account.UserName));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        context.Principal = new ClaimsPrincipal(identity);
    }
}
