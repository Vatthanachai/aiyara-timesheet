using Aiyara.Identities.Services.Authentication;
using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Microsoft.AspNetCore.Mvc;

namespace Aiyara.Identities.Api.Controllers;

/// <summary>A signed-in member's own second factor: read its state, opt in, regenerate recovery codes,
/// and opt out. Gateway exposes these under <c>/api/v1/identity/mfa</c>. Platform Admins have their own
/// flow and get 401 here. Codes are checked by the same TOTP and recovery-code rules as sign-in, and
/// wrong codes count toward the same lockout (429 <c>RATELIMITED</c> while locked).</summary>
[Route("api/v1/mfa")]
public sealed class MemberMfaController(AuthenticationService authentication, IPasetoTokenService tokens)
    : BaseController
{
    [HttpGet(Name = "GetMyMfaV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<MemberMfaStatus>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Status(CancellationToken ct) => Run(async claims =>
        new ServiceOkResponse<MemberMfaStatus>(await authentication.GetMemberMfaStatusAsync(
            claims.TenantId, claims.UserId, claims.SessionVersion, ct)));

    /// <summary>Starts an opt-in enrollment and returns the secret to add to an authenticator app.</summary>
    [HttpPost("enrollment", Name = "BeginMyMfaEnrollmentV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<MfaEnrollment>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> BeginEnrollment(CancellationToken ct) => Run(async claims =>
        new ServiceOkResponse<MfaEnrollment>(await authentication.BeginMemberMfaEnrollmentAsync(
            claims.TenantId, claims.UserId, claims.SessionVersion, ct)));

    /// <summary>Confirms the enrollment with the first code; the response lists the recovery codes, once.</summary>
    [HttpPost("enrollment/confirm", Name = "ConfirmMyMfaEnrollmentV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<MfaRecoveryCodesResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> ConfirmEnrollment([FromBody] MfaCodeRequest request, CancellationToken ct) =>
        Run(async claims => new ServiceOkResponse<MfaRecoveryCodesResponse>(
            await authentication.ConfirmMemberMfaEnrollmentAsync(claims.TenantId, claims.UserId,
                claims.SessionVersion, request, ct)));

    /// <summary>Replaces all recovery codes. Needs a current authenticator code (<c>totpCode</c>).</summary>
    [HttpPost("recovery-codes", Name = "RegenerateMyRecoveryCodesV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<MfaRecoveryCodesResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> RegenerateRecoveryCodes([FromBody] MfaCodeRequest request, CancellationToken ct) =>
        Run(async claims => new ServiceOkResponse<MfaRecoveryCodesResponse>(
            await authentication.RegenerateMemberRecoveryCodesAsync(claims.TenantId, claims.UserId,
                claims.SessionVersion, request, ct)));

    /// <summary>Turns MFA off after a <c>totpCode</c> or <c>recoveryCode</c>. 409
    /// <c>MFA_REQUIRED_BY_ORGANIZATION</c> while any of the member's organizations requires it.</summary>
    [HttpPost("disable", Name = "DisableMyMfaV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> Disable([FromBody] MfaCodeRequest request, CancellationToken ct) =>
        Run(async claims =>
        {
            await authentication.DisableMemberMfaAsync(claims.TenantId, claims.UserId, claims.SessionVersion,
                request, ct);
            return new ServiceOkResponse<object?>(null, StatusCodes.Status204NoContent);
        });

    private async Task<IActionResult> Run(Func<PasetoTokenClaims, Task<ServiceResponse>> action)
    {
        Response.Headers.CacheControl = "no-store";
        var authorization = Request.Headers.Authorization.ToString();
        var claims = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? tokens.ValidateToken(authorization[7..].Trim()) : null;
        if (claims is null || claims.MustChangePassword || claims.Role == "PlatformAdmin")
            return ReturnResponseWithHttpStatus(
                new ServiceErrorResponse(401, "UNAUTHORIZED", "Authentication is required."));
        try { return ReturnResponseWithHttpStatus(await action(claims)); }
        catch (AuthenticationException exception)
        {
            return ReturnResponseWithHttpStatus(IdentityController.AuthError(exception));
        }
    }
}
