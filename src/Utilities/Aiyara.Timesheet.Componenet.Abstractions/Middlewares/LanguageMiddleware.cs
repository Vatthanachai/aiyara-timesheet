using Microsoft.AspNetCore.Http;

namespace Aiyara.Timesheet.Component.Abstractions.Middlewares;

/// <summary>
/// Language Middleware
/// </summary>
/// <param name="next"></param>
public class LanguageMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Language Middleware
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var acceptLanguage = context.Request.Headers["Accept-Language"].ToString();
        var languageCode = acceptLanguage.Split(',').FirstOrDefault()?.Split('-')[0];

        // Define supported languages
        var supportedLanguages = new[] { "en", "th", "fr" };
        if (!supportedLanguages.Contains(languageCode))
        {
            languageCode = "en"; // Default to English
        }

        // Store the language in HttpContext for later use
        context.Items["Language"] = languageCode;

        await next(context);
    }
}