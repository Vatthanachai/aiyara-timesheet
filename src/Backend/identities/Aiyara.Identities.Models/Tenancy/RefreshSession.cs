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
    /// <summary>The browser or app that signed in, as its User-Agent header (trimmed); a rotation keeps it.</summary>
    public string? UserAgent { get; set; }
    /// <summary>When the sign-in began. A rotation copies it, so it outlives the token's own creation time,
    /// which is the last time the session was used.</summary>
    public DateTime? SignedInAtUtc { get; set; }
}
