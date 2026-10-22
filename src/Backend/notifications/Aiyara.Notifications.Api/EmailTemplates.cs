using System.Net;

internal sealed record EmailContent(string Subject, string TextBody, string HtmlBody);

internal static class EmailTemplates
{
    private const string Brand = "Aiyara Timesheet";
    private const string Ink = "#172b4d";
    private const string Muted = "#5b6b82";
    private const string BrandColor = "#2457a7";

    public static EmailContent Credential(string template, string code, Uri? frontendUrl)
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
        // The code stays out of the link, so it is never kept in browser history or proxy logs.
        var (pagePath, pageLabel, pageButton) = template switch
        {
            "activation" => ("/activate", "Activation page", "Open the activation page"),
            "invitation" => ("/accept-invitation", "Invitation page", "Open the invitation page"),
            _ => ("/reset-password", "Reset page", "Open the reset page")
        };
        var pageUrl = frontendUrl is not null ? new Uri(frontendUrl, pagePath) : null;
        var actionText = pageUrl is not null
            ? $"\n{pageLabel}: {pageUrl.AbsoluteUri}"
            : string.Empty;
        var text = $"{content.Title}\n\n{content.Description}\n\n" +
            $"{content.CodeLabel}: {code}\n{content.Expiry}{actionText}\n\n{content.SecurityNote}\n\n{Brand}";
        var actionHtml = pageUrl is not null
            ? $"""
              <table role="presentation" cellpadding="0" cellspacing="0" style="margin-top:22px"><tr><td style="background:{BrandColor};border-radius:8px">
                <a href="{WebUtility.HtmlEncode(pageUrl.AbsoluteUri)}" style="display:inline-block;padding:12px 20px;color:#ffffff;font-size:15px;font-weight:700;text-decoration:none">{pageButton}</a>
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

    public static EmailContent MonthReturned(string memberName, string period, string reason, Uri timesheetUri)
    {
        var greeting = memberName.Length > 0 ? $"Hello {memberName}," : "Hello,";
        var safeGreeting = WebUtility.HtmlEncode(greeting);
        var safePeriod = WebUtility.HtmlEncode(period);
        // The reason is the admin's own text: encoded, with its line breaks kept.
        var safeReason = WebUtility.HtmlEncode(reason).ReplaceLineEndings("<br>");
        var safeUrl = WebUtility.HtmlEncode(timesheetUri.AbsoluteUri);
        var subject = $"Your timesheet for {period} was returned";
        var text = $"{greeting}\n\nYour timesheet for {period} was returned for changes.\n\nReason:\n{reason}\n\n" +
            $"Update it and submit it again: {timesheetUri.AbsoluteUri}\n\n{Brand}";
        var body = $"""
            <p style="margin:0 0 18px;color:{Muted};font-size:16px;line-height:1.7">{safeGreeting}</p>
            <p style="margin:0 0 18px;color:{Muted};font-size:16px;line-height:1.7">Your timesheet for <strong style="color:{Ink}">{safePeriod}</strong> was returned for changes.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f3f6fb;border:1px solid #dce5f1;border-radius:10px">
              <tr><td style="padding:14px 16px 4px;color:{Muted};font-size:13px">Reason</td></tr>
              <tr><td style="padding:0 16px 16px;color:{Ink};font-size:15px;line-height:1.6">{safeReason}</td></tr>
            </table>
            <table role="presentation" cellpadding="0" cellspacing="0" style="margin-top:22px"><tr><td style="background:{BrandColor};border-radius:8px">
              <a href="{safeUrl}" style="display:inline-block;padding:12px 20px;color:#ffffff;font-size:15px;font-weight:700;text-decoration:none">Open my timesheet</a>
            </td></tr></table>
            """;
        return Create(subject, text, body);
    }

    public static EmailContent DailyReminder(string memberName, string day, Uri timesheetUri)
    {
        var greeting = memberName.Length > 0 ? $"Hello {memberName}," : "Hello,";
        var safeDay = WebUtility.HtmlEncode(day);
        var subject = $"No time logged for {day}";
        var text = $"{greeting}\n\nYou have not logged any time or leave for {day}.\n\n" +
            $"Add it to your timesheet: {timesheetUri.AbsoluteUri}\n\n{Brand}";
        var body = $"""
            <p style="margin:0 0 18px;color:{Muted};font-size:16px;line-height:1.7">{WebUtility.HtmlEncode(greeting)}</p>
            <p style="margin:0 0 22px;color:{Muted};font-size:16px;line-height:1.7">You have not logged any time or leave for <strong style="color:{Ink}">{safeDay}</strong>.</p>
            {Button(timesheetUri, "Open my timesheet")}
            """;
        return Create(subject, text, body);
    }

    public static EmailContent MonthEndReminder(string memberName, string month, decimal loggedHours,
        decimal expectedHours, IReadOnlyList<string> missingDays, Uri timesheetUri)
    {
        var greeting = memberName.Length > 0 ? $"Hello {memberName}," : "Hello,";
        var safeMonth = WebUtility.HtmlEncode(month);
        var hours = FormattableString.Invariant($"{loggedHours:0.##} of {expectedHours:0.##} hours");
        var subject = $"Your timesheet for {month} is short";
        var missingText = missingDays.Count > 0 ? $"\nDays with nothing logged: {string.Join(", ", missingDays)}\n" : "";
        var text = $"{greeting}\n\nToday is the last working day of {month}. So far you have logged {hours} " +
            $"expected up to yesterday, leave included.\n{missingText}\n" +
            $"Complete your timesheet: {timesheetUri.AbsoluteUri}\n\n{Brand}";
        var missingHtml = missingDays.Count > 0
            ? $"""
              <tr><td style="padding:0 16px 4px;color:{Muted};font-size:13px">Days with nothing logged</td></tr>
              <tr><td style="padding:0 16px 16px;color:{Ink};font-size:15px;line-height:1.6">{WebUtility.HtmlEncode(string.Join(", ", missingDays))}</td></tr>
              """
            : string.Empty;
        var body = $"""
            <p style="margin:0 0 18px;color:{Muted};font-size:16px;line-height:1.7">{WebUtility.HtmlEncode(greeting)}</p>
            <p style="margin:0 0 18px;color:{Muted};font-size:16px;line-height:1.7">Today is the last working day of <strong style="color:{Ink}">{safeMonth}</strong>. Your timesheet is short of the hours expected up to yesterday, leave included.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f3f6fb;border:1px solid #dce5f1;border-radius:10px">
              <tr><td style="padding:14px 16px 4px;color:{Muted};font-size:13px">Logged</td></tr>
              <tr><td style="padding:0 16px 14px;color:{Ink};font-size:18px;font-weight:700">{WebUtility.HtmlEncode(hours)}</td></tr>
              {missingHtml}
            </table>
            <div style="margin-top:22px">{Button(timesheetUri, "Complete my timesheet")}</div>
            """;
        return Create(subject, text, body);
    }

    private static string Button(Uri uri, string label) => $"""
        <table role="presentation" cellpadding="0" cellspacing="0"><tr><td style="background:{BrandColor};border-radius:8px">
          <a href="{WebUtility.HtmlEncode(uri.AbsoluteUri)}" style="display:inline-block;padding:12px 20px;color:#ffffff;font-size:15px;font-weight:700;text-decoration:none">{label}</a>
        </td></tr></table>
        """;

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
