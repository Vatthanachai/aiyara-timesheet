using System.Net.Http.Json;
using Aiyara.Timesheet.Contracts.Onboarding.V1;

internal sealed class IdentityOnboardingClient(HttpClient client) : IDisposable
{
    public async Task<IResult> CreateTenantAsync(CreateTenantRequest request, CancellationToken cancellationToken)
        => await ForwardAsync("api/v1/tenants", request, cancellationToken);

    public async Task<IResult> AcceptInvitationAsync(AcceptInvitationRequest request,
        CancellationToken cancellationToken)
        => await ForwardAsync("api/v1/invitations/accept", request, cancellationToken);

    public async Task<IResult> IssueInvitationAsync(Guid tenantId,
        IssueInvitationRequest request, string? onboardingKey, CancellationToken cancellationToken)
        => await ForwardAsync($"api/v1/tenants/{tenantId}/invitations", request,
            cancellationToken, onboardingKey);

    private async Task<IResult> ForwardAsync<T>(string path, T request,
        CancellationToken cancellationToken, string? onboardingKey = null)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(request)
            };
            if (onboardingKey is not null)
            {
                message.Headers.TryAddWithoutValidation("X-Onboarding-Key", onboardingKey);
            }
            using var response = await client.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return Results.Problem("Identity service is unavailable.", statusCode: 502);
        }
    }

    public void Dispose() => client.Dispose();
}
