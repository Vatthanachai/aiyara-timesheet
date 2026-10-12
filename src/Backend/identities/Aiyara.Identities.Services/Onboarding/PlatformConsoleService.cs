using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Onboarding;

/// <summary>Read-only views for the SysAdmin console. Callers must already have verified a
/// Platform Admin actor; every query here reads across tenants on purpose. Results carry only
/// organization metadata, administrator emails, counts, and audit entries, never tenant content.</summary>
public sealed class PlatformConsoleService(IdentityDbContext db, PlatformOrganizationService organizations)
{
    private const int RecentActivityCount = 8;
    private const int NewestOrganizationCount = 5;

    public async Task<PlatformOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var customers = CustomerTenants();
        var statuses = await customers.GroupBy(tenant => tenant.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var awaitingAdministrator = await customers.CountAsync(tenant =>
            !db.Memberships.IgnoreQueryFilters().Any(membership => membership.TenantId == tenant.Id &&
                membership.Role == TenantRole.TenantAdmin &&
                membership.Status == MembershipStatus.Active), cancellationToken);
        var newest = await organizations.ListAsync(null, 1, NewestOrganizationCount, cancellationToken);
        var activity = await ListAuditAsync(new PlatformAuditQuery(null, null, null, null, null,
            1, RecentActivityCount), cancellationToken);
        int CountOf(TenantStatus status) => statuses.SingleOrDefault(x => x.Status == status)?.Count ?? 0;
        return new PlatformOverviewResponse(statuses.Sum(x => x.Count), CountOf(TenantStatus.Active),
            CountOf(TenantStatus.Suspended), awaitingAdministrator, newest.Items, activity.Items);
    }

    public async Task<PlatformOrganizationDetail> GetOrganizationAsync(Guid tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await CustomerTenants().SingleOrDefaultAsync(x => x.Id == tenantId, cancellationToken)
            ?? throw new PlatformOrganizationException(PlatformOrganizationFailure.NotFound);
        var memberships = db.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(membership => membership.TenantId == tenantId);
        var activeMembers = await memberships.CountAsync(x => x.Status == MembershipStatus.Active,
            cancellationToken);
        var pendingMembers = await memberships.CountAsync(x => x.Status == MembershipStatus.PendingActivation,
            cancellationToken);
        var admins = await memberships.Where(membership => membership.Role == TenantRole.TenantAdmin)
            .Join(db.Accounts.AsNoTracking(), membership => membership.AccountId, account => account.Id,
                (membership, account) => new { account.Email, membership.Status })
            .OrderBy(admin => admin.Email)
            .ToListAsync(cancellationToken);
        return new PlatformOrganizationDetail(tenant.Id, tenant.OrganizationCode, tenant.Name,
            tenant.Slug, tenant.Status.ToString(), tenant.CreatedAtUtc, activeMembers, pendingMembers,
            admins.Select(admin => new PlatformOrganizationAdminSummary(admin.Email,
                admin.Status.ToString())).ToArray());
    }

    public async Task<PlatformAuditListResponse> ListAuditAsync(PlatformAuditQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Actor?.Length > 320 || request.Action?.Length > 60 ||
            request.FromUtc > request.ToUtc)
            throw new PlatformOrganizationException(PlatformOrganizationFailure.InvalidInput);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var query = db.PlatformOrganizationAudits.IgnoreQueryFilters().AsNoTracking();
        if (request.TenantId is { } tenantId) query = query.Where(entry => entry.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(request.Action))
            query = query.Where(entry => entry.Action == request.Action.Trim());
        if (request.FromUtc is { } from) query = query.Where(entry => entry.CreatedAtUtc >= from);
        if (request.ToUtc is { } to) query = query.Where(entry => entry.CreatedAtUtc <= to);
        if (!string.IsNullOrWhiteSpace(request.Actor))
        {
            var actor = request.Actor.Trim().ToLowerInvariant();
            query = query.Where(entry => db.Accounts.Any(account =>
                account.Id == entry.ActorAccountId && account.Email.Contains(actor)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var page = Math.Clamp(request.Page, 1, Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize)));
        var items = await query.OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(entry => new PlatformAuditEntry(entry.Id, entry.CreatedAtUtc, entry.Action,
                entry.TenantId,
                db.Tenants.IgnoreQueryFilters().Where(tenant => tenant.Id == entry.TenantId)
                    .Select(tenant => tenant.Name).FirstOrDefault(),
                db.Tenants.IgnoreQueryFilters().Where(tenant => tenant.Id == entry.TenantId)
                    .Select(tenant => tenant.OrganizationCode).FirstOrDefault(),
                db.Accounts.Where(account => account.Id == entry.ActorAccountId)
                    .Select(account => account.Email).FirstOrDefault(),
                entry.Reason, entry.BeforeJson, entry.AfterJson))
            .ToListAsync(cancellationToken);
        return new PlatformAuditListResponse(totalCount, page, pageSize, items);
    }

    private IQueryable<Tenant> CustomerTenants() => db.Tenants.IgnoreQueryFilters().AsNoTracking()
        .Where(tenant => tenant.Id != PlatformAdminBootstrap.PlatformTenantId &&
            tenant.Slug != "platform-system");
}
