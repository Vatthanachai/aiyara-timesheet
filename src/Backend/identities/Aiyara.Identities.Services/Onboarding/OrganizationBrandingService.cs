using System.Text.Json;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Services.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Onboarding;

/// <summary>The organization name and logo a Tenant Admin shows in the Shell header. Changes are
/// written to the organization audit log; the logo bytes live in object storage, which the API
/// layer handles, so this service only records the storage key.</summary>
public sealed class OrganizationBrandingService(IdentityDbContext db, TenantScope tenantScope)
{
    public async Task EnsureAdminAsync(Guid tenantId, Guid actorAccountId, long actorSessionVersion,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || tenantId == PlatformAdminBootstrap.PlatformTenantId ||
            !await TenantAdministrators.IsActiveAdminAsync(db, tenantScope, tenantId, actorAccountId,
                actorSessionVersion, cancellationToken))
            throw new TenantMemberException(TenantMemberFailure.Unauthorized);
    }

    public async Task RenameAsync(Guid tenantId, Guid actorAccountId, long actorSessionVersion,
        string? requestedName, CancellationToken cancellationToken)
    {
        var name = requestedName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200)
            throw new TenantMemberException(TenantMemberFailure.InvalidInput);
        await EnsureAdminAsync(tenantId, actorAccountId, actorSessionVersion, cancellationToken);
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        if (tenant.Name == name) return;
        var before = JsonSerializer.Serialize(new { tenant.Name });
        tenant.Name = name;
        PlatformOrganizationService.Audit(db, tenantId, actorAccountId, "organization.updated", null,
            before, JsonSerializer.Serialize(new { tenant.Name }));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Records a newly stored logo. The caller has already checked the admin.</summary>
    public async Task SetLogoAsync(Guid tenantId, Guid actorAccountId, string logoKey,
        CancellationToken cancellationToken)
    {
        tenantScope.TenantId = tenantId;
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        tenant.LogoKey = logoKey;
        tenant.LogoUpdatedAtUtc = DateTime.UtcNow;
        PlatformOrganizationService.Audit(db, tenantId, actorAccountId, "organization.logo_updated", null,
            null, null);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Clears the logo and returns the storage key it had, if any, for the caller to
    /// delete.</summary>
    public async Task<string?> RemoveLogoAsync(Guid tenantId, Guid actorAccountId,
        long actorSessionVersion, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(tenantId, actorAccountId, actorSessionVersion, cancellationToken);
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        var key = tenant.LogoKey;
        if (key is null) return null;
        tenant.LogoKey = null;
        tenant.LogoUpdatedAtUtc = DateTime.UtcNow;
        PlatformOrganizationService.Audit(db, tenantId, actorAccountId, "organization.logo_removed", null,
            null, null);
        await db.SaveChangesAsync(cancellationToken);
        return key;
    }
}
