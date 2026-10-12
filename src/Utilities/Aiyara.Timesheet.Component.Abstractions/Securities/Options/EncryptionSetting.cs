namespace Aiyara.Timesheet.Component.Abstractions.Securities.Options;

/// <summary>
/// Options for configuring encryption settings
/// </summary>
public class EncryptionSetting
{
    /// <summary>
    /// Gets or sets the encryption key in base64 format
    /// </summary>
    public string Key { get; set; }

    /// <summary>
    /// Gets or sets the initialization vector (IV) in base64 format
    /// </summary>
    public string IV { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptionSetting"/> class
    /// </summary>
    public EncryptionSetting()
    {
        Key = string.Empty;
        IV = string.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptionSetting"/> class with specified key and IV
    /// </summary>
    /// <param name="key">The encryption key in base64 format</param>
    /// <param name="iv">The initialization vector in base64 format</param>
    public EncryptionSetting(string key, string iv)
    {
        Key = key;
        IV = iv;
    }
}