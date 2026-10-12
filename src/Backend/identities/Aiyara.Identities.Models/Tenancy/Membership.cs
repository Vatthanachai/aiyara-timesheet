namespace Aiyara.Identities.Models.Tenancy;

public enum TenantRole { TenantAdmin, Employee, PlatformAdmin }

/// <summary>Deactivated memberships keep their records but cannot sign in, refresh, or activate.</summary>
public enum MembershipStatus { PendingActivation, Active, Deactivated }

public sealed class Membership
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AccountId { get; set; }
    public TenantRole Role { get; set; }
    public MembershipStatus Status { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
