using System.Security.Claims;
using Retail360.Application.Audit;

namespace Retail360.Api.Audit;

public sealed class AuditActorMiddleware
{
    private readonly RequestDelegate _next;

    public AuditActorMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        using (AuditActor.Begin(userId))
        {
            await _next(context);
        }
    }
}
