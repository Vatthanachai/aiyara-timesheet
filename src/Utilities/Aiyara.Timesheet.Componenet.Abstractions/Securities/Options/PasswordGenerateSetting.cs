namespace Aiyara.Timesheet.Component.Abstractions.Securities.Options;

/// <summary>
/// Settings for password generation and security policies
/// </summary>
[Serializable]
public class PasswordGenerateSetting
{
    /// <summary>
    /// Gets or sets whether to include lowercase letters in generated passwords
    /// </summary>
    public bool IsIncludeLowerCase { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include uppercase letters in generated passwords
    /// </summary>
    public bool IsIncludeUpperCase { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include numeric characters in generated passwords
    /// </summary>
    public bool IsIncludeNumeric { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include special characters in generated passwords
    /// </summary>
    public bool IsIncludeSpecialCase { get; set; } = true;

    /// <summary>
    /// Gets or sets the length of generated passwords
    /// </summary>
    public int PasswordLength { get; set; } = 16;

    /// <summary>
    /// Gets or sets the maximum number of failed login attempts before account lockout
    /// </summary>
    public int AccessFailedCount { get; set; } = 5;

    /// <summary>
    /// Gets or sets the number of days after which a password expires
    /// </summary>
    public int PasswordExpiredDays { get; set; } = 180;
}