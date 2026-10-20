using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Retail360.Api;

public static class ShopModelStateResponse
{
    public const string InvalidBody = "The request body is invalid.";

    public const string MissingBody = "Request body is required.";

    public const string InvalidRequest = "The request is invalid.";

    public static IActionResult Create(ActionContext context)
    {
        return new BadRequestObjectResult(new { error = Message(context.ModelState) });
    }

    public static string Message(ModelStateDictionary modelState)
    {
        foreach (var entry in modelState)
        {
            foreach (var error in entry.Value?.Errors ?? Enumerable.Empty<ModelError>())
            {
                if (error.Exception is JsonException or FormatException)
                {
                    return InvalidBody;
                }

                var text = error.ErrorMessage ?? string.Empty;
                if (text.Contains("non-empty request body", StringComparison.OrdinalIgnoreCase))
                {
                    return MissingBody;
                }

                if (text.Contains("could not be converted", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("JSON", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Path:", StringComparison.Ordinal)
                    || text.Contains("LineNumber", StringComparison.Ordinal)
                    || text.Contains("System.", StringComparison.Ordinal)
                    || text.Contains("Exception", StringComparison.Ordinal))
                {
                    return InvalidBody;
                }

                if (!string.IsNullOrWhiteSpace(text) && text.Length <= 200 && !text.Contains('\n'))
                {
                    return text;
                }
            }
        }

        return InvalidRequest;
    }
}
