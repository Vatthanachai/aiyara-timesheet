using System.Text.Json;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Onboarding;

/// <summary>Manages the Platform Admin roster. Invited admins use the same activation flow as
/// the bootstrap admin, and their accounts stay dedicated to the platform tenant. A Platform
/// Admin cannot deactivate themselves, so at least one active admin always remains. The
/// <c>BOOTSTRAP_PLATFORM_ADMIN_EMAIL</c> setting stays the recovery path; it never revives a
/// deactivated membership.</summary>
public sealed class PlatformAdminRosterService(IdentityDbContext db, TenantScope tenantScope,
    PlatformOrganizationService organizations, AuthenticationService? authentication = null)
{
    private static readonly Guid PlatformTenantId = PlatformAdminBootstrap.PlatformTenantId;

    public async Task<IReadOnlyList<PlatformAdminRosterEntry>> ListAsync(Guid actorAccountId,
        CancellationToken cancellationToken)
    {
        var admins = await db.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == PlatformTenantId && x.Role == TenantRole.PlatformAdmin)
            .Join(db.Accounts.AsNoTracking(), membership => membership.AccountId, account => account.Id,
                (membership, account) => new
                {
                    account.Id, account.Email, account.FirstName, account.LastName, membership.Status,
                    membership.CreatedAtUtc
                })
            .OrderBy(x => x.Email)
            .ToListAsync(cancellationToken);
        return admins.Select(x => new PlatformAdminRosterEntry(x.Email, x.FirstName, x.LastName,
            x.Status.ToString(), x.CreatedAtUtc, x.Id == actorAccountId)).ToArray();
    }

    /// <summary>Invites a new Platform Admin, or re-sends activation to one who has not
    /// activated yet. Accounts that belong to a customer organization are refused.</summary>
    public async Task InviteAsync(Guid actorAccountId, long actorSessionVersion,
        InvitePlatformAdminRequest request, CancellationToken cancellationToken)
    {
        await organizations.EnsurePlatformAdminAsync(actorAccountId, actorSessionVersion, cancellationToken);
        var email = PlatformOrganizationService.NormalizeEmail(request.Email);
        tenantScope.TenantId = PlatformTenantId;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        var memberships = account is null ? [] : await db.Memberships.IgnoreQueryFilters()
            .Where(x => x.AccountId == account.Id).ToListAsync(cancellationToken);
        if (memberships.Any(x => x.TenantId != PlatformTenantId))
            throw new PlatformOrganizationException(PlatformOrganizationFailure.Conflict);
        var existing = memberships.SingleOrDefault();
        if (existing is { Status: not MembershipStatus.PendingActivation })
            throw new PlatformOrganizationException(PlatformOrganizationFailure.Conflict);

        var now = DateTime.UtcNow;
        if (account is null)
        {
            account = new Account { Id = IdentityGuid.New(), Email = email, CreatedAtUtc = now };
            db.Accounts.Add(account);
        }
        account.IsPlatformAdmin = true;
        if (existing is null)
            db.Memberships.Add(new Membership
            {
                Id = IdentityGuid.New(), TenantId = PlatformTenantId, AccountId = account.Id,
                Role = TenantRole.PlatformAdmin, Status = MembershipStatus.PendingActivation,
                CreatedAtUtc = now
            });
        organizations.AddAudit(PlatformTenantId, actorAccountId,
            existing is null ? "platform_admin.invited" : "platform_admin.activation_resent", null, null,
            JsonSerializer.Serialize(new { Email = email, Status = MembershipStatus.PendingActivation.ToString() }), now);
        await db.SaveChangesAsync(cancellationToken);
        await SendActivationAsync(email, cancellationToken);
    }

    public async Task SetStatusAsync(Guid actorAccountId, long actorSessionVersion,
        SetPlatformAdminStatusRequest request, CancellationToken cancellationToken)
    {
        await organizations.EnsurePlatformAdminAsync(actorAccountId, actorSessionVersion, cancellationToken);
        var email = PlatformOrganizationService.NormalizeEmail(request.Email);
        var reason = PlatformOrganizationService.NormalizeReason(request.Reason);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        tenantScope.TenantId = PlatformTenantId;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        var membership = account is null ? null : await db.Memberships.SingleOrDefaultAsync(x =>
            x.AccountId == account.Id && x.Role == TenantRole.PlatformAdmin, cancellationToken);
        if (account is null || membership is null)
            throw new PlatformOrganizationException(PlatformOrganizationFailure.NotFound);

        var before = JsonSerializer.Serialize(new { Email = email, Status = membership.Status.ToString() });
        string action;
        if (!request.Active)
        {
            if (membership.Status == MembershipStatus.Deactivated) return;
            if (account.Id == actorAccountId)
                throw new PlatformOrganizationException(PlatformOrganizationFailure.SelfChange);
            if (membership.Status == MembershipStatus.Active && !await db.Memberships.AnyAsync(x =>
                    x.Id != membership.Id && x.Role == TenantRole.PlatformAdmin &&
                    x.Status == MembershipStatus.Active, cancellationToken))
                throw new PlatformOrganizationException(PlatformOrganizationFailure.LastAdministrator);
            membership.Status = MembershipStatus.Deactivated;
            action = "platform_admin.deactivated";
        }
        else
        {
            if (membership.Status != MembershipStatus.Deactivated) return;
            // An admin who never set a password goes back through activation.
            membership.Status = account.PasswordHash is null
                ? MembershipStatus.PendingActivation : MembershipStatus.Active;
            action = "platform_admin.reactivated";
        }

        organizations.AddAudit(PlatformTenantId, actorAccountId, action, reason, before,
            JsonSerializer.Serialize(new { Email = email, Status = membership.Status.ToString() }));
        await organizations.EndSessionsAsync(account, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (membership.Status == MembershipStatus.PendingActivation)
            await SendActivationAsync(email, cancellationToken);
    }

    private async Task SendActivationAsync(string email, CancellationToken cancellationToken)
    {
        if (authentication is null) return;
        try
        {
            await authentication.RequestActivationAsync(new RequestCredentialEmail(PlatformTenantId, email),
                cancellationToken);
        }
        // The roster change stands either way; the activation can be re-sent from the roster.
        catch (AuthenticationException exception) when (exception.Failure is
            AuthenticationFailure.Unavailable or AuthenticationFailure.RateLimited) { }
    }
}
