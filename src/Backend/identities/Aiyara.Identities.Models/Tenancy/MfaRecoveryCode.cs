namespace Aiyara.Identities.Models.Tenancy;

/// <summary>One single-use recovery code for an account's second factor, stored only as a hash.</summary>
public sealed class MfaRecoveryCode
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string CodeHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}
