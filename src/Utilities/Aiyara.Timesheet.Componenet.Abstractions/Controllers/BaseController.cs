using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;

using Microsoft.AspNetCore.Mvc;

namespace Aiyara.Timesheet.Component.Abstractions.Controllers;

[ApiController]
public class BaseController : ControllerBase
{
    protected IActionResult ReturnResponseWithHttpStatus(ServiceResponse response)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));

        var responseType = response.GetType();

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(ServiceOkResponse<>))
        {
            return Ok(response);
        }

        return response switch
        {
            ServiceBadRequestResponse => BadRequest(response),
            _ => throw new ArgumentOutOfRangeException(nameof(response))
        };
    }
}