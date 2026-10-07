using System.Net;
using System.Net.Mail;
using Aiyara.Notifications.Databases;

internal sealed record ReportReadyEmailRequest(string Email, string EmployeeName,
    string Period, string ReportUrl, Guid ReportRunId);

internal sealed class ReportReadyEmailDispatcher(
    NotificationDbContext db, IConfiguration configuration)
{
    public async Task<bool> SendAsync(ReportReadyEmailRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Period) ||
            request.Period.Length > 100 || request.Period.Contains('\r') || request.Period.Contains('\n') ||
            request.EmployeeName is null || request.EmployeeName.Length > 160 ||
            !MailAddress.TryCreate(request.Email, out var recipient) || request.ReportRunId == Guid.Empty ||
            request.EmployeeName.Contains('\r') ||
            request.EmployeeName.Contains('\n') || !Uri.TryCreate(request.ReportUrl,
                UriKind.Absolute, out var reportUri) || reportUri.Scheme is not ("http" or "https"))
            return false;

        var host = configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host) || !MailAddress.TryCreate(configuration["Smtp:From"], out var sender))
            return false;
        var outcome = "failed";
        try
        {
            using var mail = new MailMessage(sender, recipient)
            {
                Subject = $"Aiyara timesheet report ready: {request.Period}",
                Body = $"Hello {request.EmployeeName},\n\nYour timesheet report for {request.Period} is ready.\n" +
                    $"Sign in to Aiyara to view it: {reportUri}\n\nReport reference: {request.ReportRunId:D}",
                IsBodyHtml = false
            };
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
                Template = "report-ready", Outcome = outcome, AttemptedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        return outcome == "sent";
    }
}
