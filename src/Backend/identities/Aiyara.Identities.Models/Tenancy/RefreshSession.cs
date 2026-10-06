namespace Aiyara.Identities.Models.Tenancy;

public sealed class RefreshSession
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid TenantId { get; set; }
    public required string TokenHash { get; set; }
    public long SessionVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? ReplacedById { get; set; }
}
