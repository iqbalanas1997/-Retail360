using Retail360.Domain.Catalog;

namespace Retail360.Api.Catalog;

public sealed class CatalogExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public CatalogExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (CatalogRuleException exception)
        {
            context.Response.StatusCode = exception.Failure switch
            {
                CatalogRuleFailure.NotFound => StatusCodes.Status404NotFound,
                CatalogRuleFailure.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = exception.Message });
        }
    }
}
