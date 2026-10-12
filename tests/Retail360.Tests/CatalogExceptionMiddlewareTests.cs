using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Retail360.Api.Catalog;
using Retail360.Domain.Catalog;
using Retail360.Domain.Inventory;
using Xunit;

namespace Retail360.Tests;

public class CatalogExceptionMiddlewareTests
{
    [Theory]
    [InlineData(typeof(CatalogRuleException), nameof(CatalogRuleFailure.NotFound), StatusCodes.Status404NotFound, "Product was not found.")]
    [InlineData(typeof(CatalogRuleException), nameof(CatalogRuleFailure.Conflict), StatusCodes.Status409Conflict, "Barcode is already assigned to a product.")]
    [InlineData(typeof(CatalogRuleException), nameof(CatalogRuleFailure.Invalid), StatusCodes.Status400BadRequest, "Name is required.")]
    [InlineData(typeof(InventoryRuleException), nameof(InventoryRuleFailure.NotFound), StatusCodes.Status404NotFound, "Area was not found.")]
    [InlineData(typeof(InventoryRuleException), nameof(InventoryRuleFailure.Conflict), StatusCodes.Status409Conflict, "Shelf does not have enough stock.")]
    [InlineData(typeof(InventoryRuleException), nameof(InventoryRuleFailure.Invalid), StatusCodes.Status400BadRequest, "Operation id is required.")]
    public async Task Rule_failures_use_the_contract_status_and_error_body(
        Type exceptionType,
        string failureName,
        int statusCode,
        string message)
    {
        var middleware = new CatalogExceptionMiddleware(_ => throw Create(exceptionType, failureName, message));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal(message, document.RootElement.GetProperty("error").GetString());
    }

    private static Exception Create(Type exceptionType, string failureName, string message)
    {
        if (exceptionType == typeof(CatalogRuleException))
        {
            var failure = Enum.Parse<CatalogRuleFailure>(failureName);
            return new CatalogRuleException(failure, message);
        }

        var inventoryFailure = Enum.Parse<InventoryRuleFailure>(failureName);
        return new InventoryRuleException(inventoryFailure, message);
    }
}
