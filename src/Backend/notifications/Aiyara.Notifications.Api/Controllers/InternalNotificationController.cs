using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Microsoft.AspNetCore.Mvc;

[Route("internal/v1")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalNotificationController(
    CredentialEmailDispatcher credentials,
    ReportReadyEmailDispatcher reports,
    IConfiguration configuration) : BaseController
{
    [HttpPost("credential-email")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CredentialEmail([FromBody] CredentialEmailRequest request,
        CancellationToken cancellationToken)
    {
        if (!HasInternalKey()) return ReturnResponseWithHttpStatus(
            new ServiceErrorResponse(401, "UNAUTHORIZED", "Authentication is required."));
        return ReturnResponseWithHttpStatus(await credentials.SendAsync(request, cancellationToken)
            ? new ServiceOkResponse<object?>(null, 202)
            : new ServiceErrorResponse(503, "DELIVERY_UNAVAILABLE", "Delivery is unavailable."));
    }

    [HttpPost("report-ready")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ReportReady([FromBody] ReportReadyEmailRequest request,
        CancellationToken cancellationToken)
    {
        if (!HasInternalKey()) return ReturnResponseWithHttpStatus(
            new ServiceErrorResponse(401, "UNAUTHORIZED", "Authentication is required."));
        return ReturnResponseWithHttpStatus(await reports.SendAsync(request, cancellationToken)
            ? new ServiceOkResponse<object?>(null, 202)
            : new ServiceErrorResponse(503, "DELIVERY_UNAVAILABLE", "Delivery is unavailable."));
    }

    private bool HasInternalKey() => CredentialEmailDispatcher.ValidInternalKey(
        configuration["InternalApi:Key"], Request.Headers["X-Internal-Key"].ToString());
}
