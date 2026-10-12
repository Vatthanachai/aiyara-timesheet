namespace Aiyara.Timesheet.Component.Abstractions.Health.HealthChecks.Settings;

/// <summary>
/// Settings for the release version health check.
/// </summary>
[Serializable]
public class ReleaseSettings
{
    /// <summary>
    /// The name of the release version health check.
    /// </summary>
    public string Version { get; set; } = string.Empty;
}