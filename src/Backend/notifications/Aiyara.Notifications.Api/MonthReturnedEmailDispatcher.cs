using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Aiyara.Notifications.Databases;

public sealed record MonthReturnedEmailRequest(string Email, string MemberName, string Period, string Reason,
    string TimesheetUrl);

public sealed class MonthReturnedEmailDispatcher(
    NotificationDbContext db, IConfiguration configuration)
{
    public async Task<bool> SendAsync(MonthReturnedEmailRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email, out var recipient) ||
            request.MemberName is null || request.MemberName.Length > 160 || HasLineBreak(request.MemberName) ||
            string.IsNullOrWhiteSpace(request.Period) || request.Period.Length > 100 || HasLineBreak(request.Period) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000 ||
            !Uri.TryCreate(request.TimesheetUrl, UriKind.Absolute, out var timesheetUri) ||
            timesheetUri.Scheme is not ("http" or "https"))
            return false;

        var host = configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host) || !MailAddress.TryCreate(configuration["Smtp:From"], out var sender))
            return false;
        var outcome = "failed";
        try
        {
            var content = EmailTemplates.MonthReturned(request.MemberName.Trim(), request.Period, request.Reason,
                timesheetUri);
            using var mail = new MailMessage(sender, recipient)
            {
                Subject = content.Subject, SubjectEncoding = Encoding.UTF8,
                Body = content.TextBody, BodyEncoding = Encoding.UTF8, IsBodyHtml = false
            };
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                content.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));
            using var smtp = new SmtpClient(host, configuration.GetValue("Smtp:Port", 25))
            { EnableSsl = configuration.GetValue("Smtp:UseTls", true) };
            var username = configuration["Smtp:Username"];
            if (!string.IsNullOrWhiteSpace(username))
                smtp.Credentials = new NetworkCredential(username, configuration["Smtp:Password"]);
            await smtp.SendMailAsync(mail, cancellationToken);
            outcome = "sent";
        }
        catch (SmtpException)
        {
            // Persist only a delivery outcome; never store the message body or credentials.
        }
        finally
        {
            db.Deliveries.Add(new NotificationDelivery
            {
                Id = Guid.NewGuid(), RecipientEmail = recipient.Address,
                Template = "month-returned", Outcome = outcome, AttemptedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        return outcome == "sent";
    }

    private static bool HasLineBreak(string value) => value.Contains('\r') || value.Contains('\n');
}
