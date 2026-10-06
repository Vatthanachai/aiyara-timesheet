namespace Aiyara.Identities.Models.Tenancy;

public sealed class Account
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public bool IsPlatformAdmin { get; set; }
    public string? PasswordHash { get; set; }
    public DateTime? PasswordChangedAtUtc { get; set; }
    public bool MustChangePassword { get; set; }
    public long SessionVersion { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
