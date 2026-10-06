using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Aiyara.Notifications.Databases;

internal sealed record CredentialEmailRequest(string Email, string Template, string Code);

internal sealed class CredentialEmailDispatcher(
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
        var subject = request.Template switch
        {
            "activation" => "Activate your Aiyara Timesheet account",
            "invitation" => "Your Aiyara Timesheet invitation",
            _ => "Reset your Aiyara Timesheet password"
        };
        var body = request.Template switch
        {
            "activation" => $"Use this one-time activation password within 24 hours: {request.Code}",
            "invitation" => $"Use this one-time invitation code within 7 days: {request.Code}",
            _ => $"Use this one-time reset password within 24 hours: {request.Code}"
        };
        var outcome = "failed";
        try
        {
            using var mail = new MailMessage(sender, recipient)
            {
                Subject = subject, Body = body, IsBodyHtml = false
            };
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
