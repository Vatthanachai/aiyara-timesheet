using System.Security.Claims;

using Microsoft.AspNetCore.Http;

namespace Aiyara.Timesheet.Component.Abstractions.Extensions;

public static class CurrentUserExtension
{
    /// <summary>
    /// Gets the UserId claim of the currently authenticated request, or null when unauthenticated.
    /// </summary>
    public static int? GetCurrentUserId(this IHttpContextAccessor httpContextAccessor)
    {
        var value = httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, out var userId) ? userId : null;
    }

    /// <summary>
    /// Gets the Role claim of the currently authenticated request, or null when unauthenticated.
    /// </summary>
    public static string? GetCurrentRole(this IHttpContextAccessor httpContextAccessor)
        => httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)?.Value;
}
