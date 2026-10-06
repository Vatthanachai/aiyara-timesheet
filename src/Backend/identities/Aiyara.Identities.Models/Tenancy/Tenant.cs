namespace Aiyara.Identities.Models.Tenancy;

public sealed class Tenant
{
    public Guid Id { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public string TimeZoneId { get; set; } = "Asia/Bangkok";
    public DateTime CreatedAtUtc { get; set; }
}
