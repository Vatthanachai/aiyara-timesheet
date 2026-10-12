using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.AspNetCore.Mvc;

namespace Aiyara.Identities.Api.Controllers;

/// <summary>Platform security settings: the platform tenant's password policy.</summary>
[Route("api/v1/platform/security")]
public sealed class PlatformSecurityController(PlatformActorResolver actors,
    PlatformSecurityService security) : PlatformControllerBase(actors)
{
    [HttpGet("password-policy")]
    [ProducesResponseType(typeof(ServiceOkResponse<PlatformPasswordPolicy>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPasswordPolicy(CancellationToken cancellationToken)
    {
        if (await PlatformActorAsync(cancellationToken) is null) return UnauthorizedResponse();
        var result = await security.GetPasswordPolicyAsync(cancellationToken);
        return ReturnResponseWithHttpStatus(new ServiceOkResponse<PlatformPasswordPolicy>(result));
    }

    [HttpPut("password-policy")]
    [ProducesResponseType(typeof(ServiceOkResponse<PlatformPasswordPolicy>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UpdatePasswordPolicy([FromBody] UpdatePlatformPasswordPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await PlatformActorAsync(cancellationToken);
        if (actor is null) return UnauthorizedResponse();
        try
        {
            await security.UpdatePasswordPolicyAsync(actor.AccountId, actor.SessionVersion, request, cancellationToken);
            var result = await security.GetPasswordPolicyAsync(cancellationToken);
            return ReturnResponseWithHttpStatus(new ServiceOkResponse<PlatformPasswordPolicy>(result));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
        catch (AuthenticationException exception) when (exception.Failure == AuthenticationFailure.Unavailable)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "SESSION_REVOCATION_UNAVAILABLE");
        }
    }
}
