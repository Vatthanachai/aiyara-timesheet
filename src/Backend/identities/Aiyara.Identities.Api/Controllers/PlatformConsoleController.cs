using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.AspNetCore.Mvc;

namespace Aiyara.Identities.Api.Controllers;

[Route("api/v1/platform")]
public sealed class PlatformConsoleController(PlatformActorResolver actors,
    PlatformConsoleService console) : PlatformControllerBase(actors)
{
    [HttpGet("overview")]
    [ProducesResponseType(typeof(ServiceOkResponse<PlatformOverviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken)
    {
        if (await PlatformActorAsync(cancellationToken) is null) return UnauthorizedResponse();
        var result = await console.GetOverviewAsync(cancellationToken);
        return ReturnResponseWithHttpStatus(new ServiceOkResponse<PlatformOverviewResponse>(result));
    }

    [HttpGet("audit")]
    [ProducesResponseType(typeof(ServiceOkResponse<PlatformAuditListResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Audit(Guid? tenantId, string? actor, string? action,
        DateTime? fromUtc, DateTime? toUtc, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (await PlatformActorAsync(cancellationToken) is null) return UnauthorizedResponse();
        try
        {
            var result = await console.ListAuditAsync(new PlatformAuditQuery(tenantId, actor, action,
                fromUtc?.ToUniversalTime(), toUtc?.ToUniversalTime(), page, pageSize), cancellationToken);
            return ReturnResponseWithHttpStatus(new ServiceOkResponse<PlatformAuditListResponse>(result));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
    }
}
