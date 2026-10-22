namespace Aiyara.Identities.Services.Authentication;

public sealed record RequestCredentialEmail(Guid TenantId, string Email);
/// <summary>Public forgot-password body. A password belongs to the account rather than one
/// organization, so the caller gives only the email; no tenant ID or organization code.</summary>
public sealed record PasswordResetRequest(string Email);
public sealed record CompleteCredentialChallenge(string Code, string Password);
public sealed record LoginRequest
{
    public Guid? TenantId { get; init; }
    public string? OrganizationCode { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string? TotpCode { get; init; }
    public string? RecoveryCode { get; init; }

    public LoginRequest() { }

    public LoginRequest(Guid tenantId, string email, string password)
    {
        TenantId = tenantId;
        Email = email;
        Password = password;
    }
}
/// <summary>Public login body. Tenants are selected only by organization code; the internal
/// tenant ID is never accepted from callers. <c>TotpCode</c> or <c>RecoveryCode</c> answers an
/// organization's (or the account's own) second factor; a code from the pending authenticator also
/// completes a required enrollment.</summary>
public sealed record OrganizationLoginRequest(string? OrganizationCode, string Email, string Password,
    string? TotpCode = null, string? RecoveryCode = null);
/// <summary>System administrator sign-in. A second factor is always required: a code from the
/// enrolled authenticator, or one unused recovery code.</summary>
public sealed record PlatformAdminLoginRequest(string Email, string Password, string? TotpCode = null,
    string? RecoveryCode = null);
/// <summary>The authenticator secret to add before the first code can be confirmed.</summary>
public sealed record MfaEnrollment(string Secret, string ProvisioningUri);
/// <summary>Either <see cref="Enrollment"/> is offered (no session yet) or <see cref="Session"/> is
/// issued. <see cref="RecoveryCodes"/> is filled only once, when enrollment completes.</summary>
public sealed record PlatformAdminLoginResponse(AuthenticationResponse? Session, MfaEnrollment? Enrollment,
    IReadOnlyList<string> RecoveryCodes);
public sealed record SignInRequest(string Email, string Password, string? TotpCode = null,
    string? RecoveryCode = null);
/// <summary>Asks for the authenticator secret a member must add before a required enrollment can
/// be confirmed by the next login. The password is checked again; no session is involved.</summary>
public sealed record MemberMfaEnrollmentRequest(string Email, string Password);
/// <summary>A code from the member's authenticator (<c>TotpCode</c>) or, where the endpoint allows it,
/// one unused recovery code.</summary>
public sealed record MfaCodeRequest(string? TotpCode, string? RecoveryCode = null);
/// <summary>The member's own second-factor state. <see cref="RequiredByOrganization"/> is the
/// signed-in organization's setting; <see cref="CanDisable"/> is false when the member is not enrolled or any
/// of their active organizations requires MFA.</summary>
public sealed record MemberMfaStatus(bool Enabled, DateTime? EnabledAtUtc, int RecoveryCodesRemaining,
    bool RequiredByOrganization, bool CanDisable);
/// <summary>Recovery codes shown once; only their hashes are stored.</summary>
public sealed record MfaRecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);
public sealed record SignInOrganization(string OrganizationCode, string Name);
/// <summary>Either <see cref="Session"/> is issued (one organization) or the caller must
/// pick one of <see cref="Organizations"/> and log in with its organization code.</summary>
public sealed record SignInResponse(AuthenticationResponse? Session,
    IReadOnlyList<SignInOrganization> Organizations);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record TenantPasswordPolicyRequest(int MinimumLength, int ExpiryDays,
    bool RequireUppercase, bool RequireLowercase, bool RequireDigit, bool RequireSymbol);
/// <summary><see cref="RecoveryCodes"/> is null except in the response that completes a member's
/// required MFA enrollment, where it lists the one-time recovery codes (shown once).</summary>
public sealed record AuthenticationResponse(string AccessToken, DateTimeOffset AccessExpiresAtUtc,
    string RefreshToken, DateTimeOffset RefreshExpiresAtUtc, bool MustChangePassword,
    Guid TenantId, string Role, IReadOnlyList<string>? RecoveryCodes = null);

public interface ICredentialNotificationSender
{
    Task SendAsync(string email, string template, string code, CancellationToken cancellationToken);
}

public interface ICredentialAttemptLimiter
{
    Task CheckAsync(Guid tenantId, string normalizedEmail, string purpose,
        CancellationToken cancellationToken);
}

public interface ISessionRevocationPublisher
{
    Task PublishAccountAsync(Guid tenantId, Guid accountId, long sessionVersion,
        CancellationToken cancellationToken);
    Task PublishTenantPolicyAsync(Guid tenantId, CancellationToken cancellationToken);
    Task CompleteTenantPolicyAsync(Guid tenantId, long policyVersion,
        CancellationToken cancellationToken);
}

/// <summary><see cref="MfaRequired"/>: the password was right but a second factor must follow.
/// <see cref="MfaInvalid"/>: the second factor was wrong, already used, or the enrollment expired.
/// <see cref="MfaEnrollmentRequired"/>: the member's organization requires MFA and they have no
/// authenticator yet. The remaining MFA values are conflicts on the member's own MFA settings.</summary>
public enum AuthenticationFailure
{
    InvalidInput, InvalidCredentials, Conflict, RateLimited, Unavailable, MfaRequired, MfaInvalid,
    MfaEnrollmentRequired, MfaAlreadyEnrolled, MfaNotEnrolled, MfaNotRequired, MfaRequiredByOrganization
}

public sealed class AuthenticationException(AuthenticationFailure failure, string message)
    : Exception(message)
{
    public AuthenticationFailure Failure { get; } = failure;
}
