namespace Aiyara.Identities.Models.Tenancy;

public sealed class Account
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? PhotoUrl { get; set; }
    public string? JobTitle { get; set; }
    public bool IsPlatformAdmin { get; set; }
    public string? PasswordHash { get; set; }
    public DateTime? PasswordChangedAtUtc { get; set; }
    public bool MustChangePassword { get; set; }
    public long SessionVersion { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    /// <summary>Encrypted TOTP secret; null until enrollment completes. Required for Platform Admins.</summary>
    public string? MfaSecretProtected { get; set; }
    public DateTime? MfaEnabledAtUtc { get; set; }
    /// <summary>Encrypted secret offered during enrollment, before the first code confirms it.</summary>
    public string? MfaPendingSecretProtected { get; set; }
    public DateTime? MfaPendingCreatedAtUtc { get; set; }
    /// <summary>Last accepted TOTP step, so an accepted code cannot be replayed.</summary>
    public long MfaLastUsedStep { get; set; }
    public int MfaFailedCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
