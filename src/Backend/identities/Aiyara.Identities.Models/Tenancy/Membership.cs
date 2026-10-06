namespace Aiyara.Identities.Models.Tenancy;

public enum TenantRole { TenantAdmin, Employee }

public enum MembershipStatus { PendingActivation, Active }

public sealed class Membership
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AccountId { get; set; }
    public TenantRole Role { get; set; }
    public MembershipStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
