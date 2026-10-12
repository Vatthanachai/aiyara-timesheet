using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Mvc;

namespace Aiyara.Timesheet.Component.Abstractions.Middlewares.Validations;

public static class ValidationResponseConfiguration
{
    public static void ConfigureSnakeCaseValidationResponse(this ApiBehaviorOptions options)
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => ToSnakeCase(kvp.Key),
                    kvp => kvp.Value!.Errors.Select(e =>
                        TransformErrorMessage(e.ErrorMessage, kvp.Key)).ToArray()
                );

            var problemDetails = new
            {
                errors,
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                title = "One or more validation errors occurred.",
                status = 400,
                trace_id = context.HttpContext.TraceIdentifier
            };

            return new BadRequestObjectResult(problemDetails);
        };
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Handle nested properties like "Products[0].Name" -> "products[0].name"
        var parts = input.Split('.');
        var snakeParts = parts.Select(part =>
        {
            // Handle array index like "Products[0]"
            var match = Regex.Match(part, @"^(.+?)(\[\d+\])$");
            if (match.Success)
            {
                return ConvertToSnakeCase(match.Groups[1].Value) + match.Groups[2].Value;
            }

            return ConvertToSnakeCase(part);
        });

        return string.Join(".", snakeParts);
    }

    private static string ConvertToSnakeCase(string input)
        => Regex.Replace(input, "([a-z0-9])([A-Z])", "$1_$2").ToLower();

    private static string TransformErrorMessage(string message, string originalKey)
    {
        // แปลงชื่อ field ใน error message ด้วย
        var snakeKey = ToSnakeCase(originalKey.Split('.').Last());
        var originalField = originalKey.Split('.').Last();

        // Handle array index
        var match = Regex.Match(originalField, @"^(.+?)\[\d+\]$");
        if (match.Success)
        {
            originalField = match.Groups[1].Value;
            snakeKey = Regex.Match(snakeKey, @"^(.+?)\[\d+\]$").Groups[1].Value;
        }

        return message.Replace(originalField, snakeKey);
    }
}