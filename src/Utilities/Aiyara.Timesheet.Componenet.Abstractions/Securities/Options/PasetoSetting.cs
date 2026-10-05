namespace Aiyara.Timesheet.Component.Abstractions.Securities.Options;

/// <summary>
/// Settings for PASETO token generation
/// </summary>
[Serializable]
public class PasetoSetting
{
    /// <summary>
    /// Base64-encoded 32-byte symmetric key used for PASETO v4.local encryption
    /// </summary>
    public string Key { get; set; }

    /// <summary>
    /// Token lifetime in minutes
    /// </summary>
    public int ExpireMinutes { get; set; } = 60;
}
