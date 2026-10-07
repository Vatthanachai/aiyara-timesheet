using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aiyara.Report.Databases;
using Aiyara.Report.Models;
using Aiyara.Report.Services;
using Aiyara.Timesheet.Contracts.Messaging.V1;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace Aiyara.Report.Api;

public sealed record DefinitionRequest(ReportKind Kind, ReportFormat Format, string Name);
public sealed record RunRequest(Guid DefinitionId, DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc, Guid? SubjectUserId);
public sealed record ScheduleRequest(Guid ReportDefinitionId, string TimeZoneId, TimeOnly LocalTime);
public sealed record RetentionRequest(int Years);

public sealed record ReportActor(Guid TenantId, Guid UserId, string Role, string TimeZoneId)
{
    public const string ContextKey = "ValidatedReportActor";
    public bool IsAdmin => Role is "TenantAdmin" or "PlatformAdmin";
    public static bool TryRead(HttpContext context, out ReportActor actor)
    {
        actor = context.Items[ContextKey] as ReportActor ?? default!;
        return actor is not null;
    }
}

internal static class ReportCommandPublisher
{
    private const string Exchange = "aiyara.reporting.commands";
    private const string Queue = "reporting.generate.v1";

    public static async Task PublishAsync(IConfiguration configuration, Guid tenantId,
        ReportRun run, ReportDefinition definition, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory { HostName = configuration["RabbitMQ:Host"] ?? "localhost",
            UserName = configuration["RabbitMQ:User"] ?? "guest",
            Password = configuration["RabbitMQ:Password"] ?? "guest" };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: null, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(Queue, Exchange, MessageTypes.ReportGenerationRequested,
            cancellationToken: cancellationToken);
        var message = new MessageEnvelope<ReportGenerationRequestedV1>(run.Id, run.Id,
            run.IdempotencyKey, tenantId, DateTime.UtcNow, MessageTypes.ReportGenerationRequested,
            new ReportGenerationRequestedV1(tenantId, run.Id, definition.Kind.ToString()));
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        await channel.BasicPublishAsync(Exchange, MessageTypes.ReportGenerationRequested, true,
            new BasicProperties { Persistent = true, ContentType = "application/json",
                MessageId = run.Id.ToString(), CorrelationId = run.Id.ToString() }, body,
            cancellationToken);
    }
}
