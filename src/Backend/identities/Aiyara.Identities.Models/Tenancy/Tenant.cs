namespace Aiyara.Identities.Models.Tenancy;

public enum TenantStatus { Active, Suspended }

public sealed class Tenant
{
    public Guid Id { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public string OrganizationCode { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public long AccessRevocationVersion { get; set; }
    public string TimeZoneId { get; set; } = "Asia/Bangkok";
    public int PasswordMinimumLength { get; set; } = 12;
    public int PasswordExpiryDays { get; set; } = 180;
    public bool PasswordRequireUppercase { get; set; } = true;
    public bool PasswordRequireLowercase { get; set; } = true;
    public bool PasswordRequireDigit { get; set; } = true;
    public bool PasswordRequireSymbol { get; set; } = true;
    public DateTime PasswordPolicyUpdatedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
