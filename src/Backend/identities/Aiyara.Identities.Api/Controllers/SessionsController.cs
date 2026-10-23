using Aiyara.Identities.Services.Authentication;
using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Microsoft.AspNetCore.Mvc;

namespace Aiyara.Identities.Api.Controllers;

/// <summary>The signed-in account's own active sessions: list them, end one, end all but the current
/// one, or sign out everywhere. Gateway exposes these under <c>/api/v1/identity/sessions</c>. Every
/// route acts on the token's account in the token's organization only, so another account's session
/// id is <c>404 NOT_FOUND</c>. The current session is the one whose id the access token carries;
/// a token issued before session management has none and marks nothing as current.</summary>
[Route("api/v1/sessions")]
public sealed class SessionsController(AuthenticationService authentication, IPasetoTokenService tokens)
    : BaseController
{
    [HttpGet(Name = "ListMySessionsV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<IReadOnlyList<MemberSession>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> List(CancellationToken ct) => Run(async claims =>
        new ServiceOkResponse<IReadOnlyList<MemberSession>>(await authentication.ListSessionsAsync(
            claims.TenantId, claims.UserId, claims.SessionVersion, claims.SessionId, ct)));

    /// <summary>Ends another session. 409 <c>CURRENT_SESSION</c> for the session of this very token.</summary>
    [HttpDelete("{sessionId:guid}", Name = "RevokeMySessionV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Revoke(Guid sessionId, CancellationToken ct) => Run(async claims =>
    {
        await authentication.RevokeSessionAsync(claims.TenantId, claims.UserId, claims.SessionVersion,
            claims.SessionId, sessionId, ct);
        return new ServiceOkResponse<object?>(null, StatusCodes.Status204NoContent);
    });

    /// <summary>Ends every session but the current one. 409 <c>SESSION_UNKNOWN</c> when the token does not
    /// identify an active session (sign in again).</summary>
    [HttpPost("revoke-others", Name = "RevokeMyOtherSessionsV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> RevokeOthers(CancellationToken ct) => Run(async claims =>
    {
        await authentication.RevokeOtherSessionsAsync(claims.TenantId, claims.UserId, claims.SessionVersion,
            claims.SessionId, ct);
        return new ServiceOkResponse<object?>(null, StatusCodes.Status204NoContent);
    });

    /// <summary>Signs out everywhere, this session included, in every organization the account belongs to.
    /// The caller's own access token stops working at once.</summary>
    [HttpPost("revoke-all", Name = "RevokeAllMySessionsV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> RevokeAll(CancellationToken ct) => Run(async claims =>
    {
        await authentication.RevokeAllSessionsAsync(claims.TenantId, claims.UserId, claims.SessionVersion, ct);
        return new ServiceOkResponse<object?>(null, StatusCodes.Status204NoContent);
    });

    private async Task<IActionResult> Run(Func<PasetoTokenClaims, Task<ServiceResponse>> action)
    {
        Response.Headers.CacheControl = "no-store";
        var authorization = Request.Headers.Authorization.ToString();
        var claims = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? tokens.ValidateToken(authorization[7..].Trim()) : null;
        if (claims is null || claims.MustChangePassword)
            return ReturnResponseWithHttpStatus(
                new ServiceErrorResponse(401, "UNAUTHORIZED", "Authentication is required."));
        try { return ReturnResponseWithHttpStatus(await action(claims)); }
        catch (AuthenticationException exception)
        {
            return ReturnResponseWithHttpStatus(IdentityController.AuthError(exception));
        }
    }
}
