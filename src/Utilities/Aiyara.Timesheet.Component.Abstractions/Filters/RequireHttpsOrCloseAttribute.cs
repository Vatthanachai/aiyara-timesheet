using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Aiyara.Timesheet.Component.Abstractions.Filters;

/// <summary>
/// RequireHttpsOrCloseAttribute
/// </summary>
public class RequireHttpsOrCloseAttribute : RequireHttpsAttribute
{
    /// <summary>
    /// Handle non https request
    /// </summary>
    /// <param name="filterContext"></param>
    protected override void HandleNonHttpsRequest(AuthorizationFilterContext filterContext)
        => filterContext.Result = new StatusCodeResult(StatusCodes.Status400BadRequest);
}