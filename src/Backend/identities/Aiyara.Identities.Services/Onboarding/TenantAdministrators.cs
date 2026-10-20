using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Onboarding;

/// <summary>The check behind every Tenant Admin action on members: an active TenantAdmin
/// membership in the tenant, the token's session version, and a password that is neither
/// flagged for change nor older than the tenant's policy or expiry. Leaves the tenant scope set.</summary>
public static class TenantAdministrators
{
    public static async Task<bool> IsActiveAdminAsync(IdentityDbContext db, TenantScope tenantScope,
        Guid tenantId, Guid actorAccountId, long sessionVersion, CancellationToken cancellationToken)
    {
        tenantScope.TenantId = tenantId;
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actorAccountId,
            cancellationToken);
        var membership = await db.Memberships.AsNoTracking().SingleOrDefaultAsync(x =>
            x.AccountId == actorAccountId && x.Role == TenantRole.TenantAdmin &&
            x.Status == MembershipStatus.Active, cancellationToken);
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return account is not null && membership is not null && tenant is not null &&
            tenant.Status == TenantStatus.Active && account.SessionVersion == sessionVersion &&
            !account.MustChangePassword && !membership.MustChangePassword &&
            account.PasswordChangedAtUtc is { } changed &&
            changed >= tenant.PasswordPolicyUpdatedAtUtc &&
            changed >= DateTime.UtcNow.AddDays(-tenant.PasswordExpiryDays);
    }
}
