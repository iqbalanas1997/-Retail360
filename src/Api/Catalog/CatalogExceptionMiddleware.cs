using Retail360.Domain.Catalog;
using Retail360.Domain.Inventory;
using Retail360.Domain.Purchasing;
using Retail360.Domain.Sales;
using Retail360.Domain.Suppliers;

namespace Retail360.Api.Catalog;

public sealed class CatalogExceptionMiddleware
{
    public const string UnexpectedMessage = "The request could not be completed.";

    private readonly RequestDelegate _next;
    private readonly ILogger<CatalogExceptionMiddleware> _logger;

    public CatalogExceptionMiddleware(RequestDelegate next, ILogger<CatalogExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (CatalogRuleException exception)
        {
            await WriteRuleAsync(context, exception, StatusFor(exception.Failure));
        }
        catch (InventoryRuleException exception)
        {
            await WriteRuleAsync(context, exception, StatusFor(exception.Failure));
        }
        catch (SupplierRuleException exception)
        {
            await WriteRuleAsync(context, exception, StatusFor(exception.Failure));
        }
        catch (SaleRuleException exception)
        {
            await WriteRuleAsync(context, exception, StatusFor(exception.Failure));
        }
        catch (PurchaseRuleException exception)
        {
            await WriteRuleAsync(context, exception, StatusFor(exception.Failure));
        }
        catch (Exception exception) when (!IsRequestAborted(context, exception))
        {
            _logger.LogError(
                exception,
                "Unhandled shop API exception for {Method} {Path}. TraceId={TraceId}",
                context.Request.Method,
                context.Request.Path.Value,
                context.TraceIdentifier);
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            await WriteBodyAsync(context, StatusCodes.Status500InternalServerError, UnexpectedMessage);
        }
    }

    private async Task WriteRuleAsync(HttpContext context, Exception exception, int statusCode)
    {
        _logger.LogWarning(
            "Shop rule rejected {Method} {Path} with {StatusCode}. {Error} TraceId={TraceId}",
            context.Request.Method,
            context.Request.Path.Value,
            statusCode,
            exception.Message,
            context.TraceIdentifier);
        await WriteBodyAsync(context, statusCode, exception.Message);
    }

    private static async Task WriteBodyAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = message });
    }

    private static bool IsRequestAborted(HttpContext context, Exception exception)
    {
        return exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested;
    }

    private static int StatusFor(CatalogRuleFailure failure)
    {
        return failure switch
        {
            CatalogRuleFailure.NotFound => StatusCodes.Status404NotFound,
            CatalogRuleFailure.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
    }

    private static int StatusFor(InventoryRuleFailure failure)
    {
        return failure switch
        {
            InventoryRuleFailure.NotFound => StatusCodes.Status404NotFound,
            InventoryRuleFailure.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
    }

    private static int StatusFor(SupplierRuleFailure failure)
    {
        return failure switch
        {
            SupplierRuleFailure.NotFound => StatusCodes.Status404NotFound,
            SupplierRuleFailure.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
    }

    private static int StatusFor(SaleRuleFailure failure)
    {
        return failure switch
        {
            SaleRuleFailure.NotFound => StatusCodes.Status404NotFound,
            SaleRuleFailure.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
    }

    private static int StatusFor(PurchaseRuleFailure failure)
    {
        return failure switch
        {
            PurchaseRuleFailure.NotFound => StatusCodes.Status404NotFound,
            PurchaseRuleFailure.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
    }
}
