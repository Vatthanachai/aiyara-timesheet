using System.Net.Http.Json;
using System.Text.Json;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Contracts.Onboarding.V1;

public sealed class IdentityOnboardingClient(HttpClient client) : IDisposable
{
    public async Task<ServiceResponse> CreateTenantAsync(CreateTenantRequest request, CancellationToken cancellationToken)
        => await ForwardAsync("api/v1/tenants", request, cancellationToken);

    public async Task<ServiceResponse> AcceptInvitationAsync(AcceptInvitationRequest request,
        CancellationToken cancellationToken)
        => await ForwardAsync("api/v1/invitations/accept", request, cancellationToken);

    public async Task<ServiceResponse> IssueInvitationAsync(Guid tenantId,
        IssueInvitationRequest request, string? onboardingKey, CancellationToken cancellationToken)
        => await ForwardAsync($"api/v1/tenants/{tenantId}/invitations", request,
            cancellationToken, onboardingKey);

    private async Task<ServiceResponse> ForwardAsync<T>(string path, T request,
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
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return new ServiceOkResponse<object?>(null, 204);
            using var body = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = body.RootElement;
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                if (!root.TryGetProperty("result", out var result))
                    return new ServiceErrorResponse(502, "INVALID_UPSTREAM_RESPONSE", "Identity service is unavailable.");
                return new ServiceOkResponse<JsonElement>(result.Clone(), status,
                    response.Headers.Location?.ToString());
            }
            var code = root.TryGetProperty("errorCode", out var errorCode)
                ? errorCode.GetString() ?? "UPSTREAM_ERROR" : "UPSTREAM_ERROR";
            return new ServiceErrorResponse(status, code, "The request could not be completed.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return new ServiceErrorResponse(502, "IDENTITY_UNAVAILABLE", "Identity service is unavailable.");
        }
    }

    public void Dispose() => client.Dispose();
}
