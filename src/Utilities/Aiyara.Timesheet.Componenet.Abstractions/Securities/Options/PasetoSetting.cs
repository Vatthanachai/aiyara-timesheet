namespace Aiyara.Timesheet.Component.Abstractions.Securities.Options;

/// <summary>
/// Settings for PASETO token generation
/// </summary>
[Serializable]
public class PasetoSetting
{
    /// <summary>
    /// Base64-encoded 32-byte Ed25519 seed. Configure only on Identity.
    /// </summary>
    public string Key { get; set; }

    /// <summary>
    /// Token lifetime in minutes
    /// </summary>
    public int ExpireMinutes { get; set; } = 15;
    public string Issuer { get; set; } = "aiyara-identity";
    public string Audience { get; set; } = "aiyara-api";
}
