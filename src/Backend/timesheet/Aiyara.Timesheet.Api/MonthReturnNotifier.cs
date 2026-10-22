using System.Globalization;
using System.Net.Http.Json;
using Aiyara.Timesheet.Contracts.Identity.V1;

namespace Aiyara.Timesheet.Api;

public interface IMonthReturnNotifier
{
    Task MonthReturnedAsync(Guid tenantId, Guid ownerId, int year, int month, string reason, CancellationToken ct);
}

/// <summary>Emails a member that a Tenant Admin returned their month: the address from Identity, the
/// email sent by Notification. Never throws, and logs a failure without the address or the reason.</summary>
public sealed class MonthReturnNotifier(IdentityValidationService.IdentityValidationServiceClient identity,
    IHttpClientFactory clients, IConfiguration configuration, ILogger<MonthReturnNotifier> logger) : IMonthReturnNotifier
{
    public async Task MonthReturnedAsync(Guid tenantId, Guid ownerId, int year, int month, string reason,
        CancellationToken ct)
    {
        try
        {
            var profile = await identity.LookupProfileAsync(new LookupProfileRequest
            {
                SubjectId = ownerId.ToString(), TenantId = tenantId.ToString(), CorrelationId = Guid.NewGuid().ToString()
            }, cancellationToken: ct);
            if (!profile.Found || !profile.IsActive || string.IsNullOrWhiteSpace(profile.Email)) return;
            using var message = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/month-returned")
            {
                Content = JsonContent.Create(new MonthReturnedEmail(profile.Email,
                    $"{profile.FirstName} {profile.LastName}".Trim(),
                    new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture), reason,
                    configuration["Notifications:TimesheetUrl"] ?? "http://localhost:3000/remote/timesheet"))
            };
            message.Headers.Add("X-Internal-Key", configuration["Notifications:InternalKey"] ?? "");
            using var response = await clients.CreateClient("notifications").SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Month-returned email was not accepted: {StatusCode}.", (int)response.StatusCode);
        }
        catch (Exception error)
        {
            logger.LogWarning("Month-returned email failed: {FailureType}.", error.GetType().Name);
        }
    }

    private sealed record MonthReturnedEmail(string Email, string MemberName, string Period, string Reason,
        string TimesheetUrl);
}
