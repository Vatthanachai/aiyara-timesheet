using Aiyara.Identities.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Identities.Services.Authentication;

/// <summary>The second-factor input of a member's sign-in: an authenticator code, or a recovery code.</summary>
internal sealed record MemberSecondFactor(string? TotpCode, string? RecoveryCode);

/// <summary>Member MFA: the same TOTP secret, encryption, recovery codes, replay guard and
/// five-failure lockout as the Platform Admin flow. Enrollment is either forced by an organization
/// that requires MFA (the sign-in carries the first code, see <c>RequireMemberSecondFactorAsync</c>)
/// or chosen by the member through the signed-in methods below.</summary>
public sealed partial class AuthenticationService
{
    private const string RecoveryCodesRegeneratedAction = "member.mfa_recovery_codes_regenerated";
    private const string MemberMfaDisabledAction = "member.mfa_disabled";

    /// <summary>Offers the authenticator secret to a member whose organization requires MFA and who has
    /// none yet, after checking their password. The next sign-in confirms it with the first code.</summary>
    public async Task<MfaEnrollment> BeginRequiredMfaEnrollmentAsync(MemberMfaEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        var account = await VerifyCredentialsAsync(request.Email, request.Password, cancellationToken);
        if (account.MfaSecretProtected is not null)
            throw new AuthenticationException(AuthenticationFailure.MfaAlreadyEnrolled, "Already enrolled.");
        if (!await AnyActiveOrganizationRequiresMfaAsync(account.Id, cancellationToken))
            throw new AuthenticationException(AuthenticationFailure.MfaNotRequired, "MFA is not required.");
        return await GuardSecretAsync(() => BeginMfaEnrollmentAsync(account, cancellationToken));
    }

    public async Task<MemberMfaStatus> GetMemberMfaStatusAsync(Guid tenantId, Guid accountId,
        long sessionVersion, CancellationToken cancellationToken)
    {
        var (account, tenant) = await LoadMemberAsync(tenantId, accountId, sessionVersion, cancellationToken);
        var enabled = account.MfaSecretProtected is not null;
        var remaining = await db.MfaRecoveryCodes.CountAsync(x => x.AccountId == account.Id &&
            x.UsedAtUtc == null, cancellationToken);
        return new MemberMfaStatus(enabled, account.MfaEnabledAtUtc, remaining, tenant.RequireMfa,
            enabled && !await AnyActiveOrganizationRequiresMfaAsync(account.Id, cancellationToken));
    }

    /// <summary>Starts a voluntary enrollment. Keeps offering the same secret for fifteen minutes.</summary>
    public async Task<MfaEnrollment> BeginMemberMfaEnrollmentAsync(Guid tenantId, Guid accountId,
        long sessionVersion, CancellationToken cancellationToken)
    {
        var (account, _) = await LoadMemberAsync(tenantId, accountId, sessionVersion, cancellationToken);
        EnsureNotLocked(account);
        if (account.MfaSecretProtected is not null)
            throw new AuthenticationException(AuthenticationFailure.MfaAlreadyEnrolled, "Already enrolled.");
        return await GuardSecretAsync(() => BeginMfaEnrollmentAsync(account, cancellationToken));
    }

    /// <summary>Confirms the pending secret with its first code and returns the new recovery codes.</summary>
    public async Task<MfaRecoveryCodesResponse> ConfirmMemberMfaEnrollmentAsync(Guid tenantId,
        Guid accountId, long sessionVersion, MfaCodeRequest request, CancellationToken cancellationToken)
    {
        var (account, _) = await LoadMemberAsync(tenantId, accountId, sessionVersion, cancellationToken);
        EnsureNotLocked(account);
        if (account.MfaSecretProtected is not null)
            throw new AuthenticationException(AuthenticationFailure.MfaAlreadyEnrolled, "Already enrolled.");
        if (string.IsNullOrWhiteSpace(request.TotpCode))
            throw new AuthenticationException(AuthenticationFailure.InvalidInput, "A code is required.");
        return await GuardSecretAsync(async () =>
        {
            var codes = await CompleteMfaEnrollmentAsync(account, request.TotpCode, tenantId,
                MemberMfaEnrolledAction, cancellationToken);
            account.MfaFailedCount = 0;
            await db.SaveChangesAsync(cancellationToken);
            return new MfaRecoveryCodesResponse(codes);
        });
    }

    /// <summary>Replaces every recovery code. It needs a current authenticator code (not a recovery
    /// code), so a stolen session alone cannot mint codes.</summary>
    public async Task<MfaRecoveryCodesResponse> RegenerateMemberRecoveryCodesAsync(Guid tenantId,
        Guid accountId, long sessionVersion, MfaCodeRequest request, CancellationToken cancellationToken)
    {
        var (account, _) = await LoadMemberAsync(tenantId, accountId, sessionVersion, cancellationToken);
        EnsureNotLocked(account);
        if (account.MfaSecretProtected is null)
            throw new AuthenticationException(AuthenticationFailure.MfaNotEnrolled, "Not enrolled.");
        return await GuardSecretAsync(async () =>
        {
            await VerifySecondFactorAsync(account, request.TotpCode, null, tenantId,
                MemberRecoveryCodeUsedAction, cancellationToken);
            account.MfaFailedCount = 0;
            await db.SaveChangesAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var codes = await ReplaceRecoveryCodesAsync(account.Id, DateTime.UtcNow, cancellationToken);
            AddMfaAudit(tenantId, RecoveryCodesRegeneratedAction, account.Id, account.Email);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new MfaRecoveryCodesResponse(codes);
        });
    }

    /// <summary>Turns the member's own MFA off after a code. Refused while any organization the
    /// member belongs to requires MFA.</summary>
    public async Task DisableMemberMfaAsync(Guid tenantId, Guid accountId, long sessionVersion,
        MfaCodeRequest request, CancellationToken cancellationToken)
    {
        var (account, _) = await LoadMemberAsync(tenantId, accountId, sessionVersion, cancellationToken);
        EnsureNotLocked(account);
        if (account.MfaSecretProtected is null)
            throw new AuthenticationException(AuthenticationFailure.MfaNotEnrolled, "Not enrolled.");
        if (await AnyActiveOrganizationRequiresMfaAsync(account.Id, cancellationToken))
            throw new AuthenticationException(AuthenticationFailure.MfaRequiredByOrganization,
                "An organization requires MFA.");
        await GuardSecretAsync(async () =>
        {
            await VerifySecondFactorAsync(account, request.TotpCode, request.RecoveryCode, tenantId,
                MemberRecoveryCodeUsedAction, cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            ClearMfa(account);
            await db.MfaRecoveryCodes.Where(x => x.AccountId == account.Id).ExecuteDeleteAsync(cancellationToken);
            AddMfaAudit(tenantId, MemberMfaDisabledAction, account.Id, account.Email);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return 0;
        });
    }

    /// <summary>Clears the authenticator, any pending enrollment, and the failure counter. The caller
    /// deletes the recovery codes and saves.</summary>
    internal static void ClearMfa(Account account)
    {
        account.MfaSecretProtected = null;
        account.MfaEnabledAtUtc = null;
        account.MfaPendingSecretProtected = null;
        account.MfaPendingCreatedAtUtc = null;
        account.MfaLastUsedStep = 0;
        account.MfaFailedCount = 0;
    }

    /// <summary>True when any organization the account is an active member of requires MFA.</summary>
    private Task<bool> AnyActiveOrganizationRequiresMfaAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.Memberships.IgnoreQueryFilters()
            .Where(x => x.AccountId == accountId && x.Status == MembershipStatus.Active &&
                x.Role != TenantRole.PlatformAdmin)
            .Join(db.Tenants.IgnoreQueryFilters().Where(x => x.Status == TenantStatus.Active && x.RequireMfa),
                membership => membership.TenantId, tenant => tenant.Id, (membership, tenant) => tenant.Id)
            .AnyAsync(cancellationToken);

    /// <summary>The signed-in member: an active, non-Platform-Admin member of the token's tenant whose
    /// session version is current and who has no forced password change.</summary>
    private async Task<(Account Account, Tenant Tenant)> LoadMemberAsync(Guid tenantId, Guid accountId,
        long sessionVersion, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || accountId == Guid.Empty) throw InvalidCredentials();
        tenantScope.TenantId = tenantId;
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.AccountId == accountId &&
            x.Status == MembershipStatus.Active, cancellationToken);
        var tenant = await db.Tenants.SingleOrDefaultAsync(cancellationToken);
        if (account is null || membership is null || tenant is null || tenant.Status != TenantStatus.Active ||
            membership.Role == TenantRole.PlatformAdmin || account.SessionVersion != sessionVersion ||
            account.MustChangePassword || membership.MustChangePassword)
            throw InvalidCredentials();
        return (account, tenant);
    }

    // The lockout from wrong codes also guards the signed-in methods, which would otherwise let a
    // stolen session guess codes without limit.
    private static void EnsureNotLocked(Account account)
    {
        if (account.LockedUntilUtc > DateTime.UtcNow)
            throw new AuthenticationException(AuthenticationFailure.RateLimited, "Too many attempts.");
    }

    private static async Task<T> GuardSecretAsync<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new AuthenticationException(AuthenticationFailure.Unavailable,
                "The second factor cannot be verified.");
        }
    }
}
