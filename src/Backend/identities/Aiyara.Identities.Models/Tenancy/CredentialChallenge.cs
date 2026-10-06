namespace Aiyara.Identities.Models.Tenancy;

public enum CredentialChallengePurpose { Activation, PasswordReset }

public sealed class CredentialChallenge
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid TenantId { get; set; }
    public required string TokenHash { get; set; }
    public CredentialChallengePurpose Purpose { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}
