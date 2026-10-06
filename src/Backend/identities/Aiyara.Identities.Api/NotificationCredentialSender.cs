using System.Net.Http.Json;
using Aiyara.Identities.Services.Authentication;

internal sealed class NotificationCredentialSender(
    HttpClient client, IConfiguration configuration) : ICredentialNotificationSender
{
    public async Task SendAsync(string email, string template, string code,
        CancellationToken cancellationToken)
    {
        var key = configuration["Notification:InternalKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            throw new AuthenticationException(AuthenticationFailure.Unavailable,
                "Credential email delivery is unavailable.");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "/internal/v1/credential-email")
        {
            Content = JsonContent.Create(new { email, template, code })
        };
        request.Headers.Add("X-Internal-Key", key);
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new AuthenticationException(AuthenticationFailure.Unavailable,
                    "Credential email delivery is unavailable.");
        }
        catch (HttpRequestException)
        {
            throw new AuthenticationException(AuthenticationFailure.Unavailable,
                "Credential email delivery is unavailable.");
        }
    }
}
