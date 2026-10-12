namespace Aiyara.Timesheet.Component.Abstractions.Settings;

/// <summary>
/// Setting of key vault
/// </summary>
public class AzureVaultSettings
{
    /// <summary>
    /// Url/Address
    /// </summary>
    public string URL { get; set; } = string.Empty;

    /// <summary>
    /// Client id
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret 
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Tenant id
    /// </summary>
    public string Token { get; set; } = string.Empty;
}