using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Authentication;

public sealed class AuthenticationService(
    IdentityDbContext db, TenantScope tenantScope, IEncryptionService passwords,
    IPasetoTokenService tokens, ICredentialNotificationSender notifications,
    ISessionRevocationPublisher revocations, ICredentialAttemptLimiter attempts)
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(14);

    public Task RequestActivationAsync(RequestCredentialEmail request, CancellationToken cancellationToken)
        => RequestChallengeAsync(request, CredentialChallengePurpose.Activation, cancellationToken);

    public Task RequestPasswordResetAsync(RequestCredentialEmail request, CancellationToken cancellationToken)
        => RequestChallengeAsync(request, CredentialChallengePurpose.PasswordReset, cancellationToken);

    private async Task RequestChallengeAsync(RequestCredentialEmail request,
        CredentialChallengePurpose purpose, CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || !ValidEmail(request.Email)) return;
        tenantScope.TenantId = request.TenantId;
        var email = request.Email.Trim().ToLowerInvariant();
        await attempts.CheckAsync(request.TenantId, email, purpose.ToString(),
            cancellationToken);
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Email == email,
            cancellationToken);
        if (account is null) return;
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == account.Id,
            cancellationToken);
        if (membership is null || (purpose == CredentialChallengePurpose.Activation &&
            membership.Status != MembershipStatus.PendingActivation) ||
            (purpose == CredentialChallengePurpose.PasswordReset &&
            membership.Status != MembershipStatus.Active)) return;

        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        PasswordPolicy.ValidateSettings(tenant);
        var now = DateTime.UtcNow;
        var old = await db.CredentialChallenges.Where(x => x.AccountId == account.Id &&
            x.Purpose == purpose && x.UsedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var challenge in old) challenge.UsedAtUtc = now;
        var code = passwords.GenerateTemporaryPassword(
            Math.Max(16, tenant.PasswordMinimumLength),
            tenant.PasswordRequireLowercase, tenant.PasswordRequireUppercase,
            tenant.PasswordRequireDigit, tenant.PasswordRequireSymbol);
        if (!PasswordPolicy.IsSatisfied(tenant, code))
            throw new AuthenticationException(AuthenticationFailure.Unavailable,
                "Credential generation is unavailable.");
        db.CredentialChallenges.Add(new CredentialChallenge
        {
            Id = Guid.NewGuid(), TenantId = request.TenantId, AccountId = account.Id,
            TokenHash = InvitationCode.Hash(code), Purpose = purpose,
            CreatedAtUtc = now, ExpiresAtUtc = now.Add(ChallengeLifetime)
        });
        await db.SaveChangesAsync(cancellationToken);
        await notifications.SendAsync(account.Email,
            purpose == CredentialChallengePurpose.Activation ? "activation" : "password-reset",
            code, cancellationToken);
    }

    public Task CompleteActivationAsync(CompleteCredentialChallenge request,
        CancellationToken cancellationToken)
        => CompleteChallengeAsync(request, CredentialChallengePurpose.Activation, cancellationToken);

    public Task CompletePasswordResetAsync(CompleteCredentialChallenge request,
        CancellationToken cancellationToken)
        => CompleteChallengeAsync(request, CredentialChallengePurpose.PasswordReset, cancellationToken);

    private async Task CompleteChallengeAsync(CompleteCredentialChallenge request,
        CredentialChallengePurpose purpose, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 200)
            throw InvalidChallenge();
        var hash = InvitationCode.Hash(request.Code);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var challenge = await db.CredentialChallenges.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TokenHash == hash && x.Purpose == purpose,
                cancellationToken);
        if (challenge is null || challenge.UsedAtUtc is not null ||
            challenge.ExpiresAtUtc <= DateTime.UtcNow) throw InvalidChallenge();
        tenantScope.TenantId = challenge.TenantId;
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        PasswordPolicy.ValidateSettings(tenant);
        if (!PasswordPolicy.IsSatisfied(tenant, request.Password))
            throw new AuthenticationException(AuthenticationFailure.InvalidInput,
                "Password does not satisfy tenant policy.");
        var account = await db.Accounts.SingleAsync(x => x.Id == challenge.AccountId,
            cancellationToken);
        var membership = await db.Memberships.SingleAsync(x => x.AccountId == account.Id,
            cancellationToken);
        if (purpose == CredentialChallengePurpose.Activation &&
            membership.Status != MembershipStatus.PendingActivation) throw InvalidChallenge();
        if (purpose == CredentialChallengePurpose.PasswordReset &&
            membership.Status != MembershipStatus.Active) throw InvalidChallenge();
        var now = DateTime.UtcNow;
        account.PasswordHash = passwords.HashPassword(request.Password);
        account.PasswordChangedAtUtc = now;
        account.MustChangePassword = false;
        account.SessionVersion++;
        account.FailedLoginCount = 0;
        account.LockedUntilUtc = null;
        if (purpose == CredentialChallengePurpose.Activation)
        {
            membership.Status = MembershipStatus.Active;
            await db.OnboardingCapabilities.Where(x => x.AccountId == account.Id &&
                x.RevokedAtUtc == null).ExecuteUpdateAsync(x => x.SetProperty(
                    c => c.RevokedAtUtc, now), cancellationToken);
        }
        membership.MustChangePassword = false;
        challenge.UsedAtUtc = now;
        await db.RefreshSessions.Where(x => x.AccountId == account.Id && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc, now), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await PublishAccountAcrossTenantsAsync(account.Id, account.SessionVersion,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<AuthenticationResponse> LoginAsync(LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || !ValidEmail(request.Email) ||
            string.IsNullOrEmpty(request.Password)) throw InvalidCredentials();
        tenantScope.TenantId = request.TenantId;
        var email = request.Email.Trim().ToLowerInvariant();
        await attempts.CheckAsync(request.TenantId, email, "Login", cancellationToken);
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Email == email,
            cancellationToken);
        if (account is null || account.PasswordHash is null ||
            account.LockedUntilUtc > DateTime.UtcNow) throw InvalidCredentials();
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == account.Id &&
            x.Status == MembershipStatus.Active, cancellationToken);
        if (membership is null) throw InvalidCredentials();
        if (!passwords.VerifyPassword(request.Password, account.PasswordHash))
        {
            account.FailedLoginCount++;
            if (account.FailedLoginCount >= 5)
            {
                account.LockedUntilUtc = DateTime.UtcNow.AddMinutes(15);
                account.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync(cancellationToken);
            throw InvalidCredentials();
        }
        account.FailedLoginCount = 0;
        account.LockedUntilUtc = null;
        await db.SaveChangesAsync(cancellationToken);
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        var mustChange = RequiresPasswordChange(account, membership, tenant);
        return await CreateSessionAsync(account, membership, mustChange, cancellationToken);
    }

    public async Task<AuthenticationResponse> RefreshAsync(RefreshRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) ||
            request.RefreshToken.Length > 200) throw InvalidCredentials();
        var hash = InvitationCode.Hash(request.RefreshToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var session = await db.RefreshSessions.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (session is null || session.ExpiresAtUtc <= DateTime.UtcNow) throw InvalidCredentials();
        tenantScope.TenantId = session.TenantId;
        var account = await db.Accounts.SingleAsync(x => x.Id == session.AccountId,
            cancellationToken);
        if (session.SessionVersion != account.SessionVersion)
            throw InvalidCredentials();
        if (session.RevokedAtUtc is not null)
        {
            account.SessionVersion++;
            await db.RefreshSessions.Where(x => x.AccountId == account.Id && x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc, DateTime.UtcNow),
                    cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await PublishAccountAcrossTenantsAsync(account.Id, account.SessionVersion,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw InvalidCredentials();
        }
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == account.Id &&
            x.Status == MembershipStatus.Active, cancellationToken);
        if (membership is null) throw InvalidCredentials();
        var rotated = await db.RefreshSessions.Where(x => x.Id == session.Id &&
            x.RevokedAtUtc == null).ExecuteUpdateAsync(x => x.SetProperty(
                s => s.RevokedAtUtc, DateTime.UtcNow), cancellationToken);
        if (rotated != 1) throw InvalidCredentials();
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        var mustChange = RequiresPasswordChange(account, membership, tenant);
        var response = await CreateSessionAsync(account, membership, mustChange, cancellationToken);
        var replacementId = await db.RefreshSessions.Where(x => x.TokenHash ==
            InvitationCode.Hash(response.RefreshToken)).Select(x => x.Id)
            .SingleAsync(cancellationToken);
        await db.RefreshSessions.Where(x => x.Id == session.Id).ExecuteUpdateAsync(
            x => x.SetProperty(s => s.ReplacedById, replacementId), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) ||
            request.RefreshToken.Length > 200) return;
        var hash = InvitationCode.Hash(request.RefreshToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var session = await db.RefreshSessions.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (session is null || session.RevokedAtUtc is not null) return;
        tenantScope.TenantId = session.TenantId;
        await db.RefreshSessions.Where(x => x.Id == session.Id && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc, DateTime.UtcNow),
                cancellationToken);
        var account = await db.Accounts.SingleAsync(x => x.Id == session.AccountId,
            cancellationToken);
        account.SessionVersion++;
        await db.SaveChangesAsync(cancellationToken);
        await PublishAccountAcrossTenantsAsync(account.Id, account.SessionVersion,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ChangePasswordAsync(Guid tenantId, Guid accountId,
        long sessionVersion, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || accountId == Guid.Empty ||
            string.IsNullOrEmpty(request.CurrentPassword) ||
            string.IsNullOrEmpty(request.NewPassword)) throw InvalidCredentials();
        tenantScope.TenantId = tenantId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == accountId,
            cancellationToken);
        var membership = await db.Memberships.SingleOrDefaultAsync(x =>
            x.AccountId == accountId && x.Status == MembershipStatus.Active,
            cancellationToken);
        if (account?.PasswordHash is null || membership is null ||
            account.SessionVersion != sessionVersion ||
            !passwords.VerifyPassword(request.CurrentPassword, account.PasswordHash))
            throw InvalidCredentials();
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        PasswordPolicy.ValidateSettings(tenant);
        if (!PasswordPolicy.IsSatisfied(tenant, request.NewPassword) ||
            passwords.VerifyPassword(request.NewPassword, account.PasswordHash))
            throw new AuthenticationException(AuthenticationFailure.InvalidInput,
                "New password does not satisfy policy or matches the current password.");
        var now = DateTime.UtcNow;
        account.PasswordHash = passwords.HashPassword(request.NewPassword);
        account.PasswordChangedAtUtc = now;
        account.MustChangePassword = false;
        account.SessionVersion++;
        account.FailedLoginCount = 0;
        account.LockedUntilUtc = null;
        membership.MustChangePassword = false;
        await db.RefreshSessions.Where(x => x.AccountId == accountId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc, now), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await PublishAccountAcrossTenantsAsync(accountId, account.SessionVersion,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateTenantPolicyAsync(Guid tenantId, Guid actorAccountId,
        long actorSessionVersion, TenantPasswordPolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || actorAccountId == Guid.Empty)
            throw InvalidCredentials();
        tenantScope.TenantId = tenantId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var actor = await db.Accounts.SingleOrDefaultAsync(x => x.Id == actorAccountId,
            cancellationToken);
        var membership = await db.Memberships.SingleOrDefaultAsync(x =>
            x.AccountId == actorAccountId && x.Role == TenantRole.TenantAdmin &&
            x.Status == MembershipStatus.Active, cancellationToken);
        if (actor is null || actor.SessionVersion != actorSessionVersion || membership is null)
            throw InvalidCredentials();
        var tenant = await db.Tenants.SingleAsync(cancellationToken);
        if (RequiresPasswordChange(actor, membership, tenant))
            throw InvalidCredentials();
        var stricter = request.MinimumLength > tenant.PasswordMinimumLength ||
            request.ExpiryDays < tenant.PasswordExpiryDays ||
            (request.RequireUppercase && !tenant.PasswordRequireUppercase) ||
            (request.RequireLowercase && !tenant.PasswordRequireLowercase) ||
            (request.RequireDigit && !tenant.PasswordRequireDigit) ||
            (request.RequireSymbol && !tenant.PasswordRequireSymbol);
        tenant.PasswordMinimumLength = request.MinimumLength;
        tenant.PasswordExpiryDays = request.ExpiryDays;
        tenant.PasswordRequireUppercase = request.RequireUppercase;
        tenant.PasswordRequireLowercase = request.RequireLowercase;
        tenant.PasswordRequireDigit = request.RequireDigit;
        tenant.PasswordRequireSymbol = request.RequireSymbol;
        PasswordPolicy.ValidateSettings(tenant);
        if (stricter)
        {
            tenant.PasswordPolicyUpdatedAtUtc = DateTime.UtcNow;
            await db.Memberships.Where(x => x.Status == MembershipStatus.Active)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.MustChangePassword, true),
                    cancellationToken);
            await db.RefreshSessions.Where(x => x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc,
                    DateTime.UtcNow), cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        if (stricter)
            await revocations.PublishTenantPolicyAsync(tenantId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (stricter)
            await revocations.CompleteTenantPolicyAsync(tenantId, cancellationToken);
    }

    private async Task<AuthenticationResponse> CreateSessionAsync(Account account,
        Membership membership, bool mustChange, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var refresh = InvitationCode.Generate();
        db.RefreshSessions.Add(new RefreshSession
        {
            Id = Guid.NewGuid(), TenantId = membership.TenantId, AccountId = account.Id,
            TokenHash = InvitationCode.Hash(refresh), CreatedAtUtc = now,
            SessionVersion = account.SessionVersion,
            ExpiresAtUtc = now.Add(RefreshLifetime)
        });
        await db.SaveChangesAsync(cancellationToken);
        var access = tokens.GenerateToken(new PasetoTokenClaims
        {
            UserId = account.Id, TenantId = membership.TenantId,
            TokenId = Guid.NewGuid().ToString(), Email = account.Email,
            Role = membership.Role.ToString(), SessionVersion = account.SessionVersion,
            MustChangePassword = mustChange
        });
        return new AuthenticationResponse(access.Value, access.ExpiresAt, refresh,
            new DateTimeOffset(now.Add(RefreshLifetime), TimeSpan.Zero), mustChange);
    }

    private async Task PublishAccountAcrossTenantsAsync(Guid accountId, long sessionVersion,
        CancellationToken cancellationToken)
    {
        var tenantIds = await db.Memberships.IgnoreQueryFilters()
            .Where(x => x.AccountId == accountId).Select(x => x.TenantId)
            .ToListAsync(cancellationToken);
        foreach (var tenantId in tenantIds)
            await revocations.PublishAccountAsync(tenantId, accountId,
                sessionVersion, cancellationToken);
    }

    private static AuthenticationException InvalidCredentials() =>
        new(AuthenticationFailure.InvalidCredentials, "Invalid credentials.");

    private static bool RequiresPasswordChange(Account account, Membership membership,
        Tenant tenant) => account.MustChangePassword || membership.MustChangePassword ||
        account.PasswordChangedAtUtc is null ||
        account.PasswordChangedAtUtc < tenant.PasswordPolicyUpdatedAtUtc ||
        account.PasswordChangedAtUtc < DateTime.UtcNow.AddDays(-tenant.PasswordExpiryDays);

    private static AuthenticationException InvalidChallenge() =>
        new(AuthenticationFailure.InvalidCredentials, "Invalid or expired challenge.");

    private static bool ValidEmail(string? email) => email is { Length: > 3 and <= 320 } &&
        System.Net.Mail.MailAddress.TryCreate(email.Trim(), out var parsed) &&
        parsed.Address.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase);
}
