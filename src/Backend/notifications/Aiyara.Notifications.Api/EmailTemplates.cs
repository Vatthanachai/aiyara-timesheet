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
                "เปิดใช้งานบัญชีผู้ดูแล",
                "ใช้รหัสนี้เพื่อเปิดใช้งานบัญชีและตั้งรหัสผ่านใหม่",
                "รหัสเปิดใช้งาน",
                "รหัสนี้ใช้ได้ 24 ชั่วโมง",
                "หากคุณไม่ได้สมัครใช้งาน กรุณาละเว้นอีเมลนี้ และอย่าเปิดเผยรหัสแก่ผู้อื่น."),
            "invitation" => new CredentialCopy(
                "คำเชิญเข้าใช้งานองค์กร",
                "ใช้รหัสนี้เพื่อยืนยันคำเชิญเข้าใช้งาน Aiyara Timesheet",
                "รหัสคำเชิญ",
                "รหัสนี้ใช้ได้ 7 วัน",
                "หากคุณไม่ได้คาดว่าจะได้รับคำเชิญนี้ กรุณาติดต่อผู้ดูแลองค์กร และอย่าเปิดเผยรหัสแก่ผู้อื่น."),
            _ => new CredentialCopy(
                "ตั้งรหัสผ่านใหม่",
                "ใช้รหัสนี้เพื่อดำเนินการตั้งรหัสผ่านใหม่",
                "รหัสยืนยัน",
                "รหัสนี้ใช้ได้ 24 ชั่วโมง",
                "หากคุณไม่ได้ร้องขอเปลี่ยนรหัสผ่าน กรุณาละเว้นอีเมลนี้ และอย่าเปิดเผยรหัสแก่ผู้อื่น.")
        };

        var safeCode = WebUtility.HtmlEncode(code);
        var actionText = template == "activation" && activationUrl is not null
            ? $"\nหน้าเปิดใช้งานบัญชี: {activationUrl.AbsoluteUri}"
            : string.Empty;
        var text = $"{content.Title}\n\n{content.Description}\n\n" +
            $"{content.CodeLabel}: {code}\n{content.Expiry}{actionText}\n\n{content.SecurityNote}\n\n{Brand}";
        var actionHtml = template == "activation" && activationUrl is not null
            ? $"""
              <table role="presentation" cellpadding="0" cellspacing="0" style="margin-top:22px"><tr><td style="background:{BrandColor};border-radius:8px">
                <a href="{WebUtility.HtmlEncode(activationUrl.AbsoluteUri)}" style="display:inline-block;padding:12px 20px;color:#ffffff;font-size:15px;font-weight:700;text-decoration:none">เปิดหน้าเปิดใช้งานบัญชี</a>
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
        var subject = $"รายงาน Timesheet พร้อมใช้งาน: {period}";
        var text = $"เรียน {employeeName}\n\nรายงาน Timesheet ประจำช่วง {period} พร้อมใช้งานแล้ว\n" +
            $"เปิดดูรายงาน: {reportUri.AbsoluteUri}\nเลขอ้างอิง: {reportRunId:D}\n\n{Brand}";
        var body = $"""
            <p style="margin:0 0 18px;color:{Muted};font-size:16px;line-height:1.7">เรียน {safeName}</p>
            <p style="margin:0 0 22px;color:{Muted};font-size:16px;line-height:1.7">รายงาน Timesheet ประจำช่วง <strong style="color:{Ink}">{safePeriod}</strong> พร้อมใช้งานแล้ว</p>
            <table role="presentation" cellpadding="0" cellspacing="0"><tr><td style="background:{BrandColor};border-radius:8px">
              <a href="{safeUrl}" style="display:inline-block;padding:12px 20px;color:#ffffff;font-size:15px;font-weight:700;text-decoration:none">เปิดรายงาน</a>
            </td></tr></table>
            <p style="margin:22px 0 0;color:{Muted};font-size:12px;line-height:1.7">เลขอ้างอิงรายงาน: {reportRunId:D}</p>
            """;
        return Create(subject, text, body);
    }

    private static EmailContent Create(string title, string text, string body)
    {
        var safeTitle = WebUtility.HtmlEncode(title);
        var html = $"""
            <!doctype html>
            <html lang="th">
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
                      อีเมลนี้ส่งโดยระบบอัตโนมัติ กรุณาอย่าตอบกลับ<br>{Brand}
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
