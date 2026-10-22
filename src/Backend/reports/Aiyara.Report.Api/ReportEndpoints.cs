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
/// <summary>SubjectUserId reports on one member. Tenant Admins may instead report on a member group
/// with GroupName and GroupMemberIds (1–<see cref="RunQuery.MaxSubjectUserIds"/> accounts); Report
/// does not know about groups, so the caller sends the group's current members and the run keeps
/// them. Neither means the whole organization (admins) or yourself (employees).</summary>
public sealed record RunRequest(Guid DefinitionId, DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc, Guid? SubjectUserId, string? GroupName = null,
    IReadOnlyCollection<Guid>? GroupMemberIds = null);
/// <summary>GroupId schedules the report for a member group instead of the whole organization.</summary>
public sealed record ScheduleRequest(Guid ReportDefinitionId, string TimeZoneId, TimeOnly LocalTime,
    Guid? GroupId = null);
public sealed record RetentionRequest(int Years);
/// <summary>One member's month as the caller already sees it in Reporting, rendered as the same PDF as
/// a generated monthly report. Report reads and stores nothing for it: like the page's XLSX and CSV
/// exports, the file holds exactly what the caller sent. DateFormat and TimeFormat are the
/// organization's (<see cref="Services.ReportFormats"/>); StandardDayMinutes is its normal working day,
/// which marks overtime days. Locale is <c>th</c> or <c>en</c> (the default): the language of the PDF's
/// fixed words, as chosen on the page.</summary>
public sealed record TimesheetPdfRequest(string MemberName, string Period, IReadOnlyList<TimesheetPdfLine> Lines,
    string? Department = null, int? StandardDayMinutes = null, string? DateFormat = null, string? TimeFormat = null,
    string? Locale = null)
{
    public const int MaxLines = 1000;
}
/// <summary>Kind is work, leave, holiday, or dayoff. Start and End are HH:mm for work and empty
/// otherwise; Task names the work, the leave kind, or the day off.</summary>
public sealed record TimesheetPdfLine(DateOnly Date, string? Start, string? End, int DurationMinutes, string Task,
    string? Detail = null, string? Notes = null, string? Project = null, string? Category = null, string Kind = "work");
/// <summary>SubjectUserIds, when given, keeps only runs about those members (at most
/// <see cref="MaxSubjectUserIds"/>), so a caller can show one member group's reports; runs about
/// the whole organization have no subject and are then left out.</summary>
public sealed record RunQuery(ReportRunStatus? Status, Guid? DefinitionId, int Page = 1, int PageSize = 20,
    IReadOnlyCollection<Guid>? SubjectUserIds = null)
{
    public const int MaxSubjectUserIds = ReportGroupLimits.MaxMembers;
}

public sealed record ReportActor(Guid TenantId, Guid UserId, string Role, string TimeZoneId)
{
    public const string ContextKey = "ValidatedReportActor";
    // Tenant data is managed only by that tenant's admins; Platform Admins manage
    // organizations through Identity's platform routes, never tenant settings.
    public bool IsAdmin => Role is "TenantAdmin";
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
