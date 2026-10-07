using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiyara.Timesheet.Component.Abstractions.Controllers;

public static class SafeApiErrors
{
    public static async Task WriteErrorAsync(HttpContext context, int statusCode,
        string errorCode, CancellationToken cancellationToken = default)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new ServiceErrorResponse(statusCode, errorCode,
            "The request could not be completed.", traceId: context.TraceIdentifier),
            cancellationToken);
    }

    public static IServiceCollection AddSafeApiErrors(this IServiceCollection services)
    {
        services.AddExceptionHandler<SafeExceptionHandler>();
        services.AddProblemDetails();
        services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(entry => entry.Key,
                    entry => entry.Value!.Errors.Select(_ => "INVALID_VALUE").ToArray());
            return new ObjectResult(new ServiceErrorResponse(400, "VALIDATION_FAILED",
                "The request contains invalid fields.", errors, context.HttpContext.TraceIdentifier))
            { StatusCode = 400 };
        });
        return services;
    }

    private sealed class SafeExceptionHandler(ILogger<SafeExceptionHandler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
            CancellationToken cancellationToken)
        {
            logger.LogError(exception, "Unhandled API error; trace {TraceId}", context.TraceIdentifier);
            if (context.Response.HasStarted) return false;
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new ServiceErrorResponse(500,
                "INTERNAL_ERROR", "An unexpected error occurred.", traceId: context.TraceIdentifier),
                cancellationToken);
            return true;
        }
    }
}
