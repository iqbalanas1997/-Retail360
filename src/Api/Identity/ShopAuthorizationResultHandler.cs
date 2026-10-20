using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Retail360.Api.Identity;

public sealed class ShopAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly ILogger<ShopAuthorizationResultHandler> _logger;

    public ShopAuthorizationResultHandler(ILogger<ShopAuthorizationResultHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await next(context);
            return;
        }

        context.Response.ContentType = "application/json";
        if (authorizeResult.Challenged)
        {
            _logger.LogInformation(
                "Authentication rejected {Method} {Path}. TraceId={TraceId}",
                context.Request.Method,
                context.Request.Path.Value,
                context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Authentication is required." });
            return;
        }

        _logger.LogWarning(
            "Authorization rejected {Method} {Path}. TraceId={TraceId}",
            context.Request.Method,
            context.Request.Path.Value,
            context.TraceIdentifier);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "You do not have access to this action." });
    }
}
