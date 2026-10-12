using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Onboarding;

public sealed class PlatformOrganizationService(IdentityDbContext db, TenantScope tenantScope,
    ISessionRevocationPublisher revocations, AuthenticationService? authentication = null)
{
    private static readonly Regex ValidSlug = new("^[a-z0-9][a-z0-9-]{2,79}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<PlatformOrganizationListResponse> ListAsync(string? search,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(tenant => tenant.Id != PlatformAdminBootstrap.PlatformTenantId &&
                tenant.Slug != "platform-system");
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
                tenant.Name, tenant.Slug, tenant.Status, tenant.CreatedAtUtc))
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
            tenant.Status.ToString(), tenant.CreatedAtUtc,
            adminsByTenant.GetValueOrDefault(tenant.TenantId,
                Array.Empty<PlatformOrganizationAdminSummary>()))).ToArray();

        return new PlatformOrganizationListResponse(totalCount, page, pageSize, items);
    }

    public async Task<PlatformOrganizationUpdateResult> UpdateAsync(Guid tenantId,
        Guid actorAccountId, long actorSessionVersion,
        UpdatePlatformOrganizationRequest request, CancellationToken cancellationToken)
    {
        await EnsurePlatformAdminAsync(actorAccountId, actorSessionVersion, cancellationToken);
        var name = request.Name?.Trim();
        var slug = request.Slug?.Trim().ToLowerInvariant();
        var reason = NormalizeReason(request.Reason);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || slug is null ||
            !ValidSlug.IsMatch(slug) || slug == "platform-system")
            throw new PlatformOrganizationException(PlatformOrganizationFailure.InvalidInput);

        var tenant = await FindManageableTenantAsync(tenantId, cancellationToken);
        if (await db.Tenants.IgnoreQueryFilters().AnyAsync(x => x.Id != tenantId &&
            x.Slug == slug, cancellationToken))
            throw new PlatformOrganizationException(PlatformOrganizationFailure.Conflict);

        var before = JsonSerializer.Serialize(new { tenant.Name, tenant.Slug });
        tenant.Name = name;
        tenant.Slug = slug;
        var after = JsonSerializer.Serialize(new { tenant.Name, tenant.Slug });
        AddAudit(tenantId, actorAccountId, "organization.updated", reason, before, after);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new PlatformOrganizationException(PlatformOrganizationFailure.Conflict);
        }

        return ToUpdateResult(tenant);
    }

    public async Task<PlatformOrganizationUpdateResult> SetStatusAsync(Guid tenantId,
        Guid actorAccountId, long actorSessionVersion,
        SetPlatformOrganizationStatusRequest request, CancellationToken cancellationToken)
    {
        await EnsurePlatformAdminAsync(actorAccountId, actorSessionVersion, cancellationToken);
        var reason = NormalizeReason(request.Reason);
        var tenant = await FindManageableTenantAsync(tenantId, cancellationToken);
        var desiredStatus = request.Suspended ? TenantStatus.Suspended : TenantStatus.Active;
        if (tenant.Status == desiredStatus)
        {
            if (desiredStatus == TenantStatus.Active)
            {
                var currentPolicyVersion = Math.Max(tenant.AccessRevocationVersion,
                    tenant.PasswordPolicyUpdatedAtUtc.Ticks / 10);
                await revocations.CompleteTenantPolicyAsync(tenantId, currentPolicyVersion,
                    cancellationToken);
            }
            return ToUpdateResult(tenant);
        }

        var before = JsonSerializer.Serialize(new { Status = tenant.Status.ToString() });
        var action = request.Suspended ? "organization.suspended" : "organization.reactivated";
        var now = DateTime.UtcNow;
        var policyVersion = Math.Max(tenant.AccessRevocationVersion,
            tenant.PasswordPolicyUpdatedAtUtc.Ticks / 10) + 1;

        // Block Gateway cache entries before changing state; if Redis is unavailable,
        // fail closed and leave the organization unchanged.
        await revocations.PublishTenantPolicyAsync(tenantId, cancellationToken);
        tenant.Status = desiredStatus;
        tenant.AccessRevocationVersion = policyVersion;
        AddAudit(tenantId, actorAccountId, action, reason, before,
            JsonSerializer.Serialize(new { Status = desiredStatus.ToString() }), now);
        await db.SaveChangesAsync(cancellationToken);
        await revocations.CompleteTenantPolicyAsync(tenantId, policyVersion,
            cancellationToken);
        return ToUpdateResult(tenant);
    }

    public async Task<IssueInvitationResponse> InviteAdministratorAsync(Guid tenantId,
        Guid actorAccountId, long actorSessionVersion,
        InvitePlatformOrganizationAdminRequest request, CancellationToken cancellationToken)
    {
        await EnsurePlatformAdminAsync(actorAccountId, actorSessionVersion, cancellationToken);
        var email = request.Email?.Trim().ToLowerInvariant();
        if (email is null || email.Length > 320 || !MailAddress.TryCreate(email, out var parsed) ||
            parsed.Address != email)
            throw new PlatformOrganizationException(PlatformOrganizationFailure.InvalidInput);

        var tenant = await FindManageableTenantAsync(tenantId, cancellationToken);
        if (tenant.Status != TenantStatus.Active)
            throw new PlatformOrganizationException(PlatformOrganizationFailure.Suspended);

        tenantScope.TenantId = tenantId;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Email == email,
            cancellationToken);
        if (account is not null && await db.Memberships.AnyAsync(x =>
            x.AccountId == account.Id, cancellationToken))
            throw new PlatformOrganizationException(PlatformOrganizationFailure.Conflict);

        var now = DateTime.UtcNow;
        var staleInvitations = await db.Invitations.Where(x => x.Email == email &&
            x.Role == TenantRole.TenantAdmin && x.AcceptedAtUtc == null &&
            x.ExpiresAtUtc > now).ToListAsync(cancellationToken);
        foreach (var stale in staleInvitations) stale.ExpiresAtUtc = now;

        var code = InvitationCode.Generate();
        var invitation = new Invitation
        {
            Id = IdentityGuid.New(), TenantId = tenantId, Email = email,
            TokenHash = InvitationCode.Hash(code), Role = TenantRole.TenantAdmin,
            InvitedByAccountId = actorAccountId, CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(7)
        };
        db.Invitations.Add(invitation);
        AddAudit(tenantId, actorAccountId, "administrator.invited", null, null,
            JsonSerializer.Serialize(new { Email = email, Role = TenantRole.TenantAdmin.ToString() }), now);
        await db.SaveChangesAsync(cancellationToken);
        return new IssueInvitationResponse(invitation.Id, tenantId, invitation.ExpiresAtUtc, code);
    }

    public async Task ResendAdministratorActivationAsync(Guid tenantId, Guid actorAccountId,
        long actorSessionVersion, string? requestedEmail, CancellationToken cancellationToken)
    {
        await EnsurePlatformAdminAsync(actorAccountId, actorSessionVersion, cancellationToken);
        var email = requestedEmail?.Trim().ToLowerInvariant();
        if (email is null || email.Length > 320 || !MailAddress.TryCreate(email, out var parsed) ||
            parsed.Address != email)
            throw new PlatformOrganizationException(PlatformOrganizationFailure.InvalidInput);

        var tenant = await FindManageableTenantAsync(tenantId, cancellationToken);
        if (tenant.Status != TenantStatus.Active)
            throw new PlatformOrganizationException(PlatformOrganizationFailure.Suspended);
        tenantScope.TenantId = tenantId;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Email == email,
            cancellationToken);
        var membership = account is null ? null : await db.Memberships.SingleOrDefaultAsync(x =>
            x.AccountId == account.Id && x.Role == TenantRole.TenantAdmin &&
            x.Status == MembershipStatus.PendingActivation, cancellationToken);
        if (membership is null)
            throw new PlatformOrganizationException(PlatformOrganizationFailure.NotFound);

        if (authentication is null)
            throw new PlatformOrganizationException(PlatformOrganizationFailure.Unavailable);
        await authentication.RequestActivationAsync(new RequestCredentialEmail(tenantId, email),
            cancellationToken);
        AddAudit(tenantId, actorAccountId, "administrator.activation_resent", null,
            JsonSerializer.Serialize(new { Status = MembershipStatus.PendingActivation.ToString() }),
            JsonSerializer.Serialize(new { Email = email, Status = MembershipStatus.PendingActivation.ToString() }));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsurePlatformAdminAsync(Guid actorAccountId, long sessionVersion,
        CancellationToken cancellationToken)
    {
        tenantScope.TenantId = PlatformAdminBootstrap.PlatformTenantId;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == actorAccountId &&
            x.IsPlatformAdmin && x.SessionVersion == sessionVersion && !x.MustChangePassword,
            cancellationToken);
        var membership = await db.Memberships.SingleOrDefaultAsync(x =>
            x.AccountId == actorAccountId && x.Role == TenantRole.PlatformAdmin &&
            x.Status == MembershipStatus.Active && !x.MustChangePassword, cancellationToken);
        var platformTenant = await db.Tenants.SingleOrDefaultAsync(cancellationToken);
        if (account is null || membership is null || platformTenant is null ||
            platformTenant.Status != TenantStatus.Active)
            throw new PlatformOrganizationException(PlatformOrganizationFailure.Unauthorized);
    }

    private async Task<Tenant> FindManageableTenantAsync(Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || tenantId == PlatformAdminBootstrap.PlatformTenantId)
            throw new PlatformOrganizationException(PlatformOrganizationFailure.ProtectedTenant);
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.Id == tenantId && x.Slug != "platform-system", cancellationToken);
        return tenant ?? throw new PlatformOrganizationException(PlatformOrganizationFailure.NotFound);
    }

    private void AddAudit(Guid tenantId, Guid actorAccountId, string action, string? reason,
        string? before, string? after, DateTime? createdAtUtc = null) =>
        db.PlatformOrganizationAudits.Add(new PlatformOrganizationAudit
        {
            Id = IdentityGuid.New(), TenantId = tenantId, ActorAccountId = actorAccountId,
            Action = action, Reason = reason, BeforeJson = before, AfterJson = after,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        });

    private static string? NormalizeReason(string? reason)
    {
        var value = reason?.Trim();
        if (value?.Length > 500)
            throw new PlatformOrganizationException(PlatformOrganizationFailure.InvalidInput);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static PlatformOrganizationUpdateResult ToUpdateResult(Tenant tenant) =>
        new(tenant.Id, tenant.OrganizationCode, tenant.Name, tenant.Slug,
            tenant.Status.ToString());

    private sealed record TenantRow(Guid TenantId, string OrganizationCode, string Name,
        string Slug, TenantStatus Status, DateTime CreatedAtUtc);

    private sealed record AdminRow(Guid TenantId, string Email, MembershipStatus Status);
}

public enum PlatformOrganizationFailure
{
    Unauthorized, NotFound, InvalidInput, Conflict, ProtectedTenant, Suspended, Unavailable
}

public sealed class PlatformOrganizationException(PlatformOrganizationFailure failure)
    : Exception(failure.ToString())
{
    public PlatformOrganizationFailure Failure { get; } = failure;
}
