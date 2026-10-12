namespace Aiyara.Timesheet.Component.Abstractions.Emails.Options;

/// <summary>
/// Settings for sending mail via SMTP
/// </summary>
[Serializable]
public class SmtpSetting
{
    public string Host { get; set; }

    public int Port { get; set; }

    public string Username { get; set; }

    public string Password { get; set; }

    public string From { get; set; }

    public string FromName { get; set; }

    public bool EnableSsl { get; set; }
}
