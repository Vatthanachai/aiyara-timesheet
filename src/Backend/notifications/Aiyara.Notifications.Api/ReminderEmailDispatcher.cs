using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Aiyara.Notifications.Databases;

/// <summary>A reminder about time not logged: <c>Kind</c> is <c>daily</c> (about the day in
/// <c>Period</c>) or <c>month-end</c> (about the month in <c>Period</c>, with the hours and up to ten
/// working days with nothing logged).</summary>
public sealed record ReminderEmailRequest(string Email, string MemberName, string Kind, string Period,
    decimal LoggedHours, decimal ExpectedHours, IReadOnlyList<string>? MissingDays, string TimesheetUrl);

public sealed class ReminderEmailDispatcher(
    NotificationDbContext db, IConfiguration configuration)
{
    public async Task<bool> SendAsync(ReminderEmailRequest request, CancellationToken cancellationToken)
    {
        var missing = request.MissingDays ?? [];
        if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email, out var recipient) ||
            request.MemberName is null || request.MemberName.Length > 160 || HasLineBreak(request.MemberName) ||
            request.Kind is not ("daily" or "month-end") ||
            string.IsNullOrWhiteSpace(request.Period) || request.Period.Length > 100 || HasLineBreak(request.Period) ||
            request.LoggedHours < 0 || request.ExpectedHours < 0 || request.LoggedHours > 1000 || request.ExpectedHours > 1000 ||
            missing.Count > 10 || missing.Any(day => string.IsNullOrWhiteSpace(day) || day.Length > 40 || HasLineBreak(day)) ||
            !Uri.TryCreate(request.TimesheetUrl, UriKind.Absolute, out var timesheetUri) ||
            timesheetUri.Scheme is not ("http" or "https"))
            return false;

        var host = configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host) || !MailAddress.TryCreate(configuration["Smtp:From"], out var sender))
            return false;
        var outcome = "failed";
        try
        {
            var content = request.Kind == "daily"
                ? EmailTemplates.DailyReminder(request.MemberName.Trim(), request.Period, timesheetUri)
                : EmailTemplates.MonthEndReminder(request.MemberName.Trim(), request.Period, request.LoggedHours,
                    request.ExpectedHours, missing, timesheetUri);
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
                Template = $"reminder-{request.Kind}", Outcome = outcome, AttemptedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        return outcome == "sent";
    }

    private static bool HasLineBreak(string value) => value.Contains('\r') || value.Contains('\n');
}
