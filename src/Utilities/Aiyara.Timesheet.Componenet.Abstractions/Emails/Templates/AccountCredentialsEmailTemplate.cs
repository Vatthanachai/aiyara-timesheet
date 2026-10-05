namespace Aiyara.Timesheet.Component.Abstractions.Emails.Templates;

/// <summary>
/// Builds the HTML email used to deliver a system-generated password to a user
/// </summary>
public static class AccountCredentialsEmailTemplate
{
    public static string Build(string leadingMessage, string email, string password)
    {
        return $"""
            <html>
              <body style="font-family: Arial, sans-serif; color: #333333; line-height: 1.5;">
                <h2 style="color: #1a1a1a;">ShoppingOnline</h2>
                <p>{leadingMessage}</p>
                <table style="border-collapse: collapse; margin: 16px 0;">
                  <tr>
                    <td style="padding: 4px 12px 4px 0; color: #666666;">Email</td>
                    <td style="padding: 4px 0; font-weight: bold;">{email}</td>
                  </tr>
                  <tr>
                    <td style="padding: 4px 12px 4px 0; color: #666666;">Password</td>
                    <td style="padding: 4px 0; font-weight: bold;">{password}</td>
                  </tr>
                </table>
                <p>For your security, please sign in and change this password as soon as possible.</p>
                <p style="color: #999999; font-size: 12px;">If you did not request this, please contact support immediately.</p>
              </body>
            </html>
            """;
    }
}
