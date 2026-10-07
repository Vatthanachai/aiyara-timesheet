using System.Text;
using System.Text.Json;
using Aiyara.Timesheet.Contracts.Messaging.V1;
using Aiyara.Timesheet.Databases;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace Aiyara.Timesheet.Api;

public sealed class TimesheetOutboxPublisher(IServiceScopeFactory scopes,
    IConfiguration configuration, ILogger<TimesheetOutboxPublisher> logger) : BackgroundService
{
    private const string Exchange = "aiyara.timesheet.events";
    private const string ReportingQueue = "reporting.timesheet-events.v1";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = await PendingAsync(stoppingToken);
                if (pending.Count > 0)
                {
                    var factory = new ConnectionFactory
                    {
                        HostName = configuration["RabbitMQ:Host"] ?? "localhost",
                        UserName = configuration["RabbitMQ:User"] ?? "guest",
                        Password = configuration["RabbitMQ:Password"] ?? "guest",
                        ClientProvidedName = "aiyara-timesheet-outbox"
                    };
                    await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                    await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(
                        publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                        stoppingToken);
                    await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true,
                        cancellationToken: stoppingToken);
                    await channel.QueueDeclareAsync(ReportingQueue, durable: true, exclusive: false,
                        autoDelete: false, arguments: null, cancellationToken: stoppingToken);
                    await channel.QueueBindAsync(ReportingQueue, Exchange,
                        MessageTypes.TimesheetMonthChanged, cancellationToken: stoppingToken);
                    await channel.QueueBindAsync(ReportingQueue, Exchange,
                        MessageTypes.TimesheetMonthLocked, cancellationToken: stoppingToken);
                    foreach (var item in pending)
                    {
                        var body = Serialize(item);
                        var properties = new BasicProperties { Persistent = true,
                            ContentType = "application/json", MessageId = item.Id.ToString(),
                            CorrelationId = item.Id.ToString() };
                        await channel.BasicPublishAsync(Exchange, item.EventType, mandatory: true,
                            basicProperties: properties, body: body, cancellationToken: stoppingToken);
                        await MarkPublishedAsync(item, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // The durable outbox retains events for the next retry. Do not log
                // connection details because configuration may contain credentials.
                logger.LogWarning("Timesheet outbox delivery failed; retrying pending events.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task<List<TimesheetOutboxEvent>> PendingAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
        return await db.OutboxEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.PublishedAtUtc == null).OrderBy(x => x.OccurredAtUtc)
            .Take(100).ToListAsync(cancellationToken);
    }

    private async Task MarkPublishedAsync(TimesheetOutboxEvent item, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<TimesheetTenantScope>().TenantId = item.TenantId;
        var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
        var tracked = await db.OutboxEvents.SingleAsync(x => x.Id == item.Id, cancellationToken);
        tracked.PublishedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static byte[] Serialize(TimesheetOutboxEvent item)
    {
        var occurred = DateTime.SpecifyKind(item.OccurredAtUtc, DateTimeKind.Utc);
        var idempotencyKey = item.Id.ToString();
        object envelope = item.EventType == MessageTypes.TimesheetMonthLocked
            ? new MessageEnvelope<TimesheetMonthLockedV1>(item.Id, item.Id, idempotencyKey,
                item.TenantId, occurred, item.EventType,
                new TimesheetMonthLockedV1(item.TenantId, item.Year, item.Month))
            : new MessageEnvelope<TimesheetMonthChangedV1>(item.Id, item.Id, idempotencyKey,
                item.TenantId, occurred, item.EventType,
                new TimesheetMonthChangedV1(item.TenantId, item.SubjectId, item.Year, item.Month));
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, envelope.GetType()));
    }
}
