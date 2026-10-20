namespace Aiyara.Identities.Models.Tenancy;

/// <summary>A Tenant Admin's label for a kind of member, such as permanent staff or outsourced.
/// Each membership belongs to at most one group. Groups classify members for filtering and
/// reports only; they grant no permissions.</summary>
public sealed class MemberGroup
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
