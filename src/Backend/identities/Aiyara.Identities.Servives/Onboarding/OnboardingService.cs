using System.Net.Mail;
using System.Text.RegularExpressions;
using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Timesheet.Contracts.Onboarding.V1;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Onboarding;

public sealed class OnboardingService(IdentityDbContext db, TenantScope tenantScope)
{
    public async Task<CreateTenantResponse> CreateTenantAsync(
        CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var slug = request.Slug?.Trim().ToLowerInvariant();
        var email = NormalizeEmail(request.AdminEmail);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 ||
            slug is null || slug == "platform-system" ||
            !Regex.IsMatch(slug, "^[a-z0-9][a-z0-9-]{2,79}$"))
        {
            throw new OnboardingException(OnboardingFailure.InvalidInput, "Invalid tenant details.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.Tenants.IgnoreQueryFilters().AnyAsync(x => x.Slug == slug, cancellationToken))
        {
            throw new OnboardingException(OnboardingFailure.Conflict, "Tenant slug is already in use.");
        }

        var now = DateTime.UtcNow;
        var account = await FindOrCreateAccountAsync(email, now, cancellationToken);
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Name = name, Slug = slug, CreatedAtUtc = now
        };
        var membership = new Membership
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, AccountId = account.Id,
            Role = TenantRole.TenantAdmin, Status = MembershipStatus.PendingActivation,
            CreatedAtUtc = now
        };
        var onboardingKey = InvitationCode.Generate();
        var onboardingKeyExpiresAtUtc = now.AddDays(7);
        var capability = new OnboardingCapability
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, AccountId = account.Id,
            KeyHash = InvitationCode.Hash(onboardingKey), CreatedAtUtc = now,
            ExpiresAtUtc = onboardingKeyExpiresAtUtc
        };

        db.Tenants.Add(tenant);
        db.Memberships.Add(membership);
        db.OnboardingCapabilities.Add(capability);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CreateTenantResponse(tenant.Id, account.Id, membership.Id,
            membership.Role.ToString(), membership.Status.ToString(), tenant.TimeZoneId,
            onboardingKey, onboardingKeyExpiresAtUtc);
    }

    public async Task<AcceptInvitationResponse> AcceptInvitationAsync(
        AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 200)
        {
            throw new OnboardingException(OnboardingFailure.InvalidInvitation, "Invalid invitation.");
        }

        var tokenHash = InvitationCode.Hash(request.Code);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var invitation = await db.Invitations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (invitation is null || invitation.AcceptedAtUtc is not null ||
            invitation.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new OnboardingException(OnboardingFailure.InvalidInvitation, "Invalid invitation.");
        }

        tenantScope.TenantId = invitation.TenantId;
        var now = DateTime.UtcNow;
        var account = await FindOrCreateAccountAsync(invitation.Email, now, cancellationToken);
        if (await db.Memberships.AnyAsync(x => x.AccountId == account.Id, cancellationToken))
        {
            throw new OnboardingException(OnboardingFailure.Conflict, "Account is already a tenant member.");
        }

        var membership = new Membership
        {
            Id = Guid.NewGuid(), TenantId = invitation.TenantId, AccountId = account.Id,
            Role = invitation.Role, Status = MembershipStatus.PendingActivation,
            CreatedAtUtc = now
        };
        db.Memberships.Add(membership);
        invitation.AcceptedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AcceptInvitationResponse(invitation.TenantId, account.Id, membership.Id,
            membership.Role.ToString(), membership.Status.ToString());
    }

    public async Task<IssueInvitationResponse> IssueInvitationAsync(
        Guid tenantId, Guid actorAccountId, IssueInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (!Enum.TryParse<TenantRole>(request.Role, ignoreCase: true, out var role) ||
            role is not (TenantRole.Employee or TenantRole.TenantAdmin))
        {
            throw new OnboardingException(OnboardingFailure.InvalidInput, "Invalid tenant role.");
        }

        tenantScope.TenantId = tenantId;
        var actor = await db.Memberships.SingleOrDefaultAsync(
            x => x.AccountId == actorAccountId && x.Role == TenantRole.TenantAdmin,
            cancellationToken);
        if (actor is null)
        {
            throw new OnboardingException(OnboardingFailure.InvalidInput, "Tenant administrator is required.");
        }

        var existingAccount = await db.Accounts.SingleOrDefaultAsync(x => x.Email == email,
            cancellationToken);
        if (existingAccount is not null && await db.Memberships.AnyAsync(
            x => x.AccountId == existingAccount.Id, cancellationToken))
        {
            throw new OnboardingException(OnboardingFailure.Conflict, "Account is already a tenant member.");
        }

        var code = InvitationCode.Generate();
        var invitation = new Invitation
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Email = email,
            TokenHash = InvitationCode.Hash(code), Role = role,
            InvitedByAccountId = actorAccountId,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);
        return new IssueInvitationResponse(invitation.Id, tenantId, invitation.ExpiresAtUtc, code);
    }

    public async Task<IssueInvitationResponse> IssueInvitationWithKeyAsync(
        Guid tenantId, string? onboardingKey, IssueInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(onboardingKey) ||
            onboardingKey.Length > 200)
        {
            throw new OnboardingException(OnboardingFailure.Unauthorized,
                "Valid onboarding key is required.");
        }

        var keyHash = InvitationCode.Hash(onboardingKey);
        var capability = await db.OnboardingCapabilities.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.KeyHash == keyHash && x.TenantId == tenantId,
                cancellationToken);
        if (capability is null || capability.RevokedAtUtc is not null ||
            capability.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new OnboardingException(OnboardingFailure.Unauthorized,
                "Valid onboarding key is required.");
        }

        tenantScope.TenantId = tenantId;
        var membership = await db.Memberships.SingleOrDefaultAsync(x =>
            x.AccountId == capability.AccountId && x.Role == TenantRole.TenantAdmin,
            cancellationToken);
        if (membership?.Status != MembershipStatus.PendingActivation)
            throw new OnboardingException(OnboardingFailure.Unauthorized,
                "Valid onboarding key is required.");

        return await IssueInvitationAsync(tenantId, capability.AccountId, request,
            cancellationToken);
    }

    public async Task<IssueInvitationResponse> IssueInvitationAuthorizedAsync(
        Guid tenantId, Guid actorAccountId, long sessionVersion,
        IssueInvitationRequest request, CancellationToken cancellationToken)
    {
        tenantScope.TenantId = tenantId;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == actorAccountId,
            cancellationToken);
        var membership = await db.Memberships.SingleOrDefaultAsync(x =>
            x.AccountId == actorAccountId && x.Role == TenantRole.TenantAdmin &&
            x.Status == MembershipStatus.Active, cancellationToken);
        if (account is null || account.SessionVersion != sessionVersion ||
            membership is null)
            throw new OnboardingException(OnboardingFailure.Unauthorized,
                "Active tenant administrator is required.");
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        if (account.MustChangePassword || membership.MustChangePassword ||
            account.PasswordChangedAtUtc is null ||
            account.PasswordChangedAtUtc < tenant.PasswordPolicyUpdatedAtUtc ||
            account.PasswordChangedAtUtc < DateTime.UtcNow.AddDays(-tenant.PasswordExpiryDays))
            throw new OnboardingException(OnboardingFailure.Unauthorized,
                "Password change is required.");
        return await IssueInvitationAsync(tenantId, actorAccountId, request,
            cancellationToken);
    }

    private async Task<Account> FindOrCreateAccountAsync(string email, DateTime now,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (account is not null) return account;

        account = new Account { Id = Guid.NewGuid(), Email = email, CreatedAtUtc = now };
        db.Accounts.Add(account);
        return account;
    }

    private static string NormalizeEmail(string? email)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 320 ||
            !MailAddress.TryCreate(normalized, out var parsed) || parsed.Address != normalized)
        {
            throw new OnboardingException(OnboardingFailure.InvalidInput, "Invalid email address.");
        }

        return normalized;
    }
}
