using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.AspNetCore.Mvc;

namespace Aiyara.Identities.Api.Controllers;

/// <summary>The Platform Admin roster: list, invite, deactivate, and reactivate Platform Admins.</summary>
[Route("api/v1/platform/admins")]
public sealed class PlatformAdminsController(PlatformActorResolver actors,
    PlatformAdminRosterService roster) : PlatformControllerBase(actors)
{
    [HttpGet]
    [ProducesResponseType(typeof(ServiceOkResponse<IReadOnlyList<PlatformAdminRosterEntry>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var actor = await PlatformActorAsync(cancellationToken);
        if (actor is null) return UnauthorizedResponse();
        var result = await roster.ListAsync(actor.AccountId, cancellationToken);
        return ReturnResponseWithHttpStatus(new ServiceOkResponse<IReadOnlyList<PlatformAdminRosterEntry>>(result));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invite([FromBody] InvitePlatformAdminRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var actor = await PlatformActorAsync(cancellationToken);
        if (actor is null) return UnauthorizedResponse();
        try
        {
            await roster.InviteAsync(actor.AccountId, actor.SessionVersion, request, cancellationToken);
            return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(new
            {
                deliveryStatus = "accepted"
            }, StatusCodes.Status202Accepted));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
    }

    [HttpPut("status")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SetStatus([FromBody] SetPlatformAdminStatusRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await PlatformActorAsync(cancellationToken);
        if (actor is null) return UnauthorizedResponse();
        try
        {
            await roster.SetStatusAsync(actor.AccountId, actor.SessionVersion, request, cancellationToken);
            return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(new { }));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
        catch (AuthenticationException exception) when (exception.Failure == AuthenticationFailure.Unavailable)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "SESSION_REVOCATION_UNAVAILABLE");
        }
    }
}
