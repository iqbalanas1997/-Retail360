using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Retail360.Api.Catalog;
using Retail360.Domain.Catalog;
using Retail360.Domain.Inventory;
using Retail360.Domain.Purchasing;
using Retail360.Domain.Sales;
using Retail360.Domain.Suppliers;
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
        var middleware = new CatalogExceptionMiddleware(
            _ => throw Create(exceptionType, failureName, message),
            NullLogger<CatalogExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal(message, document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Supplier_conflict_uses_the_error_body()
    {
        var middleware = new CatalogExceptionMiddleware(
            _ => throw new SupplierRuleException(SupplierRuleFailure.Conflict, "A supplier with this name already exists."),
            NullLogger<CatalogExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal("A supplier with this name already exists.", document.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(typeof(SaleRuleException), nameof(SaleRuleFailure.Conflict), StatusCodes.Status409Conflict, "Product is inactive.")]
    [InlineData(typeof(PurchaseRuleException), nameof(PurchaseRuleFailure.Invalid), StatusCodes.Status400BadRequest, "A purchase needs at least one line.")]
    public async Task Sale_and_purchase_rules_use_the_same_error_body(
        Type exceptionType,
        string failureName,
        int statusCode,
        string message)
    {
        var middleware = new CatalogExceptionMiddleware(
            _ => throw Create(exceptionType, failureName, message),
            NullLogger<CatalogExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal(message, document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Unexpected_exception_returns_a_generic_error_and_keeps_the_detail_in_the_log()
    {
        var logger = new CollectingLogger();
        var middleware = new CatalogExceptionMiddleware(
            _ => throw new InvalidOperationException("Host=localhost;Password=secret"),
            logger);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "trace-1";

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains(CatalogExceptionMiddleware.UnexpectedMessage, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Error, logger.Level);
        Assert.Contains("trace-1", logger.Text, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(logger.LoggedException);
    }

    private static Exception Create(Type exceptionType, string failureName, string message)
    {
        if (exceptionType == typeof(CatalogRuleException))
        {
            var failure = Enum.Parse<CatalogRuleFailure>(failureName);
            return new CatalogRuleException(failure, message);
        }

        if (exceptionType == typeof(SaleRuleException))
        {
            var failure = Enum.Parse<SaleRuleFailure>(failureName);
            return new SaleRuleException(failure, message);
        }

        if (exceptionType == typeof(PurchaseRuleException))
        {
            var failure = Enum.Parse<PurchaseRuleFailure>(failureName);
            return new PurchaseRuleException(failure, message);
        }

        var inventoryFailure = Enum.Parse<InventoryRuleFailure>(failureName);
        return new InventoryRuleException(inventoryFailure, message);
    }

    private sealed class CollectingLogger : ILogger<CatalogExceptionMiddleware>
    {
        public LogLevel Level { get; private set; }

        public string Text { get; private set; } = string.Empty;

        public Exception? LoggedException { get; private set; }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Level = logLevel;
            LoggedException = exception;
            Text = formatter(state, exception);
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
