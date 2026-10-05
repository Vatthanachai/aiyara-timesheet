using Aiyara.Timesheet.Component.Abstractions.Exceptions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;

namespace Aiyara.Timesheet.Component.Abstractions.Filters;
/// <summary>
/// JsonExceptionFilter
/// </summary>
/// <param name="environment"></param>
public class JsonExceptionFilter(IHostEnvironment environment) : IExceptionFilter
{
    /// <summary>
    /// Handle exception
    /// </summary>
    /// <param name="context"></param>
    public void OnException(ExceptionContext context)
    {
        var error = new ApiError();
        if (environment.IsDevelopment())
        {
            error.Message = context.Exception.Message;
            error.Detail = context.Exception.StackTrace;
        }
        else
        {
            error.Message = "A server error occurred.";
            error.Detail = context.Exception.StackTrace;
        }

        context.Result = new ObjectResult(error)
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
    }
}
