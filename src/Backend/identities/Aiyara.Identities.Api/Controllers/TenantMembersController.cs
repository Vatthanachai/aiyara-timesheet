using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.AspNetCore.Mvc;

namespace Aiyara.Identities.Api.Controllers;

/// <summary>A Tenant Admin's members and pending invitations, always in the token's own tenant.
/// Gateway exposes these under <c>/api/v1/identity/members</c>. New invitations are issued by
/// <c>POST /api/v1/auth/tenants/{tenantId}/invitations</c>.</summary>
[Route("api/v1/members")]
public sealed class TenantMembersController(TenantMemberService members,
    ICredentialNotificationSender notifications, IPasetoTokenService tokens) : BaseController
{
    /// <summary>The organization's directory, also used to name report subjects.</summary>
    [HttpGet(Name = "ListTenantMembersV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<IReadOnlyList<TenantMemberEntry>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> List(CancellationToken ct) => Run(async actor =>
        new ServiceOkResponse<IReadOnlyList<TenantMemberEntry>>(await members.ListMembersAsync(
            actor.TenantId, actor.UserId, actor.SessionVersion, ct)));

    [HttpGet("invitations", Name = "ListTenantInvitationsV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<IReadOnlyList<TenantInvitationEntry>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Invitations(CancellationToken ct) => Run(async actor =>
        new ServiceOkResponse<IReadOnlyList<TenantInvitationEntry>>(await members.ListInvitationsAsync(
            actor.TenantId, actor.UserId, actor.SessionVersion, ct)));

    /// <summary>Emails a new code for a pending invitation; the old code stops working.</summary>
    [HttpPost("invitations/{invitationId:guid}/resend", Name = "ResendTenantInvitationV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> ResendInvitation(Guid invitationId, CancellationToken ct) => Run(async actor =>
    {
        var (email, issued) = await members.ResendInvitationAsync(actor.TenantId, actor.UserId,
            actor.SessionVersion, invitationId, ct);
        try { await notifications.SendAsync(email, "invitation", issued.Code, ct); }
        catch (AuthenticationException exception) when
            (exception.Failure == AuthenticationFailure.Unavailable) { }
        // The code goes only to the invitee's email.
        return new ServiceOkResponse<object>(new { issued.InvitationId, issued.ExpiresAtUtc },
            StatusCodes.Status202Accepted);
    });

    [HttpDelete("invitations/{invitationId:guid}", Name = "RevokeTenantInvitationV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> RevokeInvitation(Guid invitationId, CancellationToken ct) => Run(async actor =>
    {
        await members.RevokeInvitationAsync(actor.TenantId, actor.UserId, actor.SessionVersion,
            invitationId, ct);
        return new ServiceOkResponse<object?>(null, StatusCodes.Status204NoContent);
    });

    [HttpPut("{accountId:guid}/role", Name = "ChangeTenantMemberRoleV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> ChangeRole(Guid accountId, [FromBody] ChangeTenantMemberRoleRequest request,
        CancellationToken ct) => Run(async actor =>
    {
        await members.ChangeRoleAsync(actor.TenantId, actor.UserId, actor.SessionVersion, accountId,
            request, ct);
        return new ServiceOkResponse<object?>(null, StatusCodes.Status204NoContent);
    });

    /// <summary>Deactivates or reactivates a member. A reactivated member who never set a
    /// password is sent a new activation email.</summary>
    [HttpPut("{accountId:guid}/status", Name = "SetTenantMemberStatusV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> SetStatus(Guid accountId, [FromBody] SetTenantMemberStatusRequest request,
        CancellationToken ct) => Run(async actor =>
    {
        await members.SetStatusAsync(actor.TenantId, actor.UserId, actor.SessionVersion, accountId,
            request, ct);
        return new ServiceOkResponse<object?>(null, StatusCodes.Status204NoContent);
    });

    [HttpPost("{accountId:guid}/activation", Name = "ResendTenantMemberActivationV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> ResendActivation(Guid accountId, CancellationToken ct) => Run(async actor =>
    {
        await members.ResendActivationAsync(actor.TenantId, actor.UserId, actor.SessionVersion,
            accountId, ct);
        return new ServiceOkResponse<object?>(null, StatusCodes.Status202Accepted);
    });

    /// <summary>Runs the action for a TenantAdmin token; the service then re-checks the account,
    /// membership, session version, and password age in the database.</summary>
    private async Task<IActionResult> Run(Func<PasetoTokenClaims, Task<ServiceResponse>> action)
    {
        Response.Headers.CacheControl = "no-store";
        var authorization = Request.Headers.Authorization.ToString();
        var claims = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? tokens.ValidateToken(authorization[7..].Trim()) : null;
        if (claims is null || claims.MustChangePassword)
            return Error(StatusCodes.Status401Unauthorized, "UNAUTHORIZED");
        if (claims.Role != "TenantAdmin") return Error(StatusCodes.Status403Forbidden, "FORBIDDEN");
        try { return ReturnResponseWithHttpStatus(await action(claims)); }
        catch (TenantMemberException exception)
        {
            return exception.Failure switch
            {
                TenantMemberFailure.Unauthorized => Error(StatusCodes.Status401Unauthorized, "UNAUTHORIZED"),
                TenantMemberFailure.NotFound => Error(StatusCodes.Status404NotFound, "NOT_FOUND"),
                TenantMemberFailure.Conflict => Error(StatusCodes.Status409Conflict, "CONFLICT"),
                TenantMemberFailure.LastAdministrator => Error(StatusCodes.Status409Conflict, "LAST_ADMINISTRATOR"),
                TenantMemberFailure.SelfChange => Error(StatusCodes.Status409Conflict, "CANNOT_CHANGE_SELF"),
                TenantMemberFailure.RateLimited => Error(StatusCodes.Status429TooManyRequests, "RATE_LIMITED"),
                TenantMemberFailure.Unavailable => Error(StatusCodes.Status503ServiceUnavailable, "ACTIVATION_DELIVERY_UNAVAILABLE"),
                _ => Error(StatusCodes.Status400BadRequest, "VALIDATION_FAILED")
            };
        }
    }

    private IActionResult Error(int status, string code) => ReturnResponseWithHttpStatus(
        new ServiceErrorResponse(status, code, "The request could not be completed."));
}
