using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Aiyara.Timesheet.Component.Abstractions.Exceptions;

/// <summary>
/// Http response exception filter.
/// </summary>
public class HttpResponseExceptionFilter : IActionFilter, IOrderedFilter
{
    /// <inheritdoc/>
    public int Order { get; }

    /// <inheritdoc/>
    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is HttpResponseException exception)
        {
            context.Result = new ObjectResult(exception.Value) { StatusCode = exception.Status, };
            context.ExceptionHandled = true;
        }
    }

    /// <inheritdoc/>
    public void OnActionExecuting(ActionExecutingContext context)
    {
    }
}