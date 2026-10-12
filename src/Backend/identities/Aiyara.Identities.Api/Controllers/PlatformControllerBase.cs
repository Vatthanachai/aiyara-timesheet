using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.Controllers;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Api.Controllers;

public sealed record PlatformActor(Guid AccountId, long SessionVersion);

/// <summary>Identity's own Platform Admin check, applied on every platform request in addition
/// to Gateway's role policy: the token must name the platform tenant and the PlatformAdmin
/// role, carry the account's current session version, and belong to an account that is still a
/// Platform Admin with an active platform membership and no pending forced password change.</summary>
public sealed class PlatformActorResolver(IdentityDbContext db, TenantScope tenantScope,
    IPasetoTokenService tokens)
{
    public async Task<PlatformActor?> ResolveAsync(string? authorization,
        CancellationToken cancellationToken)
    {
        var claims = authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
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
}

public abstract class PlatformControllerBase(PlatformActorResolver actors) : BaseController
{
    protected Task<PlatformActor?> PlatformActorAsync(CancellationToken cancellationToken) =>
        actors.ResolveAsync(Request.Headers.Authorization.ToString(), cancellationToken);

    protected IActionResult Error(PlatformOrganizationException exception) => exception.Failure switch
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

    protected IActionResult UnauthorizedResponse() => Error(StatusCodes.Status401Unauthorized, "UNAUTHORIZED");

    protected IActionResult Error(int status, string code) => ReturnResponseWithHttpStatus(
        new ServiceErrorResponse(status, code, "The request could not be completed."));
}
