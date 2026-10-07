using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Api.Controllers;

[Route("api/v1/platform/organizations")]
public sealed class PlatformOrganizationsController(IdentityDbContext db, TenantScope tenantScope,
    IPasetoTokenService tokens, PlatformOrganizationService organizations,
    ICredentialNotificationSender notifications) : BaseController
{
    [HttpGet]
    public async Task<IActionResult> List(string? search, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (await PlatformActorAsync(cancellationToken) is null) return UnauthorizedResponse();
        if (search?.Length > 100)
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_FAILED");
        var result = await organizations.ListAsync(search, Math.Max(page, 1),
            Math.Clamp(pageSize, 1, 100), cancellationToken);
        return ReturnResponseWithHttpStatus(new ServiceOkResponse<PlatformOrganizationListResponse>(result));
    }

    [HttpPut("{tenantId:guid}")]
    public async Task<IActionResult> Update(Guid tenantId,
        [FromBody] UpdatePlatformOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await PlatformActorAsync(cancellationToken);
        if (actor is null) return UnauthorizedResponse();
        try
        {
            var result = await organizations.UpdateAsync(tenantId, actor.AccountId,
                actor.SessionVersion, request, cancellationToken);
            return ReturnResponseWithHttpStatus(new ServiceOkResponse<PlatformOrganizationUpdateResult>(result));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
    }

    [HttpPut("{tenantId:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid tenantId,
        [FromBody] SetPlatformOrganizationStatusRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await PlatformActorAsync(cancellationToken);
        if (actor is null) return UnauthorizedResponse();
        try
        {
            var result = await organizations.SetStatusAsync(tenantId, actor.AccountId,
                actor.SessionVersion, request, cancellationToken);
            return ReturnResponseWithHttpStatus(new ServiceOkResponse<PlatformOrganizationUpdateResult>(result));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
        catch (AuthenticationException exception) when (exception.Failure == AuthenticationFailure.Unavailable)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "SESSION_REVOCATION_UNAVAILABLE");
        }
    }

    [HttpPost("{tenantId:guid}/administrators")]
    public async Task<IActionResult> InviteAdministrator(Guid tenantId,
        [FromBody] InvitePlatformOrganizationAdminRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var actor = await PlatformActorAsync(cancellationToken);
        if (actor is null) return UnauthorizedResponse();
        try
        {
            var issued = await organizations.InviteAdministratorAsync(tenantId,
                actor.AccountId, actor.SessionVersion, request, cancellationToken);
            await notifications.SendAsync(request.Email.Trim(), "invitation", issued.Code,
                cancellationToken);
            return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(new
            {
                invitationId = issued.InvitationId,
                tenantId = issued.TenantId,
                expiresAtUtc = issued.ExpiresAtUtc,
                deliveryStatus = "accepted"
            }, StatusCodes.Status202Accepted));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
        catch (AuthenticationException exception) when (exception.Failure == AuthenticationFailure.Unavailable)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "INVITATION_DELIVERY_UNAVAILABLE");
        }
    }

    [HttpPost("{tenantId:guid}/administrators/activation")]
    public async Task<IActionResult> ResendAdministratorActivation(Guid tenantId,
        [FromBody] RequestPlatformAdministratorActivationRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await PlatformActorAsync(cancellationToken);
        if (actor is null) return UnauthorizedResponse();
        try
        {
            await organizations.ResendAdministratorActivationAsync(tenantId, actor.AccountId,
                actor.SessionVersion, request.Email, cancellationToken);
            return ReturnResponseWithHttpStatus(new ServiceOkResponse<object>(new
            {
                deliveryStatus = "accepted"
            }));
        }
        catch (PlatformOrganizationException exception) { return Error(exception); }
        catch (AuthenticationException exception) when (exception.Failure == AuthenticationFailure.Unavailable)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "ACTIVATION_DELIVERY_UNAVAILABLE");
        }
    }

    private async Task<PlatformActor?> PlatformActorAsync(CancellationToken cancellationToken)
    {
        var authorization = Request.Headers.Authorization.ToString();
        var claims = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? tokens.ValidateToken(authorization[7..].Trim()) : null;
        if (claims is null || claims.MustChangePassword ||
            claims.Role != TenantRole.PlatformAdmin.ToString() ||
            claims.TenantId != PlatformAdminBootstrap.PlatformTenantId)
            return null;

        tenantScope.TenantId = PlatformAdminBootstrap.PlatformTenantId;
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == claims.UserId && x.IsPlatformAdmin &&
            x.SessionVersion == claims.SessionVersion && !x.MustChangePassword,
            cancellationToken);
        var membership = await db.Memberships.AsNoTracking().SingleOrDefaultAsync(x =>
            x.AccountId == claims.UserId && x.Role == TenantRole.PlatformAdmin &&
            x.Status == MembershipStatus.Active && !x.MustChangePassword, cancellationToken);
        return account is null || membership is null
            ? null
            : new PlatformActor(account.Id, account.SessionVersion);
    }

    private IActionResult Error(PlatformOrganizationException exception) => exception.Failure switch
    {
        PlatformOrganizationFailure.Unauthorized => Error(StatusCodes.Status401Unauthorized, "UNAUTHORIZED"),
        PlatformOrganizationFailure.NotFound => Error(StatusCodes.Status404NotFound, "NOT_FOUND"),
        PlatformOrganizationFailure.InvalidInput => Error(StatusCodes.Status400BadRequest, "VALIDATION_FAILED"),
        PlatformOrganizationFailure.Conflict => Error(StatusCodes.Status409Conflict, "CONFLICT"),
        PlatformOrganizationFailure.ProtectedTenant => Error(StatusCodes.Status409Conflict, "PROTECTED_TENANT"),
        PlatformOrganizationFailure.Suspended => Error(StatusCodes.Status409Conflict, "ORGANIZATION_SUSPENDED"),
        PlatformOrganizationFailure.Unavailable => Error(StatusCodes.Status503ServiceUnavailable, "ACTIVATION_DELIVERY_UNAVAILABLE"),
        _ => Error(StatusCodes.Status400BadRequest, "VALIDATION_FAILED")
    };

    private IActionResult UnauthorizedResponse() => Error(StatusCodes.Status401Unauthorized, "UNAUTHORIZED");

    private IActionResult Error(int status, string code) => ReturnResponseWithHttpStatus(
        new ServiceErrorResponse(status, code, "The request could not be completed."));

    private sealed record PlatformActor(Guid AccountId, long SessionVersion);
}
