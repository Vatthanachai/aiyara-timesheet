using System.Text.Json;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Authentication;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Onboarding;

/// <summary>A Tenant Admin's management of their own organization's members and invitations.
/// Every call is scoped to the actor's tenant, so another tenant's member or invitation is
/// <see cref="TenantMemberFailure.NotFound"/>. Members are deactivated, never deleted. An admin
/// cannot change their own role or status, and no change leaves the organization without an
/// active TenantAdmin. Role and status changes end the member's sessions everywhere, and every
/// change is written to the organization audit log. Member groups only classify members, so an
/// admin may put anyone in a group, themselves included, and sessions are left alone.</summary>
public sealed class TenantMemberService(IdentityDbContext db, TenantScope tenantScope,
    OnboardingService onboarding, PlatformOrganizationService organizations,
    AuthenticationService? authentication = null)
{
    public async Task<IReadOnlyList<TenantMemberEntry>> ListMembersAsync(Guid tenantId,
        Guid actorAccountId, long actorSessionVersion, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(tenantId, actorAccountId, actorSessionVersion, cancellationToken);
        var members = await db.Memberships.AsNoTracking()
            .Join(db.Accounts.AsNoTracking(), member => member.AccountId, person => person.Id,
                (member, person) => new { person.Id, person.FirstName, person.LastName, person.Email,
                    member.Role, member.Status, member.MemberGroupId })
            // Members who have not set a name yet (pending invitations) sort after named ones.
            .OrderBy(x => x.FirstName == "").ThenBy(x => x.FirstName).ThenBy(x => x.LastName)
            .ThenBy(x => x.Email)
            .ToListAsync(cancellationToken);
        var groups = await db.MemberGroups.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name,
            cancellationToken);
        return members.Select(x => new TenantMemberEntry(x.Id, x.Email, x.FirstName, x.LastName,
            x.Role.ToString(), x.Status.ToString(), x.Id == actorAccountId, x.MemberGroupId,
            x.MemberGroupId is { } groupId ? groups[groupId] : null)).ToArray();
    }

    public async Task<IReadOnlyList<TenantMemberGroupEntry>> ListGroupsAsync(Guid tenantId,
        Guid actorAccountId, long actorSessionVersion, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(tenantId, actorAccountId, actorSessionVersion, cancellationToken);
        var groups = await db.MemberGroups.AsNoTracking()
            .Select(x => new { x.Id, x.Name,
                MemberCount = db.Memberships.Count(member => member.MemberGroupId == x.Id) })
            .ToListAsync(cancellationToken);
        return groups.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new TenantMemberGroupEntry(x.Id, x.Name, x.MemberCount)).ToArray();
    }

    public async Task<TenantMemberGroupEntry> CreateGroupAsync(Guid tenantId, Guid actorAccountId,
        long actorSessionVersion, SaveTenantMemberGroupRequest request, CancellationToken cancellationToken)
    {
        var name = GroupName(request);
        await EnsureAdminAsync(tenantId, actorAccountId, actorSessionVersion, cancellationToken);
        await EnsureGroupNameFreeAsync(name, null, cancellationToken);
        var group = new MemberGroup { Id = IdentityGuid.New(), TenantId = tenantId, Name = name,
            CreatedAtUtc = DateTime.UtcNow };
        db.MemberGroups.Add(group);
        organizations.AddAudit(tenantId, actorAccountId, "member.group_created", null, null,
            JsonSerializer.Serialize(new { group.Name }));
        await SaveGroupAsync(cancellationToken);
        return new TenantMemberGroupEntry(group.Id, group.Name, 0);
    }

    public async Task<TenantMemberGroupEntry> RenameGroupAsync(Guid tenantId, Guid actorAccountId,
        long actorSessionVersion, Guid groupId, SaveTenantMemberGroupRequest request,
        CancellationToken cancellationToken)
    {
        var name = GroupName(request);
        await EnsureAdminAsync(tenantId, actorAccountId, actorSessionVersion, cancellationToken);
        var group = await FindGroupAsync(groupId, cancellationToken);
        var memberCount = await db.Memberships.CountAsync(x => x.MemberGroupId == groupId, cancellationToken);
        if (group.Name == name) return new TenantMemberGroupEntry(group.Id, group.Name, memberCount);
        await EnsureGroupNameFreeAsync(name, groupId, cancellationToken);
        var before = JsonSerializer.Serialize(new { group.Name });
        group.Name = name;
        organizations.AddAudit(tenantId, actorAccountId, "member.group_renamed", null, before,
            JsonSerializer.Serialize(new { group.Name }));
        await SaveGroupAsync(cancellationToken);
        return new TenantMemberGroupEntry(group.Id, group.Name, memberCount);
    }

    /// <summary>Deletes a group that no member belongs to; its members are moved out first.</summary>
    public async Task DeleteGroupAsync(Guid tenantId, Guid actorAccountId, long actorSessionVersion,
        Guid groupId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(tenantId, actorAccountId, actorSessionVersion, cancellationToken);
        var group = await FindGroupAsync(groupId, cancellationToken);
        if (await db.Memberships.AnyAsync(x => x.MemberGroupId == groupId, cancellationToken))
            throw new TenantMemberException(TenantMemberFailure.GroupInUse);
        db.MemberGroups.Remove(group);
        organizations.AddAudit(tenantId, actorAccountId, "member.group_deleted", null,
            JsonSerializer.Serialize(new { group.Name }), null);
        // A member put in the group meanwhile trips the foreign key.
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { throw new TenantMemberException(TenantMemberFailure.GroupInUse); }
    }

    /// <summary>Puts any member, the caller included, in a group or in none.</summary>
    public async Task SetGroupAsync(Guid tenantId, Guid actorAccountId, long actorSessionVersion,
        Guid accountId, SetTenantMemberGroupRequest request, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(tenantId, actorAccountId, actorSessionVersion, cancellationToken);
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == accountId,
            cancellationToken) ?? throw new TenantMemberException(TenantMemberFailure.NotFound);
        var group = request.GroupId is { } groupId ? await FindGroupAsync(groupId, cancellationToken) : null;
        if (membership.MemberGroupId == group?.Id) return;
        var account = await db.Accounts.SingleAsync(x => x.Id == accountId, cancellationToken);
        var before = await db.MemberGroups.Where(x => x.Id == membership.MemberGroupId)
            .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken);
        membership.MemberGroupId = group?.Id;
        organizations.AddAudit(tenantId, actorAccountId, "member.group_changed", null,
            JsonSerializer.Serialize(new { account.Email, Group = before }),
            JsonSerializer.Serialize(new { account.Email, Group = group?.Name }));
        // The group may have been deleted meanwhile.
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { throw new TenantMemberException(TenantMemberFailure.NotFound); }
    }

    public async Task<IReadOnlyList<TenantInvitationEntry>> ListInvitationsAsync(Guid tenantId,
        Guid actorAccountId, long actorSessionVersion, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(tenantId, actorAccountId, actorSessionVersion, cancellationToken);
        var now = DateTime.UtcNow;
        var invitations = await db.Invitations.AsNoTracking()
            .Where(x => x.AcceptedAtUtc == null && x.ExpiresAtUtc > now)
            .GroupJoin(db.Accounts.AsNoTracking(), invitation => invitation.InvitedByAccountId,
                account => account.Id, (invitation, inviters) => new { invitation, inviters })
            .SelectMany(x => x.inviters.DefaultIfEmpty(), (x, inviter) => new
            {
                x.invitation.Id, x.invitation.Email, x.invitation.Role, InvitedByEmail = inviter == null ? null : inviter.Email,
                x.invitation.CreatedAtUtc, x.invitation.ExpiresAtUtc
            })
            .ToListAsync(cancellationToken);
        return invitations.OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new TenantInvitationEntry(x.Id, x.Email, x.Role.ToString(), x.InvitedByEmail,
                x.CreatedAtUtc, x.ExpiresAtUtc)).ToArray();
    }

    /// <summary>Replaces a pending invitation with a new code for the same email and role. The
    /// caller emails the returned code.</summary>
    public async Task<(string Email, IssueInvitationResponse Invitation)> ResendInvitationAsync(
        Guid tenantId, Guid actorAccountId, long actorSessionVersion, Guid invitationId,
        CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(tenantId, actorAccountId, actorSessionVersion, cancellationToken);
        var now = DateTime.UtcNow;
        var current = await PendingInvitationAsync(invitationId, now, cancellationToken);
        var (invitation, code) = await onboarding.ReplaceInvitationAsync(tenantId, current.Email,
            current.Role, actorAccountId, now, cancellationToken);
        organizations.AddAudit(tenantId, actorAccountId, "member.invitation_resent", null, null,
            JsonSerializer.Serialize(new { current.Email, Role = current.Role.ToString() }), now);
        await db.SaveChangesAsync(cancellationToken);
        return (current.Email, new IssueInvitationResponse(invitation.Id, tenantId, invitation.ExpiresAtUtc, code));
    }

    public async Task RevokeInvitationAsync(Guid tenantId, Guid actorAccountId,
        long actorSessionVersion, Guid invitationId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(tenantId, actorAccountId, actorSessionVersion, cancellationToken);
        var now = DateTime.UtcNow;
        var invitation = await PendingInvitationAsync(invitationId, now, cancellationToken);
        invitation.ExpiresAtUtc = now;
        organizations.AddAudit(tenantId, actorAccountId, "member.invitation_revoked", null,
            JsonSerializer.Serialize(new { invitation.Email, Role = invitation.Role.ToString() }), null, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangeRoleAsync(Guid tenantId, Guid actorAccountId, long actorSessionVersion,
        Guid accountId, ChangeTenantMemberRoleRequest request, CancellationToken cancellationToken)
    {
        TenantRole? requested = request.Role?.Trim().ToLowerInvariant() switch
        {
            "employee" => TenantRole.Employee,
            "tenantadmin" => TenantRole.TenantAdmin,
            _ => null
        };
        if (requested is not { } role) throw new TenantMemberException(TenantMemberFailure.InvalidInput);
        await EnsureAdminAsync(tenantId, actorAccountId, actorSessionVersion, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (account, membership) = await OtherMemberAsync(actorAccountId, accountId, cancellationToken);
        if (membership.Status == MembershipStatus.Deactivated)
            throw new TenantMemberException(TenantMemberFailure.Conflict);
        if (membership.Role == role) return;
        if (role == TenantRole.Employee)
            await EnsureAnotherActiveAdminAsync(membership, cancellationToken);

        var before = Snapshot(account, membership);
        membership.Role = role;
        organizations.AddAudit(tenantId, actorAccountId, "member.role_changed", null, before,
            Snapshot(account, membership));
        await organizations.EndSessionsAsync(account, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Deactivates a member, or reactivates one: back to Active when the account has a
    /// password, otherwise back to PendingActivation with a new activation email.</summary>
    public async Task SetStatusAsync(Guid tenantId, Guid actorAccountId, long actorSessionVersion,
        Guid accountId, SetTenantMemberStatusRequest request, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(tenantId, actorAccountId, actorSessionVersion, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var (account, membership) = await OtherMemberAsync(actorAccountId, accountId, cancellationToken);
        var before = Snapshot(account, membership);
        string action;
        if (!request.Active)
        {
            if (membership.Status == MembershipStatus.Deactivated) return;
            await EnsureAnotherActiveAdminAsync(membership, cancellationToken);
            membership.Status = MembershipStatus.Deactivated;
            action = "member.deactivated";
        }
        else
        {
            if (membership.Status != MembershipStatus.Deactivated) return;
            membership.Status = account.PasswordHash is null
                ? MembershipStatus.PendingActivation : MembershipStatus.Active;
            action = "member.reactivated";
        }

        organizations.AddAudit(tenantId, actorAccountId, action, null, before, Snapshot(account, membership));
        await organizations.EndSessionsAsync(account, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (membership.Status == MembershipStatus.PendingActivation)
            await SendActivationAsync(tenantId, account.Email, cancellationToken);
    }

    public async Task ResendActivationAsync(Guid tenantId, Guid actorAccountId,
        long actorSessionVersion, Guid accountId, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(tenantId, actorAccountId, actorSessionVersion, cancellationToken);
        var (account, membership) = await OtherMemberAsync(actorAccountId, accountId, cancellationToken);
        if (membership.Status != MembershipStatus.PendingActivation)
            throw new TenantMemberException(TenantMemberFailure.Conflict);
        await SendActivationAsync(tenantId, account.Email, cancellationToken);
        var state = Snapshot(account, membership);
        organizations.AddAudit(tenantId, actorAccountId, "member.activation_resent", null, state, state);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureAdminAsync(Guid tenantId, Guid actorAccountId, long sessionVersion,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || tenantId == PlatformAdminBootstrap.PlatformTenantId ||
            !await TenantAdministrators.IsActiveAdminAsync(db, tenantScope, tenantId, actorAccountId,
                sessionVersion, cancellationToken))
            throw new TenantMemberException(TenantMemberFailure.Unauthorized);
    }

    private static string GroupName(SaveTenantMemberGroupRequest request) =>
        request.Name?.Trim() is { Length: > 0 and <= 100 } name
            ? name : throw new TenantMemberException(TenantMemberFailure.InvalidInput);

    private async Task<MemberGroup> FindGroupAsync(Guid groupId, CancellationToken cancellationToken) =>
        await db.MemberGroups.SingleOrDefaultAsync(x => x.Id == groupId, cancellationToken)
        ?? throw new TenantMemberException(TenantMemberFailure.NotFound);

    /// <summary>Group names are unique in the organization ignoring case, so "Outsource" and
    /// "outsource" cannot both exist.</summary>
    private async Task EnsureGroupNameFreeAsync(string name, Guid? exceptGroupId,
        CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();
        if (await db.MemberGroups.AnyAsync(x => x.Id != exceptGroupId && x.Name.ToLower() == lowered,
                cancellationToken))
            throw new TenantMemberException(TenantMemberFailure.GroupNameTaken);
    }

    /// <summary>Saves a new or renamed group; the unique index catches a concurrent duplicate.</summary>
    private async Task SaveGroupAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { throw new TenantMemberException(TenantMemberFailure.GroupNameTaken); }
    }

    private async Task<Invitation> PendingInvitationAsync(Guid invitationId, DateTime now,
        CancellationToken cancellationToken) =>
        await db.Invitations.SingleOrDefaultAsync(x => x.Id == invitationId &&
            x.AcceptedAtUtc == null && x.ExpiresAtUtc > now, cancellationToken)
        ?? throw new TenantMemberException(TenantMemberFailure.NotFound);

    /// <summary>Finds a member of the scoped tenant other than the actor.</summary>
    private async Task<(Account Account, Membership Membership)> OtherMemberAsync(Guid actorAccountId,
        Guid accountId, CancellationToken cancellationToken)
    {
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == accountId,
            cancellationToken) ?? throw new TenantMemberException(TenantMemberFailure.NotFound);
        if (accountId == actorAccountId) throw new TenantMemberException(TenantMemberFailure.SelfChange);
        var account = await db.Accounts.SingleAsync(x => x.Id == accountId, cancellationToken);
        return (account, membership);
    }

    /// <summary>Refuses to demote or deactivate the organization's last active TenantAdmin;
    /// pending administrators do not count.</summary>
    private async Task EnsureAnotherActiveAdminAsync(Membership membership,
        CancellationToken cancellationToken)
    {
        if (membership.Role == TenantRole.TenantAdmin && membership.Status == MembershipStatus.Active &&
            !await db.Memberships.AnyAsync(x => x.Id != membership.Id && x.Role == TenantRole.TenantAdmin &&
                x.Status == MembershipStatus.Active, cancellationToken))
            throw new TenantMemberException(TenantMemberFailure.LastAdministrator);
    }

    private async Task SendActivationAsync(Guid tenantId, string email, CancellationToken cancellationToken)
    {
        if (authentication is null) throw new TenantMemberException(TenantMemberFailure.Unavailable);
        try
        {
            await authentication.RequestActivationAsync(new RequestCredentialEmail(tenantId, email),
                cancellationToken);
        }
        catch (AuthenticationException exception) when (exception.Failure is
            AuthenticationFailure.Unavailable or AuthenticationFailure.RateLimited)
        {
            throw new TenantMemberException(exception.Failure == AuthenticationFailure.RateLimited
                ? TenantMemberFailure.RateLimited : TenantMemberFailure.Unavailable);
        }
    }

    private static string Snapshot(Account account, Membership membership) =>
        JsonSerializer.Serialize(new
        {
            account.Email, Role = membership.Role.ToString(), Status = membership.Status.ToString()
        });
}

public enum TenantMemberFailure
{
    Unauthorized, NotFound, InvalidInput, Conflict, LastAdministrator, SelfChange, Unavailable, RateLimited,
    GroupNameTaken, GroupInUse
}

public sealed class TenantMemberException(TenantMemberFailure failure) : Exception(failure.ToString())
{
    public TenantMemberFailure Failure { get; } = failure;
}
