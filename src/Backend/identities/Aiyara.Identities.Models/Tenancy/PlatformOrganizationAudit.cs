namespace Aiyara.Identities.Models.Tenancy;

public sealed class PlatformOrganizationAudit
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorAccountId { get; set; }
    public required string Action { get; set; }
    public string? Reason { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
