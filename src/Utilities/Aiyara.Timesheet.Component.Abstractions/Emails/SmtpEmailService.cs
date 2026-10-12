using System.Net;
using System.Net.Mail;

using Aiyara.Timesheet.Component.Abstractions.Emails.Options;

using Microsoft.Extensions.Options;

namespace Aiyara.Timesheet.Component.Abstractions.Emails;

/// <summary>
/// Sends email via SMTP using the configured <see cref="SmtpSetting"/>
/// </summary>
public class SmtpEmailService(IOptions<SmtpSetting> smtpSetting) : IEmailService
{
    public async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        var setting = smtpSetting.Value;

        using var message = new MailMessage
        {
            From = new MailAddress(setting.From, setting.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
        };
        message.To.Add(toEmail);

        using var client = new SmtpClient(setting.Host, setting.Port)
        {
            EnableSsl = setting.EnableSsl,
        };

        if (!string.IsNullOrWhiteSpace(setting.Username))
        {
            client.Credentials = new NetworkCredential(setting.Username, setting.Password);
        }

        await client.SendMailAsync(message);
    }
}
