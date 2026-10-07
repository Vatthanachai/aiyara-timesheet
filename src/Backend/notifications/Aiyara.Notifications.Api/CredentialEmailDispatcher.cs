using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Net.Mime;
using Aiyara.Notifications.Databases;

public sealed record CredentialEmailRequest(string Email, string Template, string Code);

public sealed class CredentialEmailDispatcher(
    NotificationDbContext db, IConfiguration configuration)
{
    public async Task<bool> SendAsync(CredentialEmailRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Template is not ("activation" or "password-reset" or "invitation") ||
            request.Code.Length is < 12 or > 128 ||
            !MailAddress.TryCreate(request.Email, out var recipient)) return false;

        var host = configuration["Smtp:Host"];
        var from = configuration["Smtp:From"];
        if (string.IsNullOrWhiteSpace(host) || !MailAddress.TryCreate(from, out var sender))
            return false;
        var port = configuration.GetValue("Smtp:Port", 25);
        var useTls = configuration.GetValue("Smtp:UseTls", true);
        var activationUrl = Uri.TryCreate(configuration["Frontend:PublicUrl"],
            UriKind.Absolute, out var configuredUrl) &&
            configuredUrl.Scheme is "http" or "https"
                ? new Uri(configuredUrl, "/activate")
                : null;
        var content = EmailTemplates.Credential(request.Template, request.Code,
            activationUrl);
        var outcome = "failed";
        try
        {
            using var mail = new MailMessage(sender, recipient)
            {
                Subject = content.Subject, SubjectEncoding = Encoding.UTF8,
                Body = content.TextBody, BodyEncoding = Encoding.UTF8, IsBodyHtml = false
            };
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                content.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));
            using var smtp = new SmtpClient(host, port) { EnableSsl = useTls };
            var username = configuration["Smtp:Username"];
            if (!string.IsNullOrWhiteSpace(username))
                smtp.Credentials = new NetworkCredential(username, configuration["Smtp:Password"]);
            await smtp.SendMailAsync(mail, cancellationToken);
            outcome = "sent";
        }
        catch (SmtpException)
        {
            // The delivery record must not contain credentials, message body, or code.
        }
        finally
        {
            db.Deliveries.Add(new NotificationDelivery
            {
                Id = Guid.NewGuid(), RecipientEmail = recipient.Address,
                Template = request.Template, Outcome = outcome,
                AttemptedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        return outcome == "sent";
    }

    public static bool ValidInternalKey(string? configured, string? provided)
    {
        if (configured is null || configured.Length < 32 || provided is null) return false;
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
