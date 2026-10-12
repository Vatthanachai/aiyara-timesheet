using System.Net;

internal sealed record EmailContent(string Subject, string TextBody, string HtmlBody);

internal static class EmailTemplates
{
    private const string Brand = "Aiyara Timesheet";
    private const string Ink = "#172b4d";
    private const string Muted = "#5b6b82";
    private const string BrandColor = "#2457a7";

    public static EmailContent Credential(string template, string code, Uri? activationUrl)
    {
        var content = template switch
        {
            "activation" => new CredentialCopy(
                "Activate your Aiyara Timesheet account",
                "Use this code to activate your account and set a new password.",
                "Activation code",
                "This code is valid for 24 hours.",
                "If you did not request this, please ignore this email and do not share the code with anyone."),
            "invitation" => new CredentialCopy(
                "Your Aiyara Timesheet invitation",
                "Use this code to accept your invitation to join Aiyara Timesheet.",
                "Invitation code",
                "This code is valid for 7 days.",
                "If you were not expecting this invitation, please contact your organization administrator and do not share the code with anyone."),
            _ => new CredentialCopy(
                "Reset your Aiyara Timesheet password",
                "Use this code to reset your password.",
                "Reset code",
                "This code is valid for 24 hours.",
                "If you did not request a password reset, please ignore this email and do not share the code with anyone.")
        };

        var safeCode = WebUtility.HtmlEncode(code);
        var actionText = template == "activation" && activationUrl is not null
            ? $"\nActivation page: {activationUrl.AbsoluteUri}"
            : string.Empty;
        var text = $"{content.Title}\n\n{content.Description}\n\n" +
            $"{content.CodeLabel}: {code}\n{content.Expiry}{actionText}\n\n{content.SecurityNote}\n\n{Brand}";
        var actionHtml = template == "activation" && activationUrl is not null
            ? $"""
              <table role="presentation" cellpadding="0" cellspacing="0" style="margin-top:22px"><tr><td style="background:{BrandColor};border-radius:8px">
                <a href="{WebUtility.HtmlEncode(activationUrl.AbsoluteUri)}" style="display:inline-block;padding:12px 20px;color:#ffffff;font-size:15px;font-weight:700;text-decoration:none">Open the activation page</a>
              </td></tr></table>
              """
            : string.Empty;
        var body = $"""
            <p style="margin:0 0 22px;color:{Muted};font-size:16px;line-height:1.7">{content.Description}</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f3f6fb;border:1px solid #dce5f1;border-radius:10px">
              <tr><td align="center" style="padding:20px 16px 8px;color:{Muted};font-size:13px">{content.CodeLabel}</td></tr>
              <tr><td align="center" style="padding:0 16px 18px;color:{Ink};font-family:Consolas,Monaco,monospace;font-size:26px;font-weight:700;letter-spacing:3px;word-break:break-all;direction:ltr">{safeCode}</td></tr>
            </table>
            <p style="margin:16px 0 0;color:{BrandColor};font-size:14px;font-weight:700">{content.Expiry}</p>
            {actionHtml}
            <p style="margin:24px 0 0;padding-top:18px;border-top:1px solid #e5eaf1;color:{Muted};font-size:13px;line-height:1.7">{content.SecurityNote}</p>
            """;
        return Create(content.Title, text, body);
    }

    public static EmailContent ReportReady(string employeeName, string period, Uri reportUri,
        Guid reportRunId)
    {
        var safeName = WebUtility.HtmlEncode(employeeName);
        var safePeriod = WebUtility.HtmlEncode(period);
        var safeUrl = WebUtility.HtmlEncode(reportUri.AbsoluteUri);
        var subject = $"Aiyara timesheet report ready: {period}";
        var text = $"Hello {employeeName},\n\nYour timesheet report for {period} is ready.\n" +
            $"Sign in to Aiyara to view it: {reportUri.AbsoluteUri}\nReport reference: {reportRunId:D}\n\n{Brand}";
        var body = $"""
            <p style="margin:0 0 18px;color:{Muted};font-size:16px;line-height:1.7">Hello {safeName}</p>
            <p style="margin:0 0 22px;color:{Muted};font-size:16px;line-height:1.7">Your timesheet report for <strong style="color:{Ink}">{safePeriod}</strong> is ready.</p>
            <table role="presentation" cellpadding="0" cellspacing="0"><tr><td style="background:{BrandColor};border-radius:8px">
              <a href="{safeUrl}" style="display:inline-block;padding:12px 20px;color:#ffffff;font-size:15px;font-weight:700;text-decoration:none">Open the report</a>
            </td></tr></table>
            <p style="margin:22px 0 0;color:{Muted};font-size:12px;line-height:1.7">Report reference: {reportRunId:D}</p>
            """;
        return Create(subject, text, body);
    }

    private static EmailContent Create(string title, string text, string body)
    {
        var safeTitle = WebUtility.HtmlEncode(title);
        var html = $"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#f2f5f9;color:{Ink};font-family:Arial,'Tahoma',sans-serif">
              <div style="display:none;max-height:0;overflow:hidden;opacity:0">{safeTitle} - {Brand}</div>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f2f5f9;padding:32px 12px">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;background:#ffffff;border:1px solid #e1e7ef;border-radius:14px;overflow:hidden">
                    <tr><td style="background:{BrandColor};padding:22px 28px;color:#ffffff">
                      <div style="font-size:18px;font-weight:700;letter-spacing:.2px">{Brand}</div>
                      <div style="margin-top:5px;color:#e8efff;font-size:12px;letter-spacing:1.4px;text-transform:uppercase">WORKSPACE NOTIFICATION</div>
                    </td></tr>
                    <tr><td style="padding:32px 28px 28px">
                      <h1 style="margin:0 0 18px;color:{Ink};font-size:24px;line-height:1.35">{safeTitle}</h1>
                      {body}
                    </td></tr>
                    <tr><td style="border-top:1px solid #e8edf3;padding:18px 28px;color:{Muted};font-size:12px;line-height:1.6">
                      This is an automated message. Please do not reply.<br>{Brand}
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
        return new EmailContent(title, text, html);
    }

    private sealed record CredentialCopy(string Title, string Description, string CodeLabel,
        string Expiry, string SecurityNote);
}
