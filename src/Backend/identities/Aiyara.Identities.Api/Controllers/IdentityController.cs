using Aiyara.Identities.Databases;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Api.Controllers;

[Route("api/v1")]
public sealed class IdentityController(
    OnboardingService onboarding,
    AuthenticationService authentication,
    IdentityDbContext db,
    ICredentialNotificationSender notifications,
    IPasetoTokenService tokens) : BaseController
{
    [HttpPost("tenants", Name = "CreateTenantV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<CreateTenantResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request, CancellationToken ct) =>
        await Onboarding(async () =>
        {
            var result = await onboarding.CreateTenantAsync(request, ct);
            await TrySendActivation(result.TenantId, request.AdminEmail, ct);
            Response.Headers.CacheControl = "no-store";
            return new ServiceOkResponse<CreateTenantResponse>(result, 201);
        });

    [HttpPost("invitations/accept", Name = "AcceptInvitationV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<AcceptInvitationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInvitationRequest request, CancellationToken ct) =>
        await Onboarding(async () =>
        {
            var result = await onboarding.AcceptInvitationAsync(request, ct);
            var email = await db.Accounts.Where(x => x.Id == result.AccountId)
                .Select(x => x.Email).SingleAsync(ct);
            await TrySendActivation(result.TenantId, email, ct);
            return new ServiceOkResponse<AcceptInvitationResponse>(result);
        });

    [HttpPost("tenants/{tenantId:guid}/invitations", Name = "IssueInvitationV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<IssueInvitationResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IssueInvitation(Guid tenantId, [FromBody] IssueInvitationRequest request,
        CancellationToken ct) => await Onboarding(async () =>
    {
        var result = await onboarding.IssueInvitationWithKeyAsync(tenantId,
            Request.Headers["X-Onboarding-Key"].ToString(), request, ct);
        await TrySendInvitation(request.Email, result.Code, ct);
        Response.Headers.CacheControl = "no-store";
        return new ServiceOkResponse<IssueInvitationResponse>(result, 201);
    });

    [HttpPost("auth/activation/request")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> RequestActivation([FromBody] RequestCredentialEmail request, CancellationToken ct) =>
        Auth(async () => { await authentication.RequestActivationAsync(request, ct); return AcceptedResponse(); });

    [HttpPost("auth/activate")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status429TooManyRequests)]
    public Task<IActionResult> Activate([FromBody] CompleteCredentialChallenge request, CancellationToken ct) =>
        Auth(async () => { await authentication.CompleteActivationAsync(request, ct); return NoContentResponse(); });

    [HttpPost("auth/login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ServiceOkResponse<AuthenticationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status429TooManyRequests)]
    public Task<IActionResult> Login([FromBody] OrganizationLoginRequest request, CancellationToken ct) =>
        Auth(async () => { var result = await authentication.LoginAsync(new LoginRequest
            {
                OrganizationCode = request.OrganizationCode, Email = request.Email,
                Password = request.Password
            }, ct);
            Response.Headers.CacheControl = "no-store"; return new ServiceOkResponse<object>(result); });

    [HttpPost("auth/sign-in")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ServiceOkResponse<SignInResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status429TooManyRequests)]
    public Task<IActionResult> SignIn([FromBody] SignInRequest request, CancellationToken ct) =>
        Auth(async () => { var result = await authentication.SignInAsync(request, ct);
            Response.Headers.CacheControl = "no-store"; return new ServiceOkResponse<object>(result); });

    [HttpPost("auth/platform-admin/login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ServiceOkResponse<PlatformAdminLoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status429TooManyRequests)]
    public Task<IActionResult> LoginPlatformAdmin([FromBody] PlatformAdminLoginRequest request,
        CancellationToken ct) =>
        Auth(async () => { var result = await authentication.LoginPlatformAdminAsync(request, ct);
            Response.Headers.CacheControl = "no-store"; return new ServiceOkResponse<object>(result); });

    [HttpPost("auth/refresh")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ServiceOkResponse<AuthenticationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct) =>
        Auth(async () => { var result = await authentication.RefreshAsync(request, ct);
            Response.Headers.CacheControl = "no-store"; return new ServiceOkResponse<object>(result); });

    [HttpPost("auth/logout")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct) =>
        Auth(async () => { await authentication.LogoutAsync(request, ct); return NoContentResponse(); });

    [HttpPost("auth/password/forgot")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> ForgotPassword([FromBody] RequestCredentialEmail request, CancellationToken ct) =>
        Auth(async () => { await authentication.RequestPasswordResetAsync(request, ct); return AcceptedResponse(); });

    [HttpPost("auth/password/reset")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status429TooManyRequests)]
    public Task<IActionResult> ResetPassword([FromBody] CompleteCredentialChallenge request, CancellationToken ct) =>
        Auth(async () => { await authentication.CompletePasswordResetAsync(request, ct); return NoContentResponse(); });

    [HttpPost("auth/password/change")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct) =>
        Auth(async () =>
        {
            var claims = ReadClaims();
            if (claims is null) return UnauthorizedResponse();
            await authentication.ChangePasswordAsync(claims.TenantId, claims.UserId,
                claims.SessionVersion, request, ct);
            return NoContentResponse();
        });

    [HttpPut("auth/tenants/{tenantId:guid}/password-policy")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> UpdatePasswordPolicy(Guid tenantId,
        [FromBody] TenantPasswordPolicyRequest request, CancellationToken ct) => Auth(async () =>
    {
        var claims = ReadClaims();
        if (claims is null || claims.TenantId != tenantId || claims.Role != "TenantAdmin" ||
            claims.MustChangePassword) return UnauthorizedResponse();
        await authentication.UpdateTenantPolicyAsync(tenantId, claims.UserId,
            claims.SessionVersion, request, ct);
        return NoContentResponse();
    });

    [HttpPost("auth/tenants/{tenantId:guid}/invitations")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ServiceOkResponse<IssueInvitationResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IssueAuthorizedInvitation(Guid tenantId,
        [FromBody] IssueInvitationRequest request, CancellationToken ct)
    {
        var claims = ReadClaims();
        if (claims is null || claims.TenantId != tenantId || claims.Role != "TenantAdmin" ||
            claims.MustChangePassword) return ReturnResponseWithHttpStatus(UnauthorizedResponse());
        return await Onboarding(async () =>
        {
            var result = await onboarding.IssueInvitationAuthorizedAsync(tenantId,
                claims.UserId, claims.SessionVersion, request, ct);
            await TrySendInvitation(request.Email, result.Code, ct);
            Response.Headers.CacheControl = "no-store";
            return new ServiceOkResponse<IssueInvitationResponse>(result, 201);
        });
    }

    private async Task<IActionResult> Auth(Func<Task<ServiceResponse>> action)
    {
        try { return ReturnResponseWithHttpStatus(await action()); }
        catch (AuthenticationException exception)
        {
            var status = exception.Failure switch
            {
                AuthenticationFailure.InvalidCredentials => 401,
                AuthenticationFailure.MfaRequired or AuthenticationFailure.MfaInvalid => 401,
                AuthenticationFailure.Conflict => 409,
                AuthenticationFailure.RateLimited => 429,
                AuthenticationFailure.Unavailable => 503,
                _ => 400
            };
            return ReturnResponseWithHttpStatus(new ServiceErrorResponse(status,
                exception.Failure.ToString().ToUpperInvariant(), "The request could not be completed."));
        }
    }

    private async Task<IActionResult> Onboarding(Func<Task<ServiceResponse>> action)
    {
        try { return ReturnResponseWithHttpStatus(await action()); }
        catch (OnboardingException exception)
        {
            var status = exception.Failure switch
            {
                OnboardingFailure.Unauthorized => 401,
                OnboardingFailure.Conflict => 409,
                _ => 400
            };
            return ReturnResponseWithHttpStatus(new ServiceErrorResponse(status,
                exception.Failure.ToString().ToUpperInvariant(), "The request could not be completed."));
        }
    }

    private PasetoTokenClaims? ReadClaims()
    {
        var authorization = Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? tokens.ValidateToken(authorization[7..].Trim()) : null;
    }

    private static ServiceResponse AcceptedResponse() => new ServiceOkResponse<object?>(null, 202);
    private static ServiceResponse NoContentResponse() => new ServiceOkResponse<object?>(null, 204);
    private static ServiceResponse UnauthorizedResponse() =>
        new ServiceErrorResponse(401, "UNAUTHORIZED", "Authentication is required.");

    private async Task TrySendInvitation(string email, string code, CancellationToken ct)
    {
        try { await notifications.SendAsync(email, "invitation", code, ct); }
        catch (AuthenticationException exception) when
            (exception.Failure == AuthenticationFailure.Unavailable) { }
    }

    private async Task TrySendActivation(Guid tenantId, string email, CancellationToken ct)
    {
        try { await authentication.RequestActivationAsync(new RequestCredentialEmail(tenantId, email), ct); }
        catch (AuthenticationException exception) when
            (exception.Failure is AuthenticationFailure.Unavailable or AuthenticationFailure.RateLimited) { }
    }
}
