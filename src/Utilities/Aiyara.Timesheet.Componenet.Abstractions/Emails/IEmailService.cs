namespace Aiyara.Timesheet.Component.Abstractions.Emails;

public interface IEmailService
{
    /// <summary>
    /// Sends an HTML email
    /// </summary>
    Task SendAsync(string toEmail, string subject, string htmlBody);
}
