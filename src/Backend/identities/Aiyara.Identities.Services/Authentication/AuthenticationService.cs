using Aiyara.Identities.Databases;
using Aiyara.Identities.Models.Tenancy;
using Aiyara.Identities.Services.Onboarding;
using Aiyara.Timesheet.Component.Abstractions.Securities;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Authentication;

public sealed partial class AuthenticationService(
    IdentityDbContext db, TenantScope tenantScope, IEncryptionService passwords,
    IPasetoTokenService tokens, ICredentialNotificationSender notifications,
    ISessionRevocationPublisher revocations, ICredentialAttemptLimiter attempts,
    IMfaSecretProtector mfaSecrets, SessionDeviceContext? device = null)
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(14);
    private static readonly TimeSpan MfaEnrollmentLifetime = TimeSpan.FromMinutes(15);
    private const int MfaAttemptLimit = 5;
    private const int RecoveryCodeCount = 10;
    public const string MfaIssuer = "Aiyara Timesheet";
    public const string MfaEnrolledAction = "platform_admin.mfa_enrolled";
    public const string RecoveryCodeUsedAction = "platform_admin.recovery_code_used";
    public const string MemberMfaEnrolledAction = "member.mfa_enrolled";
    public const string MemberRecoveryCodeUsedAction = "member.mfa_recovery_code_used";
    public const string MemberMfaLockedAction = "member.mfa_locked";

    public async Task RequestActivationAsync(RequestCredentialEmail request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || !ValidEmail(request.Email)) return;
        tenantScope.TenantId = request.TenantId;
        var tenant = await db.Tenants.SingleOrDefaultAsync(cancellationToken);
        if (tenant is null || tenant.Status != TenantStatus.Active) return;
        var email = request.Email.Trim().ToLowerInvariant();
        await attempts.CheckAsync(request.TenantId, email,
            CredentialChallengePurpose.Activation.ToString(), cancellationToken);
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Email == email,
            cancellationToken);
        if (account is null) return;
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == account.Id,
            cancellationToken);
        if (membership is null || membership.Status != MembershipStatus.PendingActivation) return;
        await IssueChallengeAsync(account, request.TenantId, [tenant],
            CredentialChallengePurpose.Activation, cancellationToken);
    }

    // The password belongs to the account, so a reset is requested by email alone (attempts are
    // counted per email, like login) and the code is issued in the account's oldest active
    // organization. The new password must satisfy every active organization's policy.
    public async Task RequestPasswordResetAsync(PasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        if (!ValidEmail(request.Email)) return;
        var email = request.Email.Trim().ToLowerInvariant();
        await attempts.CheckAsync(Guid.Empty, email,
            CredentialChallengePurpose.PasswordReset.ToString(), cancellationToken);
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Email == email,
            cancellationToken);
        if (account is null) return;
        var tenants = await ActivePolicyTenantsAsync(account.Id, cancellationToken);
        if (tenants.Count == 0) return;
        tenantScope.TenantId = tenants[0].Id;
        await IssueChallengeAsync(account, tenants[0].Id, tenants,
            CredentialChallengePurpose.PasswordReset, cancellationToken);
    }

    private async Task<List<Tenant>> ActivePolicyTenantsAsync(Guid accountId,
        CancellationToken cancellationToken) =>
        (await db.Memberships.IgnoreQueryFilters()
            .Where(x => x.AccountId == accountId && x.Status == MembershipStatus.Active)
            .Join(db.Tenants.IgnoreQueryFilters().Where(x => x.Status == TenantStatus.Active),
                membership => membership.TenantId, tenant => tenant.Id,
                (membership, tenant) => new { membership.CreatedAtUtc, Tenant = tenant })
            .ToListAsync(cancellationToken))
        .OrderBy(x => x.CreatedAtUtc).Select(x => x.Tenant).ToList();

    private async Task IssueChallengeAsync(Account account, Guid tenantId,
        IReadOnlyList<Tenant> policies, CredentialChallengePurpose purpose,
        CancellationToken cancellationToken)
    {
        foreach (var policy in policies) PasswordPolicy.ValidateSettings(policy);
        var now = DateTime.UtcNow;
        // An account has one password, so a new reset code replaces every outstanding one;
        // activation codes stay per organization.
        var pending = purpose == CredentialChallengePurpose.PasswordReset
            ? db.CredentialChallenges.IgnoreQueryFilters()
            : db.CredentialChallenges;
        var old = await pending.Where(x => x.AccountId == account.Id &&
            x.Purpose == purpose && x.UsedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var challenge in old) challenge.UsedAtUtc = now;
        var code = passwords.GenerateTemporaryPassword(
            Math.Max(16, policies.Max(x => x.PasswordMinimumLength)),
            policies.Any(x => x.PasswordRequireLowercase),
            policies.Any(x => x.PasswordRequireUppercase),
            policies.Any(x => x.PasswordRequireDigit),
            policies.Any(x => x.PasswordRequireSymbol));
        if (!policies.All(x => PasswordPolicy.IsSatisfied(x, code)))
            throw new AuthenticationException(AuthenticationFailure.Unavailable,
                "Credential generation is unavailable.");
        db.CredentialChallenges.Add(new CredentialChallenge
        {
            Id = IdentityGuid.New(), TenantId = tenantId, AccountId = account.Id,
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
        var tenant = await db.Tenants.SingleOrDefaultAsync(cancellationToken);
        if (tenant is null || tenant.Status != TenantStatus.Active) throw InvalidChallenge();
        var policies = purpose == CredentialChallengePurpose.PasswordReset
            ? await ActivePolicyTenantsAsync(challenge.AccountId, cancellationToken)
            : [tenant];
        foreach (var policy in policies) PasswordPolicy.ValidateSettings(policy);
        if (!policies.All(x => PasswordPolicy.IsSatisfied(x, request.Password)))
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
        // The reset password met every active organization's policy, so none still needs a change.
        if (purpose == CredentialChallengePurpose.PasswordReset)
            await db.Memberships.IgnoreQueryFilters().Where(x => x.AccountId == account.Id &&
                x.Status == MembershipStatus.Active && x.MustChangePassword)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.MustChangePassword, false),
                    cancellationToken);
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
        var tenantId = request.TenantId is { } requestedTenantId && requestedTenantId != Guid.Empty
            ? requestedTenantId
            : await ResolveOrganizationCodeAsync(request.OrganizationCode, cancellationToken);
        return await LoginToTenantAsync(tenantId, request.Email, request.Password,
            requiredRole: null, new MemberSecondFactor(request.TotpCode, request.RecoveryCode),
            cancellationToken);
    }

    /// <summary>Signs a Platform Admin in with password and second factor. An admin without an
    /// authenticator is offered an enrollment secret first; the first valid code confirms it,
    /// and the response then carries one-time recovery codes. Five wrong second factors lock the
    /// account for fifteen minutes.</summary>
    public async Task<PlatformAdminLoginResponse> LoginPlatformAdminAsync(
        PlatformAdminLoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var account = await VerifyCredentialsAsync(request.Email, request.Password, cancellationToken);
            tenantScope.TenantId = PlatformAdminBootstrap.PlatformTenantId;
            if (!await db.Memberships.AnyAsync(x => x.AccountId == account.Id &&
                    x.Role == TenantRole.PlatformAdmin && x.Status == MembershipStatus.Active, cancellationToken))
                throw InvalidCredentials();

            IReadOnlyList<string> recoveryCodes = [];
            if (account.MfaSecretProtected is null)
            {
                if (string.IsNullOrWhiteSpace(request.TotpCode))
                    return new PlatformAdminLoginResponse(null, await BeginMfaEnrollmentAsync(account,
                        cancellationToken), []);
                recoveryCodes = await CompleteMfaEnrollmentAsync(account, request.TotpCode,
                    PlatformAdminBootstrap.PlatformTenantId, MfaEnrolledAction, cancellationToken);
            }
            else await VerifySecondFactorAsync(account, request.TotpCode, request.RecoveryCode,
                PlatformAdminBootstrap.PlatformTenantId, RecoveryCodeUsedAction, cancellationToken);

            account.MfaFailedCount = 0;
            var session = await StartSessionAsync(PlatformAdminBootstrap.PlatformTenantId, account,
                TenantRole.PlatformAdmin, cancellationToken, secondFactorVerified: true);
            return new PlatformAdminLoginResponse(session, null, recoveryCodes);
        }
        catch (AuthenticationException exception) when
            (exception.Failure is AuthenticationFailure.InvalidCredentials or AuthenticationFailure.MfaInvalid)
        {
            await AuditFailedPlatformSignInAsync(request.Email, cancellationToken);
            throw;
        }
        // A stored secret that cannot be decrypted means Mfa:EncryptionKey changed; that is a
        // configuration fault, not a wrong code, so it must not count toward the lockout.
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new AuthenticationException(AuthenticationFailure.Unavailable,
                "The second factor cannot be verified.");
        }
    }

    private async Task<MfaEnrollment> BeginMfaEnrollmentAsync(Account account,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        string secret;
        // Keep offering the same secret for a while so a mistyped first code does not force a rescan.
        if (account.MfaPendingSecretProtected is not null && account.MfaPendingCreatedAtUtc > now - MfaEnrollmentLifetime)
            secret = mfaSecrets.Unprotect(account.MfaPendingSecretProtected);
        else
        {
            secret = Totp.GenerateSecret();
            account.MfaPendingSecretProtected = mfaSecrets.Protect(secret);
            account.MfaPendingCreatedAtUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        return new MfaEnrollment(secret, Totp.ProvisioningUri(MfaIssuer, account.Email, secret));
    }

    private async Task<IReadOnlyList<string>> CompleteMfaEnrollmentAsync(Account account, string code,
        Guid auditTenantId, string auditAction, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (account.MfaPendingSecretProtected is null || account.MfaPendingCreatedAtUtc <= now - MfaEnrollmentLifetime ||
            !Totp.TryVerify(mfaSecrets.Unprotect(account.MfaPendingSecretProtected), code, now, 0, out var step))
            throw await MfaFailureAsync(account, auditTenantId, cancellationToken);

        account.MfaSecretProtected = account.MfaPendingSecretProtected;
        account.MfaPendingSecretProtected = null;
        account.MfaPendingCreatedAtUtc = null;
        account.MfaEnabledAtUtc = now;
        account.MfaLastUsedStep = step;
        var codes = await ReplaceRecoveryCodesAsync(account.Id, now, cancellationToken);
        AddMfaAudit(auditTenantId, auditAction, account.Id, account.Email);
        return codes;
    }

    /// <summary>Checks one authenticator code, or one unused recovery code, against an enrolled
    /// account. The tenant and action say where a spent recovery code is audited; wrong codes count
    /// toward the shared five-failure lockout.</summary>
    private async Task VerifySecondFactorAsync(Account account, string? totpCode, string? recoveryCode,
        Guid auditTenantId, string recoveryAuditAction, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(totpCode))
        {
            if (Totp.TryVerify(mfaSecrets.Unprotect(account.MfaSecretProtected!), totpCode, now,
                    account.MfaLastUsedStep, out var step))
            {
                account.MfaLastUsedStep = step;
                return;
            }
        }
        else if (!string.IsNullOrWhiteSpace(recoveryCode))
        {
            var hash = InvitationCode.Hash(NormalizeRecoveryCode(recoveryCode));
            // A conditional update, so two simultaneous sign-ins cannot both spend the same code.
            if (await db.MfaRecoveryCodes.Where(x => x.AccountId == account.Id && x.CodeHash == hash &&
                    x.UsedAtUtc == null)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.UsedAtUtc, now), cancellationToken) == 1)
            {
                AddMfaAudit(auditTenantId, recoveryAuditAction, account.Id, account.Email);
                return;
            }
        }
        else throw new AuthenticationException(AuthenticationFailure.MfaRequired, "A second factor is required.");

        throw await MfaFailureAsync(account, auditTenantId, cancellationToken);
    }

    private async Task<AuthenticationException> MfaFailureAsync(Account account, Guid auditTenantId,
        CancellationToken cancellationToken)
    {
        account.MfaFailedCount++;
        if (account.MfaFailedCount >= MfaAttemptLimit)
        {
            account.LockedUntilUtc = DateTime.UtcNow.AddMinutes(15);
            account.MfaFailedCount = 0;
            // Platform Admin failures are audited as failed sign-ins instead.
            if (auditTenantId != PlatformAdminBootstrap.PlatformTenantId)
                AddMfaAudit(auditTenantId, MemberMfaLockedAction, account.Id, account.Email);
        }
        await db.SaveChangesAsync(cancellationToken);
        return new AuthenticationException(AuthenticationFailure.MfaInvalid, "Invalid second factor.");
    }

    /// <summary>Replaces every recovery code of the account and returns the new plaintext codes,
    /// which are shown once and stored only as hashes.</summary>
    private async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(Guid accountId, DateTime now,
        CancellationToken cancellationToken)
    {
        await db.MfaRecoveryCodes.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        var codes = Enumerable.Range(0, RecoveryCodeCount).Select(_ => NewRecoveryCode()).ToArray();
        db.MfaRecoveryCodes.AddRange(codes.Select(code => new MfaRecoveryCode
        {
            Id = IdentityGuid.New(), AccountId = accountId,
            CodeHash = InvitationCode.Hash(NormalizeRecoveryCode(code)), CreatedAtUtc = now
        }));
        return codes;
    }

    private static string NewRecoveryCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var characters = Enumerable.Range(0, 10)
            .Select(_ => alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)])
            .ToArray();
        return $"{new string(characters, 0, 5)}-{new string(characters, 5, 5)}";
    }

    private static string NormalizeRecoveryCode(string code) =>
        new(code.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public const string PlatformSignInAction = "platform_admin.signed_in";
    public const string PlatformSignInFailedAction = "platform_admin.sign_in_failed";

    private async Task AuditFailedPlatformSignInAsync(string? email, CancellationToken cancellationToken)
    {
        // Record only well-formed addresses so arbitrary input never lands in the audit log.
        var normalized = ValidEmail(email) ? email!.Trim().ToLowerInvariant() : null;
        var accountId = normalized is null ? Guid.Empty : await db.Accounts
            .Where(x => x.Email == normalized).Select(x => x.Id).SingleOrDefaultAsync(cancellationToken);
        AddPlatformSignInAudit(PlatformSignInFailedAction, accountId, normalized);
        await db.SaveChangesAsync(cancellationToken);
    }

    private void AddPlatformSignInAudit(string action, Guid accountId, string? email) =>
        AddMfaAudit(PlatformAdminBootstrap.PlatformTenantId, action, accountId, email);

    /// <summary>Adds an audit row in the tenant's log; it holds the email only, never a secret or code.</summary>
    private void AddMfaAudit(Guid tenantId, string action, Guid accountId, string? email) =>
        db.PlatformOrganizationAudits.Add(new PlatformOrganizationAudit
        {
            Id = IdentityGuid.New(), TenantId = tenantId,
            ActorAccountId = accountId, Action = action,
            AfterJson = System.Text.Json.JsonSerializer.Serialize(new { Email = email }),
            CreatedAtUtc = DateTime.UtcNow
        });

    public async Task<SignInResponse> SignInAsync(SignInRequest request,
        CancellationToken cancellationToken)
    {
        var account = await VerifyCredentialsAsync(request.Email, request.Password,
            cancellationToken);
        var organizations = await db.Memberships.IgnoreQueryFilters()
            .Where(x => x.AccountId == account.Id && x.Status == MembershipStatus.Active)
            .Join(db.Tenants.IgnoreQueryFilters().Where(x => x.Status == TenantStatus.Active),
                membership => membership.TenantId, tenant => tenant.Id,
                (membership, tenant) => new { tenant.Id, tenant.OrganizationCode, tenant.Name })
            .OrderBy(x => x.Name).ToListAsync(cancellationToken);
        if (organizations.Count == 0) throw InvalidCredentials();
        if (organizations.Count > 1)
            return new SignInResponse(null, organizations
                .Select(x => new SignInOrganization(x.OrganizationCode, x.Name)).ToList());
        var session = await StartSessionAsync(organizations[0].Id, account, requiredRole: null,
            cancellationToken, secondFactor: new MemberSecondFactor(request.TotpCode, request.RecoveryCode));
        return new SignInResponse(session, []);
    }

    private async Task<AuthenticationResponse> LoginToTenantAsync(Guid tenantId, string email,
        string password, TenantRole? requiredRole, MemberSecondFactor secondFactor,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw InvalidCredentials();
        var account = await VerifyCredentialsAsync(email, password, cancellationToken);
        return await StartSessionAsync(tenantId, account, requiredRole, cancellationToken,
            secondFactor: secondFactor);
    }

    // Accounts are global (one per email), so attempts and lockout are counted per email
    // rather than per tenant; otherwise a password could be guessed once per organization.
    private async Task<Account> VerifyCredentialsAsync(string email, string password,
        CancellationToken cancellationToken)
    {
        if (!ValidEmail(email) || string.IsNullOrEmpty(password)) throw InvalidCredentials();
        email = email.Trim().ToLowerInvariant();
        await attempts.CheckAsync(Guid.Empty, email, "Login", cancellationToken);
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Email == email,
            cancellationToken);
        if (account is null || account.PasswordHash is null ||
            account.LockedUntilUtc > DateTime.UtcNow) throw InvalidCredentials();
        if (!passwords.VerifyPassword(password, account.PasswordHash))
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
        return account;
    }

    private async Task<AuthenticationResponse> StartSessionAsync(Guid tenantId, Account account,
        TenantRole? requiredRole, CancellationToken cancellationToken, bool secondFactorVerified = false,
        MemberSecondFactor? secondFactor = null)
    {
        tenantScope.TenantId = tenantId;
        var tenant = await db.Tenants.SingleOrDefaultAsync(cancellationToken);
        if (tenant is null || tenant.Status != TenantStatus.Active) throw InvalidCredentials();
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == account.Id &&
            x.Status == MembershipStatus.Active, cancellationToken);
        if (membership is null) throw InvalidCredentials();
        if (requiredRole is { } role && membership.Role != role) throw InvalidCredentials();
        // Every route that could issue a Platform Admin session ends here, so none can skip the
        // second factor; only the system administrator sign-in verifies one.
        if (membership.Role == TenantRole.PlatformAdmin && !secondFactorVerified)
            throw new AuthenticationException(AuthenticationFailure.MfaRequired, "A second factor is required.");
        // Members answer the second factor here too, before any token exists, so a member who must
        // enroll gets no session until the enrollment is confirmed.
        var recoveryCodes = membership.Role == TenantRole.PlatformAdmin ? null
            : await RequireMemberSecondFactorAsync(tenant, account, secondFactor, cancellationToken);
        var mustChange = RequiresPasswordChange(account, membership, tenant);
        // Audit every new Platform Admin session, whichever sign-in route issued it.
        if (membership.Role == TenantRole.PlatformAdmin)
            AddPlatformSignInAudit(PlatformSignInAction, account.Id, account.Email);
        var response = await CreateSessionAsync(account, membership, mustChange, cancellationToken);
        return recoveryCodes is null ? response : response with { RecoveryCodes = recoveryCodes };
    }

    /// <summary>The second factor a member's sign-in needs: an enrolled authenticator is always
    /// asked (the account is global, so it protects every organization it belongs to), and an
    /// organization that requires MFA also makes an un-enrolled member enroll. Returns the new
    /// recovery codes when this sign-in completed an enrollment, otherwise null.</summary>
    private async Task<IReadOnlyList<string>?> RequireMemberSecondFactorAsync(Tenant tenant, Account account,
        MemberSecondFactor? factor, CancellationToken cancellationToken)
    {
        var enrolled = account.MfaSecretProtected is not null;
        if (!enrolled && !tenant.RequireMfa) return null;
        try
        {
            if (enrolled)
            {
                await VerifySecondFactorAsync(account, factor?.TotpCode, factor?.RecoveryCode, tenant.Id,
                    MemberRecoveryCodeUsedAction, cancellationToken);
                account.MfaFailedCount = 0;
                return null;
            }
            if (string.IsNullOrWhiteSpace(factor?.TotpCode))
                throw new AuthenticationException(AuthenticationFailure.MfaEnrollmentRequired,
                    "This organization requires an authenticator.");
            var codes = await CompleteMfaEnrollmentAsync(account, factor.TotpCode, tenant.Id,
                MemberMfaEnrolledAction, cancellationToken);
            account.MfaFailedCount = 0;
            return codes;
        }
        // A secret that cannot be decrypted means Mfa:EncryptionKey changed: a configuration fault,
        // not a wrong code, so it must not count toward the lockout.
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new AuthenticationException(AuthenticationFailure.Unavailable,
                "The second factor cannot be verified.");
        }
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
        var tenant = await db.Tenants.SingleOrDefaultAsync(cancellationToken);
        if (tenant is null || tenant.Status != TenantStatus.Active) throw InvalidCredentials();
        var account = await db.Accounts.SingleAsync(x => x.Id == session.AccountId,
            cancellationToken);
        if (session.SessionVersion != account.SessionVersion)
            throw InvalidCredentials();
        // Revoked and never rotated: the member (or a policy change) ended it. That is not a stolen
        // token being replayed, so it fails alone instead of signing the account out everywhere.
        if (session.RevokedAtUtc is not null && session.ReplacedById is null) throw InvalidCredentials();
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
        // A session that predates the requirement or an MFA reset cannot be renewed without a sign-in.
        if (tenant.RequireMfa && membership.Role != TenantRole.PlatformAdmin &&
            account.MfaSecretProtected is null) throw InvalidCredentials();
        var rotated = await db.RefreshSessions.Where(x => x.Id == session.Id &&
            x.RevokedAtUtc == null).ExecuteUpdateAsync(x => x.SetProperty(
                s => s.RevokedAtUtc, DateTime.UtcNow), cancellationToken);
        if (rotated != 1) throw InvalidCredentials();
        var mustChange = RequiresPasswordChange(account, membership, tenant);
        var response = await CreateSessionAsync(account, membership, mustChange, cancellationToken,
            session.SignedInAtUtc ?? session.CreatedAtUtc, session.UserAgent);
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
        var tenant = await db.Tenants.SingleOrDefaultAsync(cancellationToken);
        if (tenant is null || tenant.Status != TenantStatus.Active) throw InvalidCredentials();
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == accountId,
            cancellationToken);
        var membership = await db.Memberships.SingleOrDefaultAsync(x =>
            x.AccountId == accountId && x.Status == MembershipStatus.Active,
            cancellationToken);
        if (account?.PasswordHash is null || membership is null ||
            account.SessionVersion != sessionVersion ||
            !passwords.VerifyPassword(request.CurrentPassword, account.PasswordHash))
            throw InvalidCredentials();
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
        var tenant = await db.Tenants.SingleOrDefaultAsync(cancellationToken);
        if (tenant is null || tenant.Status != TenantStatus.Active) throw InvalidCredentials();
        if (RequiresPasswordChange(actor, membership, tenant))
            throw InvalidCredentials();
        await ApplyPasswordPolicyAsync(tenant, request, transaction, beforeSave: null, cancellationToken);
    }

    /// <summary>Updates the platform tenant's password policy. The caller has already verified a
    /// Platform Admin; <paramref name="beforeSave"/> receives the old and new settings so the change
    /// is audited in the same transaction.</summary>
    public async Task UpdatePlatformPolicyAsync(TenantPasswordPolicyRequest request,
        Action<string, string> beforeSave, CancellationToken cancellationToken)
    {
        tenantScope.TenantId = PlatformAdminBootstrap.PlatformTenantId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var tenant = await db.Tenants.SingleOrDefaultAsync(cancellationToken)
            ?? throw InvalidCredentials();
        await ApplyPasswordPolicyAsync(tenant, request, transaction, beforeSave, cancellationToken);
    }

    /// <summary>Applies a policy inside <paramref name="transaction"/> and commits it. A stricter
    /// policy forces every active member to change password and ends their sessions.</summary>
    private async Task ApplyPasswordPolicyAsync(Tenant tenant, TenantPasswordPolicyRequest request,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        Action<string, string>? beforeSave, CancellationToken cancellationToken)
    {
        // Reject an invalid request before touching the tracked tenant.
        PasswordPolicy.ValidateSettings(request.MinimumLength, request.ExpiryDays);
        var tenantId = tenant.Id;
        var before = PolicySnapshot(tenant);
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
            var now = DateTime.UtcNow;
            tenant.PasswordPolicyUpdatedAtUtc = new DateTime(
                Math.Max(now.Ticks - now.Ticks % 10,
                    tenant.PasswordPolicyUpdatedAtUtc.Ticks + 10), DateTimeKind.Utc);
            await db.Memberships.Where(x => x.Status == MembershipStatus.Active)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.MustChangePassword, true),
                    cancellationToken);
            await db.RefreshSessions.Where(x => x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc,
                    DateTime.UtcNow), cancellationToken);
        }
        beforeSave?.Invoke(before, PolicySnapshot(tenant));
        await db.SaveChangesAsync(cancellationToken);
        if (stricter)
            await revocations.PublishTenantPolicyAsync(tenantId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (stricter)
            await revocations.CompleteTenantPolicyAsync(tenantId,
                tenant.PasswordPolicyUpdatedAtUtc.Ticks / 10, cancellationToken);
    }

    private static string PolicySnapshot(Tenant tenant) => System.Text.Json.JsonSerializer.Serialize(new
    {
        MinimumLength = tenant.PasswordMinimumLength, ExpiryDays = tenant.PasswordExpiryDays,
        RequireUppercase = tenant.PasswordRequireUppercase, RequireLowercase = tenant.PasswordRequireLowercase,
        RequireDigit = tenant.PasswordRequireDigit, RequireSymbol = tenant.PasswordRequireSymbol
    });

    private async Task<AuthenticationResponse> CreateSessionAsync(Account account,
        Membership membership, bool mustChange, CancellationToken cancellationToken,
        DateTime? signedInAtUtc = null, string? inheritedUserAgent = null)
    {
        var now = DateTime.UtcNow;
        var refresh = InvitationCode.Generate();
        var sessionId = IdentityGuid.New();
        db.RefreshSessions.Add(new RefreshSession
        {
            Id = sessionId, SignedInAtUtc = signedInAtUtc ?? now,
            UserAgent = device?.UserAgent ?? inheritedUserAgent, TenantId = membership.TenantId, AccountId = account.Id,
            TokenHash = InvitationCode.Hash(refresh), CreatedAtUtc = now,
            SessionVersion = account.SessionVersion,
            ExpiresAtUtc = now.Add(RefreshLifetime)
        });
        await db.SaveChangesAsync(cancellationToken);
        var access = tokens.GenerateToken(new PasetoTokenClaims
        {
            UserId = account.Id, TenantId = membership.TenantId,
            TokenId = IdentityGuid.New().ToString(), Email = account.Email,
            Role = membership.Role.ToString(), SessionVersion = account.SessionVersion,
            MustChangePassword = mustChange, SessionId = sessionId
        });
        return new AuthenticationResponse(access.Value, access.ExpiresAt, refresh,
            new DateTimeOffset(now.Add(RefreshLifetime), TimeSpan.Zero), mustChange,
            membership.TenantId, membership.Role.ToString());
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

    private async Task<Guid> ResolveOrganizationCodeAsync(string? organizationCode,
        CancellationToken cancellationToken)
    {
        if (organizationCode is not { Length: 7 } code ||
            code.Any(c => !char.IsAsciiLetterOrDigit(c))) return Guid.Empty;
        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationCode == code.ToUpperInvariant(),
                cancellationToken);
        return tenant?.Id ?? Guid.Empty;
    }

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
