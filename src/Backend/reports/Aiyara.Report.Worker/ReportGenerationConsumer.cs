using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aiyara.Report.Databases;
using Aiyara.Report.Models;
using Aiyara.Report.Services;
using Aiyara.Timesheet.Contracts.Messaging.V1;
using Aiyara.Timesheet.Contracts.Identity.V1;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Aiyara.Report.Worker;

internal static class ReportRabbit
{
    public const string CommandsExchange = "aiyara.reporting.commands";
    public const string GenerateQueue = "reporting.generate.v1";

    public static ConnectionFactory Factory(IConfiguration configuration) => new()
    {
        HostName = configuration["RabbitMQ:Host"] ?? "localhost",
        UserName = configuration["RabbitMQ:User"] ?? "guest",
        Password = configuration["RabbitMQ:Password"] ?? "guest",
        ClientProvidedName = "aiyara-report-worker"
    };
}

internal sealed class ReportGenerationConsumer(IServiceScopeFactory scopes,
    IConfiguration configuration, IReportObjectStorage storage, IHttpClientFactory clients,
    IdentityValidationService.IdentityValidationServiceClient identity,
    ILogger<ReportGenerationConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await ReportRabbit.Factory(configuration).CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await channel.ExchangeDeclareAsync(ReportRabbit.CommandsExchange, ExchangeType.Topic, durable: true,
                    cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(ReportRabbit.GenerateQueue, durable: true, exclusive: false,
                    autoDelete: false, arguments: null, cancellationToken: stoppingToken);
                await channel.QueueBindAsync(ReportRabbit.GenerateQueue, ReportRabbit.CommandsExchange,
                    MessageTypes.ReportGenerationRequested, cancellationToken: stoppingToken);
                await channel.BasicQosAsync(0, 1, false, stoppingToken);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    try
                    {
                        var envelope = JsonSerializer.Deserialize<MessageEnvelope<ReportGenerationRequestedV1>>(
                            delivery.Body.Span) ?? throw new InvalidDataException("Invalid report command.");
                        await ProcessAsync(envelope, stoppingToken);
                        await channel.BasicAckAsync(delivery.DeliveryTag, false, stoppingToken);
                    }
                    catch (Exception error)
                    {
                        logger.LogWarning("Report generation failed: {FailureType}", error.GetType().Name);
                        var retry = await RecordFailureAsync(delivery.Body, error, stoppingToken);
                        await channel.BasicNackAsync(delivery.DeliveryTag, false, retry, stoppingToken);
                    }
                };
                await channel.BasicConsumeAsync(ReportRabbit.GenerateQueue, false, consumer, stoppingToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning("Report queue connection failed: {FailureType}", error.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    internal async Task ProcessAsync(MessageEnvelope<ReportGenerationRequestedV1> envelope,
        CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<ReportingTenantScope>().TenantId = envelope.TenantId;
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var run = await db.ReportRuns.SingleOrDefaultAsync(x => x.Id == envelope.Payload.ReportRunId, cancellationToken)
            ?? throw new InvalidDataException("Report run was not found.");
        if (run.Status == ReportRunStatus.Succeeded) return;
        run.Status = ReportRunStatus.Running;
        run.AttemptCount++;
        run.FailureReason = null;
        await db.SaveChangesAsync(cancellationToken);

        var definition = await db.ReportDefinitions.SingleAsync(x => x.Id == run.ReportDefinitionId, cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(run.TimeZoneId);
        var startLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(run.PeriodStartUtc, DateTimeKind.Utc), zone));
        var endLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(run.PeriodEndUtc.AddTicks(-1), DateTimeKind.Utc), zone));
        // Each snapshot with its owner; reports about several people (the whole organization or a
        // member group) need the owner to name each row.
        var snapshots = new List<(Guid? Owner, string Json)>();
        var groupMembers = run.SubjectGroupMemberIdsJson is { } membersJson
            ? JsonSerializer.Deserialize<HashSet<Guid>>(membersJson) : null;
        var http = clients.CreateClient("timesheet");
        for (var month = new DateOnly(startLocal.Year, startLocal.Month, 1);
             month <= new DateOnly(endLocal.Year, endLocal.Month, 1); month = month.AddMonths(1))
        {
            var path = run.SubjectUserId is { } subject
                ? $"/internal/v1/report-snapshots/{subject:D}/{month.Year}/{month.Month}"
                : $"/internal/v1/report-snapshots/{month.Year}/{month.Month}";
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-Tenant-Id", envelope.TenantId.ToString());
            request.Headers.Add("X-Internal-Service-Key", configuration["Reporting:InternalKey"]);
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new SourceMonthNotLockedException(ReportRunReasons.SourceMonthNotLocked(month));
            response.EnsureSuccessStatusCode();
            if (run.SubjectUserId is null)
            {
                var owners = (await response.Content.ReadFromJsonAsync<SnapshotEnvelope<List<SnapshotPayload>>>(
                    cancellationToken: cancellationToken))?.Result ?? [];
                // A group report reads the whole organization's month and keeps only its members.
                snapshots.AddRange(owners.Where(x => groupMembers is null || groupMembers.Contains(x.OwnerId))
                    .Select(x => ((Guid?)x.OwnerId, x.PayloadJson)));
            }
            else
            {
                var snapshot = await response.Content.ReadFromJsonAsync<SnapshotEnvelope<JsonElement>>(
                    cancellationToken: cancellationToken);
                snapshots.Add((run.SubjectUserId, snapshot?.Result.GetRawText()
                    ?? throw new JsonException("Missing snapshot result.")));
            }
        }

        var memberNames = new Dictionary<Guid, string>();
        var namesRows = run.SubjectUserId is null;
        if (namesRows)
            foreach (var member in snapshots.Select(x => x.Owner!.Value).Distinct())
                memberNames[member] = await MemberNameAsync(envelope.TenantId, member, run.Id, cancellationToken);
        var lines = snapshots.SelectMany(snapshot => ReportDocumentRenderer.ReadSnapshot(snapshot.Json)
                .Select(line => !namesRows ? line : line with { Member = memberNames[snapshot.Owner!.Value] }))
            .Where(x => x.Date >= startLocal && x.Date <= endLocal)
            .OrderBy(x => x.Date).ThenBy(x => x.Member).ThenBy(x => x.Start).ToArray();
        var payload = JsonSerializer.Serialize(new { tenantId = envelope.TenantId,
            subjectUserId = run.SubjectUserId, subjectGroupName = run.SubjectGroupName,
            subjectGroupMemberIds = groupMembers, run.PeriodStartUtc, run.PeriodEndUtc,
            sourceSnapshots = snapshots.Select(x => x.Json) });
        var employeeName = groupMembers is null ? "Tenant summary" : $"Group summary ({groupMembers.Count} members)";
        var department = run.SubjectGroupName ?? "";
        string? employeeEmail = null;
        if (run.SubjectUserId is { } owner)
        {
            var profile = await identity.LookupProfileAsync(new LookupProfileRequest
            {
                SubjectId = owner.ToString(), TenantId = envelope.TenantId.ToString(),
                CorrelationId = run.Id.ToString()
            }, cancellationToken: cancellationToken);
            employeeName = profile.Found
                ? $"{profile.FirstName} {profile.LastName}".Trim() : owner.ToString("D");
            if (profile.Found && profile.IsActive) employeeEmail = profile.Email;
        }
        var data = new ReportDocumentData(employeeName, department,
            $"{startLocal:yyyy-MM-dd} – {endLocal:yyyy-MM-dd}", lines,
            snapshots.Sum(x => ReportDocumentRenderer.CountLeaveDays(x.Json)));
        var bytes = definition.Format == ReportFormat.Pdf
            ? ReportDocumentRenderer.RenderPdf(data) : ReportDocumentRenderer.RenderXlsx(data);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var contentType = definition.Format == ReportFormat.Pdf ? "application/pdf" :
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        var extension = definition.Format == ReportFormat.Pdf ? "pdf" : "xlsx";
        var key = $"{envelope.TenantId:N}/{run.Id:N}/generated-v1.{extension}";
        await storage.EnsureBucketAsync(cancellationToken);
        await using (var content = new MemoryStream(bytes, writable: false))
            await storage.PutAsync(key, content, contentType, cancellationToken);

        var created = DateTime.UtcNow;
        var years = await db.RetentionPolicies.Select(x => x.Years).SingleOrDefaultAsync(cancellationToken);
        db.ReportSnapshots.Add(new ReportSnapshot { TenantId = envelope.TenantId, ReportRunId = run.Id,
            PayloadJson = payload, Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant(),
            CreatedAtUtc = created });
        db.ReportObjects.Add(new ReportObject { TenantId = envelope.TenantId, ReportRunId = run.Id,
            ObjectKey = key, Sha256 = hash, ContentType = contentType, LengthBytes = bytes.LongLength,
            Version = 1, CreatedAtUtc = created, RetainUntilUtc = created.AddYears(years == 0 ? 7 : years) });
        run.Status = ReportRunStatus.Succeeded;
        run.CompletedAtUtc = created;
        await db.SaveChangesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(employeeEmail))
        {
            try
            {
                var sender = new ReportReadyNotificationSender(
                    clients.CreateClient("notifications"),
                    configuration["Notifications:InternalKey"] ?? "",
                    TimeSpan.FromSeconds(1));
                var sent = await sender.SendAsync(new ReportReadyNotification(employeeEmail,
                    employeeName, data.Period,
                    configuration["Notifications:ReportUrl"] ?? "http://localhost:3000", run.Id),
                    cancellationToken);
                if (!sent)
                    logger.LogWarning("Report-ready notification was not accepted for run {ReportRunId} after retries.", run.Id);
            }
            catch (Exception error)
            {
                logger.LogWarning("Report-ready notification failed for run {ReportRunId}: {FailureType}.",
                    run.Id, error.GetType().Name);
            }
        }
    }

    /// <summary>A group member's name from Identity, else their email, else their account ID (for
    /// someone who has since left the organization).</summary>
    private async Task<string> MemberNameAsync(Guid tenantId, Guid accountId, Guid runId,
        CancellationToken cancellationToken)
    {
        var profile = await identity.LookupProfileAsync(new LookupProfileRequest
        {
            SubjectId = accountId.ToString(), TenantId = tenantId.ToString(), CorrelationId = runId.ToString()
        }, cancellationToken: cancellationToken);
        if (!profile.Found) return accountId.ToString("D");
        var name = $"{profile.FirstName} {profile.LastName}".Trim();
        return name.Length > 0 ? name : profile.Email;
    }

    private sealed record SnapshotPayload(Guid OwnerId, string PayloadJson, DateTime CreatedAtUtc);
    private sealed record SnapshotEnvelope<T>(T Result);

    private async Task<bool> RecordFailureAsync(ReadOnlyMemory<byte> body, Exception error,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<MessageEnvelope<ReportGenerationRequestedV1>>(body.Span);
            if (envelope is null) return false;
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ReportingTenantScope>().TenantId = envelope.TenantId;
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var run = await db.ReportRuns.SingleOrDefaultAsync(x => x.Id == envelope.Payload.ReportRunId, cancellationToken);
            if (run is null) return false;
            if (error is SourceMonthNotLockedException)
            {
                run.Status = ReportRunStatus.Queued;
                run.AttemptCount = Math.Max(0, run.AttemptCount - 1);
                run.FailureReason = error.Message;
                await db.SaveChangesAsync(cancellationToken);
                return false; // The minute recovery scan republishes it after source data is locked.
            }
            run.FailureReason = error is InvalidOperationException ? error.Message : "Report generation failed.";
            if (run.AttemptCount >= 5)
            {
                run.Status = ReportRunStatus.Failed;
                run.CompletedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return false;
            }
            run.Status = ReportRunStatus.Queued;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch { return true; }
    }
}

internal sealed class SourceMonthNotLockedException(string message) : InvalidOperationException(message);
