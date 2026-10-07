using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Onboarding;

public sealed class PlatformOrganizationService(IdentityDbContext db)
{
    public async Task<PlatformOrganizationListResponse> ListAsync(string? search,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(tenant => tenant.Slug != "platform-system");
        var normalizedSearch = search?.Trim();
        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            var emailSearch = normalizedSearch.ToLowerInvariant();
            query = query.Where(tenant => tenant.Name.ToLower().Contains(emailSearch) ||
                tenant.Slug.Contains(emailSearch) ||
                tenant.OrganizationCode.Contains(normalizedSearch.ToUpperInvariant()) ||
                db.Memberships.IgnoreQueryFilters().Any(membership =>
                    membership.TenantId == tenant.Id && membership.Role == TenantRole.TenantAdmin &&
                    db.Accounts.Any(account => account.Id == membership.AccountId &&
                        account.Email.Contains(emailSearch))));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize)));
        var tenants = await query.OrderByDescending(tenant => tenant.CreatedAtUtc)
            .ThenBy(tenant => tenant.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(tenant => new TenantRow(tenant.Id, tenant.OrganizationCode,
                tenant.Name, tenant.Slug, tenant.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var tenantIds = tenants.Select(tenant => tenant.TenantId).ToArray();
        var admins = await db.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(membership => tenantIds.Contains(membership.TenantId) &&
                membership.Role == TenantRole.TenantAdmin)
            .Join(db.Accounts.AsNoTracking(), membership => membership.AccountId,
                account => account.Id, (membership, account) => new
                {
                    membership.TenantId, account.Email, membership.Status
                })
            .OrderBy(admin => admin.Email)
            .Select(admin => new AdminRow(admin.TenantId, admin.Email, admin.Status))
            .ToListAsync(cancellationToken);
        var adminsByTenant = admins.GroupBy(admin => admin.TenantId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<PlatformOrganizationAdminSummary>)
                group.Select(admin => new PlatformOrganizationAdminSummary(admin.Email,
                    admin.Status.ToString())).ToArray());
        var items = tenants.Select(tenant => new PlatformOrganizationSummary(
            tenant.TenantId, tenant.OrganizationCode, tenant.Name, tenant.Slug,
            tenant.CreatedAtUtc, adminsByTenant.GetValueOrDefault(tenant.TenantId,
                Array.Empty<PlatformOrganizationAdminSummary>()))).ToArray();

        return new PlatformOrganizationListResponse(totalCount, page, pageSize, items);
    }

    private sealed record TenantRow(Guid TenantId, string OrganizationCode, string Name,
        string Slug, DateTime CreatedAtUtc);

    private sealed record AdminRow(Guid TenantId, string Email, MembershipStatus Status);
}
