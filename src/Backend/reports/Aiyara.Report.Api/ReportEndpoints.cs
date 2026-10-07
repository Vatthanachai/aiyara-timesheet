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

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Reports");
        api.MapGet("/context", (HttpContext ctx) => !ReportActor.TryRead(ctx, out var actor)
            ? Results.Unauthorized() : Results.Ok(new { actor.TenantId, actor.UserId,
                actor.TimeZoneId, actor.IsAdmin }));
        api.MapGet("/definitions", async (HttpContext ctx, ReportingDbContext db) =>
        {
            if (!ReportActor.TryRead(ctx, out _)) return Results.Unauthorized();
            return Results.Ok(await db.ReportDefinitions.OrderBy(x => x.Kind).ThenBy(x => x.Format).ToListAsync());
        });
        api.MapGet("/dashboard", async (HttpContext ctx, ReportingDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (!ReportActor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            var query = db.ReportRuns.AsNoTracking();
            if (!actor.IsAdmin) query = query.Where(x => x.SubjectUserId == actor.UserId);
            var counts = await query.GroupBy(x => x.Status).Select(group => new
                { Status = group.Key, Count = group.Count() }).ToListAsync(cancellationToken);
            int Count(ReportRunStatus status) => counts.FirstOrDefault(x => x.Status == status)?.Count ?? 0;
            return Results.Ok(new { Total = counts.Sum(x => x.Count),
                Ready = Count(ReportRunStatus.Succeeded), Queued = Count(ReportRunStatus.Queued) +
                    Count(ReportRunStatus.Running), Failed = Count(ReportRunStatus.Failed),
                Recent = await query.OrderByDescending(x => x.CreatedAtUtc).Take(5)
                    .Select(x => new { x.Id, x.Status, x.CreatedAtUtc }).ToListAsync(cancellationToken) });
        });
        api.MapPost("/definitions", async (HttpContext ctx, ReportingDbContext db,
            DefinitionRequest body) =>
        {
            if (!ReportActor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (!actor.IsAdmin) return Results.StatusCode(403);
            if (!Enum.IsDefined(body.Kind) || !Enum.IsDefined(body.Format) ||
                string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 160) return Results.BadRequest();
            var definition = new ReportDefinition { TenantId = actor.TenantId, Kind = body.Kind,
                Format = body.Format, Name = body.Name.Trim(), CreatedAtUtc = DateTime.UtcNow };
            db.ReportDefinitions.Add(definition);
            Audit(db, actor, "definition.created", null, new { definition.Id, definition.Kind, definition.Format });
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/definitions/{definition.Id}", definition);
        });
        api.MapPut("/definitions/{id:guid}", async (HttpContext ctx, ReportingDbContext db,
            Guid id, DefinitionRequest body) =>
        {
            if (!ReportActor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (!actor.IsAdmin) return Results.StatusCode(403);
            var definition = await db.ReportDefinitions.SingleOrDefaultAsync(x => x.Id == id);
            if (definition is null) return Results.NotFound();
            if (!Enum.IsDefined(body.Kind) || !Enum.IsDefined(body.Format) ||
                string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 160) return Results.BadRequest();
            var before = new { definition.Name, definition.Kind, definition.Format };
            definition.Name = body.Name.Trim(); definition.Kind = body.Kind; definition.Format = body.Format;
            Audit(db, actor, "definition.updated", null, new { before, after = new { definition.Name, definition.Kind, definition.Format } });
            await db.SaveChangesAsync();
            return Results.Ok(definition);
        });
        api.MapDelete("/definitions/{id:guid}", async (HttpContext ctx, ReportingDbContext db, Guid id) =>
        {
            if (!ReportActor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (!actor.IsAdmin) return Results.StatusCode(403);
            var definition = await db.ReportDefinitions.SingleOrDefaultAsync(x => x.Id == id);
            if (definition is null) return Results.NotFound();
            definition.IsEnabled = false;
            Audit(db, actor, "definition.disabled", null, new { id });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapGet("/runs", async (HttpContext ctx, ReportingDbContext db, int? take) =>
        {
            if (!ReportActor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            var query = db.ReportRuns.AsNoTracking();
            if (!actor.IsAdmin) query = query.Where(x => x.SubjectUserId == actor.UserId);
            var limit = Math.Clamp(take ?? 50, 1, 100);
            var runs = await query.OrderByDescending(x => x.CreatedAtUtc).Take(limit)
                .Join(db.ReportDefinitions, run => run.ReportDefinitionId, definition => definition.Id,
                    (run, definition) => new { run.Id, run.SubjectUserId, run.Status, run.PeriodStartUtc,
                        run.PeriodEndUtc, run.AttemptCount, run.CreatedAtUtc, definition.Kind,
                        definition.Format, definition.Name })
                .ToListAsync();
            return Results.Ok(runs);
        });
        api.MapPost("/runs", async (HttpContext ctx, ReportingDbContext db,
            IConfiguration configuration, ReportObjectStorage storage, RunRequest body,
            CancellationToken cancellationToken) =>
        {
            if (!ReportActor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (body.PeriodStartUtc >= body.PeriodEndUtc ||
                body.PeriodEndUtc - body.PeriodStartUtc > TimeSpan.FromDays(367)) return Results.BadRequest();
            var subject = body.SubjectUserId ?? (actor.IsAdmin ? null : actor.UserId);
            if (!actor.IsAdmin && subject != actor.UserId) return Results.StatusCode(403);
            var definition = await db.ReportDefinitions.SingleOrDefaultAsync(x =>
                x.Id == body.DefinitionId && x.IsEnabled, cancellationToken);
            if (definition is null) return Results.NotFound("Report definition not found.");
            var key = $"manual:{definition.Id:N}:{subject?.ToString("N") ?? "tenant"}:{body.PeriodStartUtc:yyyyMMdd}:{body.PeriodEndUtc:yyyyMMdd}";
            var existing = await db.ReportRuns.SingleOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
            if (existing is not null) return Results.Accepted($"/api/v1/runs/{existing.Id}", existing);
            await storage.EnsureBucketAsync(cancellationToken);
            var run = new ReportRun { TenantId = actor.TenantId, ReportDefinitionId = definition.Id,
                SubjectUserId = subject, RequestedBy = actor.UserId, TimeZoneId = actor.TimeZoneId,
                IdempotencyKey = key,
                PeriodStartUtc = body.PeriodStartUtc.UtcDateTime, PeriodEndUtc = body.PeriodEndUtc.UtcDateTime,
                CreatedAtUtc = DateTime.UtcNow };
            db.ReportRuns.Add(run);
            Audit(db, actor, "run.requested", run.Id, new { definition.Kind, definition.Format,
                run.SubjectUserId, run.PeriodStartUtc, run.PeriodEndUtc });
            await db.SaveChangesAsync(cancellationToken);
            try { await ReportCommandPublisher.PublishAsync(configuration, actor.TenantId, run, definition, cancellationToken); }
            catch (Exception)
            {
                // The worker's scheduled recovery scan republishes queued runs.
            }
            return Results.Accepted($"/api/v1/runs/{run.Id}", run);
        });
        api.MapGet("/runs/{id:guid}/download", async (HttpContext ctx, ReportingDbContext db,
            ReportObjectStorage storage, Guid id, CancellationToken cancellationToken) =>
        {
            if (!ReportActor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            var run = await db.ReportRuns.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (run is null || !actor.IsAdmin && run.SubjectUserId != actor.UserId) return Results.NotFound();
            var file = await db.ReportObjects.AsNoTracking().Where(x => x.ReportRunId == id)
                .OrderByDescending(x => x.IsExternallySigned).ThenByDescending(x => x.Version)
                .FirstOrDefaultAsync(cancellationToken);
            if (file is null) return Results.NotFound();
            using var response = await storage.GetAsync(file.ObjectKey, cancellationToken);
            await using var content = new MemoryStream();
            await response.ResponseStream.CopyToAsync(content, cancellationToken);
            return Results.File(content.ToArray(), file.ContentType,
                $"report-{run.PeriodStartUtc:yyyy-MM-dd}.{(file.ContentType == "application/pdf" ? "pdf" : "xlsx")}");
        });
        api.MapPost("/runs/{id:guid}/signed-document", async (HttpContext ctx,
            ReportingDbContext db, ReportObjectStorage storage, Guid id, IFormFile file,
            CancellationToken cancellationToken) =>
        {
            if (!ReportActor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (!actor.IsAdmin) return Results.StatusCode(403);
            if (file.Length is < 5 or > 25 * 1024 * 1024 ||
                !string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("Upload a PDF no larger than 25 MB.");
            var run = await db.ReportRuns.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (run is null || run.Status != ReportRunStatus.Succeeded ||
                !await db.ReportDefinitions.AnyAsync(x => x.Id == run.ReportDefinitionId &&
                    x.Kind == ReportKind.Monthly && x.Format == ReportFormat.Pdf, cancellationToken))
                return Results.NotFound();
            await using var input = file.OpenReadStream();
            var bytes = await ReadBoundedAsync(input, 25 * 1024 * 1024, cancellationToken);
            if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) return Results.BadRequest("File is not a PDF.");
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var version = (await db.ReportObjects.Where(x => x.ReportRunId == id && x.IsExternallySigned)
                .Select(x => (int?)x.Version).MaxAsync(cancellationToken) ?? 0) + 1;
            var key = $"{actor.TenantId:N}/{id:N}/signed/v{version}.pdf";
            await storage.EnsureBucketAsync(cancellationToken);
            await using var content = new MemoryStream(bytes, writable: false);
            await storage.PutAsync(key, content, "application/pdf", cancellationToken);
            var entity = new ReportObject { TenantId = actor.TenantId, ReportRunId = id,
                ObjectKey = key, Sha256 = hash, ContentType = "application/pdf", LengthBytes = bytes.LongLength,
                Version = version, IsExternallySigned = true, CreatedAtUtc = DateTime.UtcNow,
                RetainUntilUtc = await RetainUntilAsync(db, actor.TenantId, cancellationToken) };
            db.ReportObjects.Add(entity);
            Audit(db, actor, "signed-document.uploaded", id, new { entity.ObjectKey, entity.Version, entity.Sha256 });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/runs/{id}/documents/{entity.Id}", new { entity.Id, entity.Version, entity.Sha256 });
        }).DisableAntiforgery();
        api.MapGet("/schedules", async (HttpContext ctx, ReportingDbContext db) =>
        {
            if (!ReportActor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (!actor.IsAdmin) return Results.StatusCode(403);
            return Results.Ok(await db.ReportSchedules.Join(db.ReportDefinitions,
                schedule => schedule.ReportDefinitionId, definition => definition.Id,
                (schedule, definition) => new { schedule.Id, schedule.ReportDefinitionId,
                    schedule.TimeZoneId, schedule.LocalTime, schedule.NextFireAtUtc,
                    schedule.LastFireAtUtc, schedule.IsEnabled, definition.Kind, definition.Format, definition.Name })
                .OrderBy(x => x.Name).ToListAsync());
        });
        api.MapPost("/schedules", async (HttpContext ctx, ReportingDbContext db,
            ScheduleRequest body) =>
        {
            if (!ReportActor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (!actor.IsAdmin) return Results.StatusCode(403);
            var definition = await db.ReportDefinitions.SingleOrDefaultAsync(x =>
                x.Id == body.ReportDefinitionId && x.IsEnabled);
            if (definition is null || definition.Kind == ReportKind.Performance ||
                !TimeZoneInfo.TryFindSystemTimeZoneById(body.TimeZoneId, out _)) return Results.BadRequest();
            var next = ReportCalendar.NextFireUtc(definition.Kind, body.TimeZoneId, DateTimeOffset.UtcNow, body.LocalTime);
            var schedule = new ReportSchedule { TenantId = actor.TenantId,
                ReportDefinitionId = definition.Id, TimeZoneId = body.TimeZoneId,
                LocalTime = body.LocalTime, NextFireAtUtc = next.UtcDateTime };
            db.ReportSchedules.Add(schedule);
            Audit(db, actor, "schedule.created", null, new { schedule.Id, definition.Kind, schedule.NextFireAtUtc });
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/schedules/{schedule.Id}", schedule);
        });
        api.MapDelete("/schedules/{id:guid}", async (HttpContext ctx, ReportingDbContext db, Guid id) =>
        {
            if (!ReportActor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (!actor.IsAdmin) return Results.StatusCode(403);
            var schedule = await db.ReportSchedules.SingleOrDefaultAsync(x => x.Id == id);
            if (schedule is null) return Results.NotFound();
            schedule.IsEnabled = false;
            Audit(db, actor, "schedule.disabled", null, new { id });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapGet("/retention", async (HttpContext ctx, ReportingDbContext db) =>
        {
            if (!ReportActor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (!actor.IsAdmin) return Results.StatusCode(403);
            var policy = await db.RetentionPolicies.SingleOrDefaultAsync();
            return Results.Ok(policy ?? new ReportRetentionPolicy { TenantId = actor.TenantId, Years = 7 });
        });
        api.MapPut("/retention", async (HttpContext ctx, ReportingDbContext db, RetentionRequest body) =>
        {
            if (!ReportActor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (!actor.IsAdmin) return Results.StatusCode(403);
            if (body.Years is < 1 or > 20) return Results.BadRequest();
            var policy = await db.RetentionPolicies.SingleOrDefaultAsync();
            if (policy is null) { policy = new ReportRetentionPolicy { TenantId = actor.TenantId }; db.Add(policy); }
            policy.Years = body.Years; policy.UpdatedAtUtc = DateTime.UtcNow;
            Audit(db, actor, "retention.updated", null, new { policy.Years });
            await db.SaveChangesAsync();
            return Results.Ok(policy);
        });
    }

    private static async Task<DateTime> RetainUntilAsync(ReportingDbContext db, Guid tenantId,
        CancellationToken cancellationToken)
    {
        var years = await db.RetentionPolicies.Select(x => x.Years).SingleOrDefaultAsync(cancellationToken);
        return DateTime.UtcNow.AddYears(years == 0 ? 7 : years);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("The uploaded file exceeds 25 MB.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }

    private static void Audit(ReportingDbContext db, ReportActor actor, string action,
        Guid? runId, object details) => db.Audits.Add(new ReportAudit
        {
            TenantId = actor.TenantId, ActorId = actor.UserId, ReportRunId = runId,
            Action = action, DetailsJson = JsonSerializer.Serialize(details), OccurredAtUtc = DateTime.UtcNow
        });
}

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
