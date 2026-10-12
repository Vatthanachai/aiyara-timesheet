namespace Aiyara.Timesheet.Component.Abstractions.Emails.Options;

/// <summary>
/// Settings for sending mail via SMTP
/// </summary>
[Serializable]
public class SmtpSetting
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;

    public bool EnableSsl { get; set; }
}
