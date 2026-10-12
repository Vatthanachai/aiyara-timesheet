using Aiyara.Identities.Databases;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Onboarding;

/// <summary>Platform-wide security settings. Only the platform tenant's password policy lives here;
/// customer organizations keep theirs with their own Tenant Admins.</summary>
public sealed class PlatformSecurityService(IdentityDbContext db, PlatformOrganizationService organizations,
    AuthenticationService authentication)
{
    public const string PasswordPolicyUpdatedAction = "platform.password_policy_updated";

    public async Task<PlatformPasswordPolicy> GetPasswordPolicyAsync(CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == PlatformAdminBootstrap.PlatformTenantId, cancellationToken);
        return new PlatformPasswordPolicy(tenant.PasswordMinimumLength, tenant.PasswordExpiryDays,
            tenant.PasswordRequireUppercase, tenant.PasswordRequireLowercase,
            tenant.PasswordRequireDigit, tenant.PasswordRequireSymbol);
    }

    /// <summary>A stricter policy makes every Platform Admin, the caller included, change their
    /// password at next sign-in and ends their sessions, as it does for customer organizations.</summary>
    public async Task UpdatePasswordPolicyAsync(Guid actorAccountId, long actorSessionVersion,
        UpdatePlatformPasswordPolicyRequest request, CancellationToken cancellationToken)
    {
        await organizations.EnsurePlatformAdminAsync(actorAccountId, actorSessionVersion, cancellationToken);
        var reason = PlatformOrganizationService.NormalizeReason(request.Reason);
        try
        {
            await authentication.UpdatePlatformPolicyAsync(new TenantPasswordPolicyRequest(
                    request.MinimumLength, request.ExpiryDays, request.RequireUppercase,
                    request.RequireLowercase, request.RequireDigit, request.RequireSymbol),
                (before, after) => organizations.AddAudit(PlatformAdminBootstrap.PlatformTenantId,
                    actorAccountId, PasswordPolicyUpdatedAction, reason, before, after),
                cancellationToken);
        }
        catch (AuthenticationException exception) when (exception.Failure == AuthenticationFailure.InvalidInput)
        {
            throw new PlatformOrganizationException(PlatformOrganizationFailure.InvalidInput);
        }
    }
}
