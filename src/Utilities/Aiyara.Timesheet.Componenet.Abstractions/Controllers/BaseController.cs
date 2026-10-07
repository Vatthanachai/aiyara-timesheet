using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aiyara.Timesheet.Component.Abstractions.Controllers;

[ApiController]
public class BaseController : ControllerBase
{
    protected IActionResult ReturnResponseWithHttpStatus(ServiceResponse response)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));

        if (response is ServiceFileResponse file)
            return File(file.Content, file.ContentType, file.FileName);

        if (response.Location is not null) Response.Headers.Location = response.Location;

        if (response.StatusCode == StatusCodes.Status204NoContent)
            return NoContent();

        if (response.StatusCode is < 200 or > 599)
            throw new ArgumentOutOfRangeException(nameof(response));

        return StatusCode(response.StatusCode, response);
    }
}
