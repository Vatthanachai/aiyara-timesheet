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
    /// <summary>Object-storage key of the organization's logo, shown in the Shell header.</summary>
    public string? LogoKey { get; set; }
    /// <summary>When the logo last changed; versions its URL so browsers fetch the new one.</summary>
    public DateTime? LogoUpdatedAtUtc { get; set; }
    /// <summary>How every member sees dates; one of <see cref="OrganizationFormats.DateFormats"/>.</summary>
    public string DateFormat { get; set; } = OrganizationFormats.DefaultDateFormat;
    /// <summary>How every member sees times; one of <see cref="OrganizationFormats.TimeFormats"/>.</summary>
    public string TimeFormat { get; set; } = OrganizationFormats.DefaultTimeFormat;
}

/// <summary>The date and time formats a Tenant Admin may choose. <c>bbbb</c> is the Buddhist year
/// (Gregorian + 543); <c>MMM</c> is the month name in the viewer's language.</summary>
public static class OrganizationFormats
{
    public const string DefaultDateFormat = "dd/MM/yyyy";
    public const string DefaultTimeFormat = "HH:mm";
    public static readonly IReadOnlyList<string> DateFormats =
        [DefaultDateFormat, "dd/MM/bbbb", "d MMM yyyy", "yyyy-MM-dd", "MM/dd/yyyy"];
    public static readonly IReadOnlyList<string> TimeFormats = [DefaultTimeFormat, "h:mm a"];
}
