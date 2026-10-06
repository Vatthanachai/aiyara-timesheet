namespace Aiyara.Identities.Models.Tenancy;

public sealed class Invitation
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Email { get; set; }
    public required string TokenHash { get; set; }
    public TenantRole Role { get; set; } = TenantRole.Employee;
    public Guid InvitedByAccountId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
}
