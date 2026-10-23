using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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

    /// <summary>The organization's member-management history, newest first, <c>member.*</c>
    /// actions only. Optional filters: exact action code, fromUtc, toUtc. Page size is 1-100.</summary>
    [HttpGet("audit", Name = "ListTenantAuditV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<TenantAuditListResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Audit(string? action, DateTime? fromUtc, DateTime? toUtc, int page = 1,
        int pageSize = 25, CancellationToken ct = default) => Run(async actor =>
        new ServiceOkResponse<TenantAuditListResponse>(await members.ListAuditAsync(actor.TenantId,
            actor.UserId, actor.SessionVersion, action, fromUtc?.ToUniversalTime(),
            toUtc?.ToUniversalTime(), page, pageSize, ct)));

    /// <summary>Invites up to 100 people at once. Rows are judged one by one and the response lists
    /// each as Created or Skipped with a reason, so a partly bad batch still invites the good rows.
    /// More than 100 rows, or none, is 400 <c>VALIDATION_FAILED</c>. Gateway exposes it as
    /// <c>/api/v1/identity/members/invitations/bulk</c>.</summary>
    [HttpPost("invitations/bulk", Name = "BulkInviteTenantMembersV1")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ServiceOkResponse<BulkInvitationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status429TooManyRequests)]
    public Task<IActionResult> BulkInvite([FromBody] BulkInvitationRequest request,
        CancellationToken ct) => Run(async actor =>
        new ServiceOkResponse<BulkInvitationResponse>(await members.InviteBulkAsync(actor.TenantId,
            actor.UserId, actor.SessionVersion, request, async (email, code, token) =>
            {
                try { await notifications.SendAsync(email, "invitation", code, token); return true; }
                catch (AuthenticationException exception) when (exception.Failure is
                    AuthenticationFailure.Unavailable or AuthenticationFailure.RateLimited) { return false; }
            }, ct)));

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

    /// <summary>Clears a member's authenticator and recovery codes (a lost device) and ends their
    /// sessions; they enroll again at their next sign-in if the organization requires MFA. Not for the
    /// caller's own account (409 <c>CANNOT_CHANGE_SELF</c>).</summary>
    [HttpPost("{accountId:guid}/mfa-reset", Name = "ResetTenantMemberMfaV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> ResetMfa(Guid accountId, [FromBody] ResetTenantMemberMfaRequest request,
        CancellationToken ct) => Run(async actor =>
    {
        await members.ResetMemberMfaAsync(actor.TenantId, actor.UserId, actor.SessionVersion, accountId,
            request, ct);
        return new ServiceOkResponse<object?>(null, StatusCodes.Status204NoContent);
    });

    /// <summary>Turns the organization's MFA requirement on or off. On needs the caller to have an
    /// authenticator (409 <c>MFA_NOT_ENROLLED</c>) and ends the sessions of members who have none.
    /// Gateway exposes it as <c>/api/v1/identity/organization/mfa</c>.</summary>
    [HttpPut("~/api/v1/organization/mfa", Name = "SetOrganizationMfaRequirementV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> SetMfaRequirement([FromBody] SetTenantMfaRequirementRequest request,
        CancellationToken ct) => Run(async actor =>
    {
        await members.SetMfaRequirementAsync(actor.TenantId, actor.UserId, actor.SessionVersion, request, ct);
        return new ServiceOkResponse<object>(new { requireMfa = request.Required });
    });

    /// <summary>Puts a member, the caller included, in a group, or in none with a null groupId.</summary>
    [HttpPut("{accountId:guid}/group", Name = "SetTenantMemberGroupV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> SetGroup(Guid accountId, [FromBody] SetTenantMemberGroupRequest request,
        CancellationToken ct) => Run(async actor =>
    {
        await members.SetGroupAsync(actor.TenantId, actor.UserId, actor.SessionVersion, accountId,
            request, ct);
        return new ServiceOkResponse<object?>(null, StatusCodes.Status204NoContent);
    });

    /// <summary>The organization's member groups, by name, with how many members each has.</summary>
    [HttpGet("groups", Name = "ListTenantMemberGroupsV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<IReadOnlyList<TenantMemberGroupEntry>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Groups(CancellationToken ct) => Run(async actor =>
        new ServiceOkResponse<IReadOnlyList<TenantMemberGroupEntry>>(await members.ListGroupsAsync(
            actor.TenantId, actor.UserId, actor.SessionVersion, ct)));

    [HttpPost("groups", Name = "CreateTenantMemberGroupV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<TenantMemberGroupEntry>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> CreateGroup([FromBody] SaveTenantMemberGroupRequest request,
        CancellationToken ct) => Run(async actor =>
        new ServiceOkResponse<TenantMemberGroupEntry>(await members.CreateGroupAsync(actor.TenantId,
            actor.UserId, actor.SessionVersion, request, ct), StatusCodes.Status201Created));

    [HttpPut("groups/{groupId:guid}", Name = "RenameTenantMemberGroupV1")]
    [ProducesResponseType(typeof(ServiceOkResponse<TenantMemberGroupEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> RenameGroup(Guid groupId, [FromBody] SaveTenantMemberGroupRequest request,
        CancellationToken ct) => Run(async actor =>
        new ServiceOkResponse<TenantMemberGroupEntry>(await members.RenameGroupAsync(actor.TenantId,
            actor.UserId, actor.SessionVersion, groupId, request, ct)));

    /// <summary>Deletes a group with no members; otherwise 409 GROUP_IN_USE.</summary>
    [HttpDelete("groups/{groupId:guid}", Name = "DeleteTenantMemberGroupV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ServiceErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> DeleteGroup(Guid groupId, CancellationToken ct) => Run(async actor =>
    {
        await members.DeleteGroupAsync(actor.TenantId, actor.UserId, actor.SessionVersion, groupId, ct);
        return new ServiceOkResponse<object?>(null, StatusCodes.Status204NoContent);
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
                TenantMemberFailure.GroupNameTaken => Error(StatusCodes.Status409Conflict, "GROUP_NAME_TAKEN"),
                TenantMemberFailure.GroupInUse => Error(StatusCodes.Status409Conflict, "GROUP_IN_USE"),
                TenantMemberFailure.MfaNotEnrolled => Error(StatusCodes.Status409Conflict, "MFA_NOT_ENROLLED"),
                TenantMemberFailure.RateLimited => Error(StatusCodes.Status429TooManyRequests, "RATE_LIMITED"),
                TenantMemberFailure.Unavailable => Error(StatusCodes.Status503ServiceUnavailable, "ACTIVATION_DELIVERY_UNAVAILABLE"),
                _ => Error(StatusCodes.Status400BadRequest, "VALIDATION_FAILED")
            };
        }
        // Ending sessions publishes to Redis; an outage leaves the change rolled back.
        catch (AuthenticationException exception) when (exception.Failure == AuthenticationFailure.Unavailable)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "SESSION_REVOCATION_UNAVAILABLE");
        }
    }

    private IActionResult Error(int status, string code) => ReturnResponseWithHttpStatus(
        new ServiceErrorResponse(status, code, "The request could not be completed."));
}
