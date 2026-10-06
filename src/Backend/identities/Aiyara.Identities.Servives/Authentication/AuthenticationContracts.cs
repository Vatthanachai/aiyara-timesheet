namespace Aiyara.Identities.Services.Authentication;

public sealed record RequestCredentialEmail(Guid TenantId, string Email);
public sealed record CompleteCredentialChallenge(string Code, string Password);
public sealed record LoginRequest(Guid TenantId, string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record TenantPasswordPolicyRequest(int MinimumLength, int ExpiryDays,
    bool RequireUppercase, bool RequireLowercase, bool RequireDigit, bool RequireSymbol);
public sealed record AuthenticationResponse(string AccessToken, DateTimeOffset AccessExpiresAtUtc,
    string RefreshToken, DateTimeOffset RefreshExpiresAtUtc, bool MustChangePassword);

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

public enum AuthenticationFailure { InvalidInput, InvalidCredentials, Conflict, RateLimited, Unavailable }

public sealed class AuthenticationException(AuthenticationFailure failure, string message)
    : Exception(message)
{
    public AuthenticationFailure Failure { get; } = failure;
}
