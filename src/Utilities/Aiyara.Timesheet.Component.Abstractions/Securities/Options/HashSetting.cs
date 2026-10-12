namespace Aiyara.Timesheet.Component.Abstractions.Securities.Options;

/// <summary>
/// Settings for password hashing configuration
/// </summary>
[Serializable]
public class HashSetting
{
    /// <summary>
    /// Gets or sets the size of the key in bits for password hashing
    /// </summary>
    public int KeySize { get; set; }

    /// <summary>
    /// Gets or sets the number of iterations for the PBKDF2 algorithm
    /// </summary>
    public int Iteration { get; set; }
}