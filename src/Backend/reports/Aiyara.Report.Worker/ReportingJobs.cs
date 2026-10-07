using System.Text;
using System.Text.Json;
using Aiyara.Report.Databases;
using Aiyara.Report.Models;
using Aiyara.Report.Services;
using Aiyara.Timesheet.Contracts.Messaging.V1;
using Microsoft.EntityFrameworkCore;
using Quartz;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Aiyara.Report.Worker;

internal sealed class TimesheetMonthEventConsumer(IServiceScopeFactory scopes,
    IConfiguration configuration, ILogger<TimesheetMonthEventConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await ReportRabbit.Factory(configuration).CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await channel.ExchangeDeclareAsync("aiyara.timesheet.events", ExchangeType.Topic, durable: true,
                    cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync("reporting.timesheet-events.v1", durable: true, exclusive: false,
                    autoDelete: false, arguments: null, cancellationToken: stoppingToken);
                await channel.QueueBindAsync("reporting.timesheet-events.v1", "aiyara.timesheet.events",
                    MessageTypes.TimesheetMonthLocked, cancellationToken: stoppingToken);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    try
                    {
                        using var message = JsonDocument.Parse(delivery.Body);
                        if (message.RootElement.GetProperty("Type").GetString() == MessageTypes.TimesheetMonthLocked)
                        {
                            var tenantId = message.RootElement.GetProperty("TenantId").GetGuid();
                            var payload = message.RootElement.GetProperty("Payload");
                            await QueueMonthAsync(tenantId, payload.GetProperty("Year").GetInt32(),
                                payload.GetProperty("Month").GetInt32(),
                                payload.TryGetProperty("TimeZoneId", out var timezone)
                                    ? timezone.GetString() ?? "Asia/Bangkok" : "Asia/Bangkok", stoppingToken);
                        }
                        await channel.BasicAckAsync(delivery.DeliveryTag, false, stoppingToken);
                    }
                    catch (Exception error)
                    {
                        logger.LogWarning("Timesheet report event failed: {FailureType}", error.GetType().Name);
                        await channel.BasicNackAsync(delivery.DeliveryTag, false, true, stoppingToken);
                    }
                };
                await channel.BasicConsumeAsync("reporting.timesheet-events.v1", false, consumer, stoppingToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning("Timesheet event connection failed: {FailureType}", error.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task QueueMonthAsync(Guid tenantId, int year, int month, string timeZoneId,
        CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<ReportingTenantScope>().TenantId = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        using var http = new HttpClient { BaseAddress = new Uri(configuration["Timesheet:BaseUrl"] ?? "http://localhost:5198") };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/report-snapshots/{year}/{month}");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        request.Headers.Add("X-Internal-Service-Key", configuration["Reporting:InternalKey"]);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var owners = await response.Content.ReadFromJsonAsync<List<SnapshotOwner>>(cancellationToken: cancellationToken) ?? [];
        if (owners.Count == 0) return;
        var definition = await db.ReportDefinitions.SingleOrDefaultAsync(x =>
            x.Kind == ReportKind.Monthly && x.Format == ReportFormat.Pdf, cancellationToken);
        if (definition is null)
        {
            definition = new ReportDefinition { TenantId = tenantId, Kind = ReportKind.Monthly,
                Format = ReportFormat.Pdf, Name = "Monthly employee timesheet", CreatedAtUtc = DateTime.UtcNow };
            db.ReportDefinitions.Add(definition);
            await db.SaveChangesAsync(cancellationToken);
        }
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var start = TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, 1), zone);
        var end = TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, 1).AddMonths(1), zone);
        foreach (var owner in owners)
        {
            var key = $"month-lock:{year:D4}-{month:D2}:{owner.OwnerId:N}";
            if (await db.ReportRuns.AnyAsync(x => x.IdempotencyKey == key, cancellationToken)) continue;
            var run = new ReportRun { TenantId = tenantId, ReportDefinitionId = definition.Id,
                SubjectUserId = owner.OwnerId, RequestedBy = Guid.Empty, TimeZoneId = zone.Id,
                IdempotencyKey = key, PeriodStartUtc = start, PeriodEndUtc = end, CreatedAtUtc = DateTime.UtcNow };
            db.ReportRuns.Add(run);
            db.Audits.Add(new ReportAudit { TenantId = tenantId, ActorId = Guid.Empty,
                ReportRunId = run.Id, Action = "run.queued.month-locked", DetailsJson = "{}",
                OccurredAtUtc = DateTime.UtcNow });
        }
        await db.SaveChangesAsync(cancellationToken);
        var runs = await db.ReportRuns.Where(x => x.IdempotencyKey.StartsWith($"month-lock:{year:D4}-{month:D2}:"))
            .Where(x => x.Status == ReportRunStatus.Queued).ToListAsync(cancellationToken);
        await ReportCommandPublisher.PublishAsync(configuration, runs, definition, cancellationToken);
    }

    private sealed record SnapshotOwner(Guid OwnerId, string PayloadJson, DateTime CreatedAtUtc);
}

internal sealed class ScheduleDispatchJob(IServiceScopeFactory scopes,
    IConfiguration configuration) : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) =>
        ReportQuartzMetrics.MeasureAsync("schedule_dispatch", () => ExecuteCoreAsync(context));

    private async ValueTask ExecuteCoreAsync(IJobExecutionContext context)
    {
        using var scope = scopes.CreateScope();
        var tenantScope = scope.ServiceProvider.GetRequiredService<ReportingTenantScope>();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var due = await db.ReportSchedules.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.IsEnabled && x.NextFireAtUtc <= DateTime.UtcNow).ToListAsync(context.CancellationToken);
        foreach (var pending in due)
        {
            tenantScope.TenantId = pending.TenantId;
            var schedule = await db.ReportSchedules.SingleOrDefaultAsync(x => x.Id == pending.Id, context.CancellationToken);
            if (schedule is null || !schedule.IsEnabled || schedule.NextFireAtUtc > DateTime.UtcNow) continue;
            var definition = await db.ReportDefinitions.SingleAsync(x => x.Id == schedule.ReportDefinitionId, context.CancellationToken);
            var fired = new DateTimeOffset(DateTime.SpecifyKind(schedule.NextFireAtUtc!.Value, DateTimeKind.Utc));
            var period = ReportCalendar.ClosedPeriod(definition.Kind, schedule.TimeZoneId, fired);
            var key = $"schedule:{schedule.Id:N}:{period.StartUtc:yyyyMMdd}:{period.EndUtc:yyyyMMdd}";
            if (!await db.ReportRuns.AnyAsync(x => x.IdempotencyKey == key, context.CancellationToken))
            {
                var run = new ReportRun { TenantId = schedule.TenantId, ReportDefinitionId = definition.Id,
                    RequestedBy = Guid.Empty, TimeZoneId = schedule.TimeZoneId, IdempotencyKey = key,
                    PeriodStartUtc = period.StartUtc.UtcDateTime, PeriodEndUtc = period.EndUtc.UtcDateTime,
                    CreatedAtUtc = DateTime.UtcNow };
                db.ReportRuns.Add(run);
                db.Audits.Add(new ReportAudit { TenantId = schedule.TenantId, ActorId = Guid.Empty,
                    ReportRunId = run.Id, Action = "run.queued.schedule", DetailsJson = "{}",
                    OccurredAtUtc = DateTime.UtcNow });
            }
            schedule.LastFireAtUtc = schedule.NextFireAtUtc;
            schedule.NextFireAtUtc = ReportCalendar.NextFireUtc(definition.Kind, schedule.TimeZoneId,
                fired.AddTicks(1), schedule.LocalTime).UtcDateTime;
            await db.SaveChangesAsync(context.CancellationToken);
        }
        var queued = await db.ReportRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status == ReportRunStatus.Queued).OrderBy(x => x.CreatedAtUtc)
            .Take(100).ToListAsync(context.CancellationToken);
        foreach (var run in queued)
        {
            tenantScope.TenantId = run.TenantId;
            var definition = await db.ReportDefinitions.SingleAsync(x => x.Id == run.ReportDefinitionId, context.CancellationToken);
            await ReportCommandPublisher.PublishAsync(configuration, [run], definition, context.CancellationToken);
        }
    }
}

internal sealed class RetentionPurgeJob(IServiceScopeFactory scopes,
    IReportObjectStorage storage) : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) =>
        ReportQuartzMetrics.MeasureAsync("retention_purge", () => ExecuteAsync(context.CancellationToken));

    internal async ValueTask ExecuteAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var tenantScope = scope.ServiceProvider.GetRequiredService<ReportingTenantScope>();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var expired = await db.ReportObjects.IgnoreQueryFilters().Where(x => x.RetainUntilUtc <= DateTime.UtcNow)
            .Take(200).ToListAsync(cancellationToken);
        foreach (var item in expired)
        {
            tenantScope.TenantId = item.TenantId;
            await storage.DeleteAsync(item.ObjectKey, cancellationToken);
            db.ReportObjects.Remove(item);
            db.Audits.Add(new ReportAudit { TenantId = item.TenantId, ActorId = Guid.Empty,
                ReportRunId = item.ReportRunId, Action = "object.purged.retention",
                DetailsJson = JsonSerializer.Serialize(new { item.ObjectKey, item.Version, item.Sha256 }),
                OccurredAtUtc = DateTime.UtcNow });
            if (!await db.ReportObjects.AnyAsync(x => x.ReportRunId == item.ReportRunId &&
                    x.Id != item.Id && x.RetainUntilUtc > DateTime.UtcNow, cancellationToken))
            {
                var snapshot = await db.ReportSnapshots.SingleOrDefaultAsync(x =>
                    x.ReportRunId == item.ReportRunId, cancellationToken);
                if (snapshot is not null)
                {
                    db.Audits.Add(new ReportAudit { TenantId = item.TenantId, ActorId = Guid.Empty,
                        ReportRunId = item.ReportRunId, Action = "snapshot.purged.retention",
                        DetailsJson = JsonSerializer.Serialize(new { snapshot.Sha256 }),
                        OccurredAtUtc = DateTime.UtcNow });
                    db.ReportSnapshots.Remove(snapshot);
                }
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

internal static class ReportCommandPublisher
{
    public static async Task PublishAsync(IConfiguration configuration, IReadOnlyList<ReportRun> runs,
        ReportDefinition definition, CancellationToken cancellationToken)
    {
        if (runs.Count == 0) return;
        await using var connection = await ReportRabbit.Factory(configuration).CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(ReportRabbit.CommandsExchange, ExchangeType.Topic, durable: true,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(ReportRabbit.GenerateQueue, durable: true, exclusive: false,
            autoDelete: false, arguments: null, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(ReportRabbit.GenerateQueue, ReportRabbit.CommandsExchange,
            MessageTypes.ReportGenerationRequested, cancellationToken: cancellationToken);
        foreach (var run in runs)
        {
            var message = new MessageEnvelope<ReportGenerationRequestedV1>(run.Id, run.Id,
                run.IdempotencyKey, run.TenantId, DateTime.UtcNow, MessageTypes.ReportGenerationRequested,
                new ReportGenerationRequestedV1(run.TenantId, run.Id, definition.Kind.ToString()));
            await channel.BasicPublishAsync(ReportRabbit.CommandsExchange, MessageTypes.ReportGenerationRequested,
                mandatory: true, basicProperties: new BasicProperties { Persistent = true,
                    ContentType = "application/json", MessageId = run.Id.ToString(),
                    CorrelationId = run.Id.ToString() },
                body: Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)), cancellationToken);
        }
    }
}
