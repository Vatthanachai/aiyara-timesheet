using System.Net.Http.Json;
using Aiyara.Timesheet.Contracts.Identity.V1;

namespace Aiyara.Timesheet.Api;

/// <summary>Runs <see cref="MissingEntryReminders"/> once a minute.</summary>
public sealed class ReminderWorker(IServiceScopeFactory scopes, ILogger<ReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<MissingEntryReminders>()
                    .RunAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning("Reminder run failed: {FailureType}.", error.GetType().Name);
            }
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}

/// <summary>The organization's time zone and members from Identity over gRPC.</summary>
public sealed class GrpcReminderDirectory(IdentityValidationService.IdentityValidationServiceClient identity)
    : IReminderDirectory
{
    public async Task<string?> TimeZoneAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = await identity.GetOrganizationSettingsAsync(new GetOrganizationSettingsRequest
        {
            TenantId = tenantId.ToString(), CorrelationId = Guid.NewGuid().ToString()
        }, cancellationToken: ct);
        return settings.Found && !string.IsNullOrWhiteSpace(settings.TimeZoneId) ? settings.TimeZoneId : null;
    }

    public async Task<IReadOnlyList<ReminderMember>> MembersAsync(Guid tenantId, CancellationToken ct)
    {
        var response = await identity.ListMembersAsync(new ListMembersRequest
        {
            TenantId = tenantId.ToString(), CorrelationId = Guid.NewGuid().ToString()
        }, cancellationToken: ct);
        return response.Members
            .Where(x => Guid.TryParse(x.SubjectId, out _) && !string.IsNullOrWhiteSpace(x.Email))
            .Select(x => new ReminderMember(Guid.Parse(x.SubjectId), x.Email, $"{x.FirstName} {x.LastName}".Trim(),
                x.JoinedAtUtc?.ToDateTime() ?? DateTime.MinValue))
            .ToList();
    }
}

/// <summary>Sends reminders through Notification. Never throws, and logs a failure without the address.</summary>
public sealed class ReminderEmailSender(IHttpClientFactory clients, IConfiguration configuration,
    ILogger<ReminderEmailSender> logger) : IReminderSender
{
    public async Task SendAsync(ReminderEmail email, CancellationToken ct)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/timesheet-reminder")
            {
                Content = JsonContent.Create(new
                {
                    email.Email, email.MemberName, email.Kind, email.Period, email.LoggedHours, email.ExpectedHours,
                    email.MissingDays,
                    TimesheetUrl = configuration["Notifications:TimesheetUrl"] ?? "http://localhost:3000/remote/timesheet"
                })
            };
            message.Headers.Add("X-Internal-Key", configuration["Notifications:InternalKey"] ?? "");
            using var response = await clients.CreateClient("notifications").SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Reminder email was not accepted: {StatusCode}.", (int)response.StatusCode);
        }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Reminder email failed: {FailureType}.", error.GetType().Name);
        }
    }
}
