using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aiyara.Report.Databases;
using Aiyara.Report.Models;
using Aiyara.Report.Services;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Report.Api;

public sealed class ReportOperations(ReportingDbContext db, IConfiguration configuration,
    IReportObjectStorage storage)
{
    public async Task<ServiceResponse> Definitions(ReportActor actor, CancellationToken ct) =>
        Ok(await db.ReportDefinitions.Where(x => x.IsEnabled)
            .OrderBy(x => x.Kind).ThenBy(x => x.Format).ThenBy(x => x.Name).ToListAsync(ct));

    public async Task<ServiceResponse> Dashboard(ReportActor actor, CancellationToken ct)
    {
        var query = VisibleRuns(actor);
        var counts = await query.GroupBy(x => x.Status).Select(group => new
            { Status = group.Key, Count = group.Count() }).ToListAsync(ct);
        int Count(ReportRunStatus status) => counts.FirstOrDefault(x => x.Status == status)?.Count ?? 0;
        // Blocked runs stay Queued while the Worker waits for their source month to be locked.
        var blocked = await query.CountAsync(x => x.Status == ReportRunStatus.Queued &&
            x.FailureReason != null && x.FailureReason.StartsWith("Source month "), ct);
        return Ok(new { Total = counts.Sum(x => x.Count),
            Ready = Count(ReportRunStatus.Succeeded),
            Queued = Count(ReportRunStatus.Queued) + Count(ReportRunStatus.Running) - blocked,
            Blocked = blocked,
            Failed = Count(ReportRunStatus.Failed),
            Recent = await query.OrderByDescending(x => x.CreatedAtUtc).Take(5)
                .Select(x => new { x.Id, x.Status, x.CreatedAtUtc }).ToListAsync(ct) });
    }

    public async Task<ServiceResponse> CreateDefinition(ReportActor actor,
        DefinitionRequest body, CancellationToken ct)
    {
        if (!ValidDefinition(body)) return Error(400, "VALIDATION_FAILED");
        // One definition per kind and format (a unique index). A disabled one is enabled again
        // rather than duplicated.
        var existing = await db.ReportDefinitions.SingleOrDefaultAsync(x =>
            x.Kind == body.Kind && x.Format == body.Format, ct);
        if (existing is { IsEnabled: true }) return Error(409, "DEFINITION_EXISTS");
        if (existing is not null)
        {
            existing.IsEnabled = true;
            existing.Name = body.Name.Trim();
            Audit(actor, "definition.reenabled", null, new { existing.Id, existing.Name });
            await db.SaveChangesAsync(ct);
            return Ok(existing);
        }
        var definition = new ReportDefinition { TenantId = actor.TenantId, Kind = body.Kind,
            Format = body.Format, Name = body.Name.Trim(), CreatedAtUtc = DateTime.UtcNow };
        db.ReportDefinitions.Add(definition);
        Audit(actor, "definition.created", null, new { definition.Id, definition.Kind, definition.Format });
        // The Worker creates a tenant's default Monthly/PDF definition when a month is locked, so a
        // concurrent insert can still hit the unique index after the check above.
        if (!await TrySaveUniqueAsync(definition.Kind, definition.Format, definition.Id, ct))
            return Error(409, "DEFINITION_EXISTS");
        return Created(definition, $"/api/v1/definitions/{definition.Id}");
    }

    /// <summary>Saves, turning a unique-index race on (tenant, kind, format) into false instead of
    /// a 500. Any other failure is rethrown.</summary>
    private async Task<bool> TrySaveUniqueAsync(ReportKind kind, ReportFormat format, Guid id,
        CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await db.ReportDefinitions.AnyAsync(x => x.Kind == kind && x.Format == format && x.Id != id, ct))
                return false;
            throw;
        }
    }

    public async Task<ServiceResponse> UpdateDefinition(ReportActor actor, Guid id,
        DefinitionRequest body, CancellationToken ct)
    {
        var definition = await db.ReportDefinitions.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (definition is null) return Error(404, "NOT_FOUND");
        if (!ValidDefinition(body)) return Error(400, "VALIDATION_FAILED");
        if (await db.ReportDefinitions.AnyAsync(x => x.Id != id && x.Kind == body.Kind &&
                x.Format == body.Format, ct))
            return Error(409, "DEFINITION_EXISTS");
        var before = new { definition.Name, definition.Kind, definition.Format };
        definition.Name = body.Name.Trim(); definition.Kind = body.Kind; definition.Format = body.Format;
        Audit(actor, "definition.updated", null, new { before,
            after = new { definition.Name, definition.Kind, definition.Format } });
        if (!await TrySaveUniqueAsync(definition.Kind, definition.Format, definition.Id, ct))
            return Error(409, "DEFINITION_EXISTS");
        return Ok(definition);
    }

    public async Task<ServiceResponse> DeleteDefinition(ReportActor actor, Guid id, CancellationToken ct)
    {
        var definition = await db.ReportDefinitions.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (definition is null) return Error(404, "NOT_FOUND");
        definition.IsEnabled = false;
        Audit(actor, "definition.disabled", null, new { id });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public async Task<ServiceResponse> Runs(ReportActor actor, RunQuery request, CancellationToken ct)
    {
        if (request.Status is { } requested && !Enum.IsDefined(requested)) return Error(400, "VALIDATION_FAILED");
        if (request.SubjectUserIds is { Count: > RunQuery.MaxSubjectUserIds }) return Error(400, "VALIDATION_FAILED");
        var query = VisibleRuns(actor);
        if (request.Status is { } status) query = query.Where(x => x.Status == status);
        if (request.DefinitionId is { } definitionId) query = query.Where(x => x.ReportDefinitionId == definitionId);
        if (request.SubjectUserIds is { } subjects)
        {
            var ids = subjects.Distinct().Cast<Guid?>().ToList();
            query = query.Where(x => ids.Contains(x.SubjectUserId));
        }
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var totalCount = await query.CountAsync(ct);
        var page = Math.Clamp(request.Page, 1, Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize)));
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Join(db.ReportDefinitions, run => run.ReportDefinitionId, definition => definition.Id,
                (run, definition) => new { run.Id, run.SubjectUserId, run.SubjectGroupName, run.Status,
                    run.PeriodStartUtc, run.PeriodEndUtc, run.AttemptCount, run.CreatedAtUtc,
                    run.CompletedAtUtc, run.FailureReason, DefinitionId = definition.Id,
                    definition.Kind, definition.Format, definition.Name,
                    SignedVersion = db.ReportObjects.Where(x => x.ReportRunId == run.Id && x.IsExternallySigned)
                        .Max(x => (int?)x.Version) })
            .ToListAsync(ct);
        var items = rows.Select(row =>
        {
            var (reason, blockedMonth) = ReportRunReasons.Describe(row.Status, row.FailureReason);
            return new { row.Id, row.DefinitionId, row.SubjectUserId, row.SubjectGroupName, row.Status,
                StatusReason = reason, BlockedMonth = blockedMonth, row.PeriodStartUtc, row.PeriodEndUtc,
                row.AttemptCount, row.CreatedAtUtc, row.CompletedAtUtc, row.SignedVersion,
                row.Kind, row.Format, row.Name };
        }).ToList();
        return Ok(new { TotalCount = totalCount, Page = page, PageSize = pageSize, Items = items });
    }

    public async Task<ServiceResponse> CreateRun(ReportActor actor, RunRequest body, CancellationToken ct)
    {
        if (body.PeriodStartUtc >= body.PeriodEndUtc ||
            body.PeriodEndUtc - body.PeriodStartUtc > TimeSpan.FromDays(367))
            return Error(400, "VALIDATION_FAILED");
        var isGroup = body.GroupName is not null || body.GroupMemberIds is not null;
        if (isGroup && !actor.IsAdmin) return Error(403, "FORBIDDEN");
        var groupName = body.GroupName?.Trim();
        var groupMembers = body.GroupMemberIds?.Distinct().Order().ToArray();
        if (isGroup && (body.SubjectUserId is not null || groupName is not { Length: > 0 and <= 100 } ||
                groupMembers is not { Length: > 0 and <= RunQuery.MaxSubjectUserIds }))
            return Error(400, "VALIDATION_FAILED");
        var subject = body.SubjectUserId ?? (actor.IsAdmin ? null : actor.UserId);
        if (!actor.IsAdmin && subject != actor.UserId) return Error(403, "FORBIDDEN");
        var definition = await db.ReportDefinitions.SingleOrDefaultAsync(x =>
            x.Id == body.DefinitionId && x.IsEnabled, ct);
        if (definition is null) return Error(404, "NOT_FOUND");
        var groupMembersJson = groupMembers is null ? null : JsonSerializer.Serialize(groupMembers);
        // The same group (name and members) for the same period is the same report.
        var target = groupMembersJson is not null
            ? "group-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(groupName + "\n" + groupMembersJson)))[..32]
            : subject?.ToString("N") ?? "tenant";
        var key = $"manual:{definition.Id:N}:{target}:{body.PeriodStartUtc:yyyyMMdd}:{body.PeriodEndUtc:yyyyMMdd}";
        var existing = await db.ReportRuns.SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (existing is not null) return Accepted(existing, $"/api/v1/runs/{existing.Id}");
        await storage.EnsureBucketAsync(ct);
        var run = new ReportRun { TenantId = actor.TenantId, ReportDefinitionId = definition.Id,
            SubjectUserId = subject, SubjectGroupName = isGroup ? groupName : null,
            SubjectGroupMemberIdsJson = groupMembersJson, RequestedBy = actor.UserId, TimeZoneId = actor.TimeZoneId,
            IdempotencyKey = key, PeriodStartUtc = body.PeriodStartUtc.UtcDateTime,
            PeriodEndUtc = body.PeriodEndUtc.UtcDateTime, CreatedAtUtc = DateTime.UtcNow };
        db.ReportRuns.Add(run);
        Audit(actor, "run.requested", run.Id, new { definition.Kind, definition.Format,
            run.SubjectUserId, run.SubjectGroupName, GroupMemberCount = groupMembers?.Length,
            run.PeriodStartUtc, run.PeriodEndUtc });
        await db.SaveChangesAsync(ct);
        try { await ReportCommandPublisher.PublishAsync(configuration, actor.TenantId, run, definition, ct); }
        catch (Exception) { /* Scheduled recovery republishes queued runs. */ }
        return Accepted(run, $"/api/v1/runs/{run.Id}");
    }

    public async Task<ServiceResponse> Download(ReportActor actor, Guid id, CancellationToken ct)
    {
        var run = await db.ReportRuns.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (run is null || !actor.IsAdmin && run.SubjectUserId != actor.UserId)
            return Error(404, "NOT_FOUND");
        var file = await db.ReportObjects.AsNoTracking().Where(x => x.ReportRunId == id)
            .OrderByDescending(x => x.IsExternallySigned).ThenByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);
        if (file is null) return Error(404, "NOT_FOUND");
        await using var response = await storage.GetAsync(file.ObjectKey, ct);
        await using var content = new MemoryStream();
        await response.Content.CopyToAsync(content, ct);
        return new ServiceFileResponse(content.ToArray(), file.ContentType,
            $"report-{run.PeriodStartUtc:yyyy-MM-dd}.{(file.ContentType == "application/pdf" ? "pdf" : "xlsx")}");
    }

    public async Task<ServiceResponse> UploadSignedDocument(ReportActor actor, Guid id,
        IFormFile file, CancellationToken ct)
    {
        if (file.Length is < 5 or > 25 * 1024 * 1024 ||
            !string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            return Error(400, "INVALID_PDF");
        var run = await db.ReportRuns.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (run is null || run.Status != ReportRunStatus.Succeeded ||
            !await db.ReportDefinitions.AnyAsync(x => x.Id == run.ReportDefinitionId &&
                x.Kind == ReportKind.Monthly && x.Format == ReportFormat.Pdf, ct))
            return Error(404, "NOT_FOUND");
        await using var input = file.OpenReadStream();
        var bytes = await ReadBoundedAsync(input, 25 * 1024 * 1024, ct);
        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            return Error(400, "INVALID_PDF");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var version = (await db.ReportObjects.Where(x => x.ReportRunId == id && x.IsExternallySigned)
            .Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1;
        var key = $"{actor.TenantId:N}/{id:N}/signed/v{version}.pdf";
        await storage.EnsureBucketAsync(ct);
        await using var content = new MemoryStream(bytes, writable: false);
        await storage.PutAsync(key, content, "application/pdf", ct);
        var entity = new ReportObject { TenantId = actor.TenantId, ReportRunId = id,
            ObjectKey = key, Sha256 = hash, ContentType = "application/pdf", LengthBytes = bytes.LongLength,
            Version = version, IsExternallySigned = true, CreatedAtUtc = DateTime.UtcNow,
            RetainUntilUtc = await RetainUntilAsync(ct) };
        db.ReportObjects.Add(entity);
        Audit(actor, "signed-document.uploaded", id,
            new { entity.ObjectKey, entity.Version, entity.Sha256 });
        await db.SaveChangesAsync(ct);
        return Created(new { entity.Id, entity.Version, entity.Sha256 },
            $"/api/v1/runs/{id}/documents/{entity.Id}");
    }

    public async Task<ServiceResponse> Schedules(ReportActor actor, CancellationToken ct) =>
        Ok(await db.ReportSchedules.Where(x => x.IsEnabled).Join(db.ReportDefinitions.Where(x => x.IsEnabled),
            schedule => schedule.ReportDefinitionId, definition => definition.Id,
            (schedule, definition) => new { schedule.Id, schedule.ReportDefinitionId,
                schedule.TimeZoneId, schedule.LocalTime, schedule.NextFireAtUtc,
                schedule.LastFireAtUtc, schedule.IsEnabled, definition.Kind, definition.Format,
                definition.Name }).OrderBy(x => x.Name).ToListAsync(ct));

    public async Task<ServiceResponse> CreateSchedule(ReportActor actor,
        ScheduleRequest body, CancellationToken ct)
    {
        var definition = await db.ReportDefinitions.SingleOrDefaultAsync(x =>
            x.Id == body.ReportDefinitionId && x.IsEnabled, ct);
        if (definition is null || definition.Kind == ReportKind.Performance ||
            !TimeZoneInfo.TryFindSystemTimeZoneById(body.TimeZoneId, out _))
            return Error(400, "VALIDATION_FAILED");
        var next = ReportCalendar.NextFireUtc(definition.Kind, body.TimeZoneId,
            DateTimeOffset.UtcNow, body.LocalTime);
        var schedule = new ReportSchedule { TenantId = actor.TenantId,
            ReportDefinitionId = definition.Id, TimeZoneId = body.TimeZoneId,
            LocalTime = body.LocalTime, NextFireAtUtc = next.UtcDateTime };
        db.ReportSchedules.Add(schedule);
        Audit(actor, "schedule.created", null,
            new { schedule.Id, definition.Kind, schedule.NextFireAtUtc });
        await db.SaveChangesAsync(ct);
        return Created(schedule, $"/api/v1/schedules/{schedule.Id}");
    }

    public async Task<ServiceResponse> DeleteSchedule(ReportActor actor, Guid id, CancellationToken ct)
    {
        var schedule = await db.ReportSchedules.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (schedule is null) return Error(404, "NOT_FOUND");
        schedule.IsEnabled = false;
        Audit(actor, "schedule.disabled", null, new { id });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public async Task<ServiceResponse> Retention(ReportActor actor, CancellationToken ct)
    {
        var policy = await db.RetentionPolicies.SingleOrDefaultAsync(ct);
        return Ok(policy ?? new ReportRetentionPolicy { TenantId = actor.TenantId, Years = 7 });
    }

    public async Task<ServiceResponse> UpdateRetention(ReportActor actor,
        RetentionRequest body, CancellationToken ct)
    {
        if (body.Years is < 1 or > 20) return Error(400, "VALIDATION_FAILED");
        var policy = await db.RetentionPolicies.SingleOrDefaultAsync(ct);
        if (policy is null)
        {
            policy = new ReportRetentionPolicy { TenantId = actor.TenantId };
            db.Add(policy);
        }
        policy.Years = body.Years; policy.UpdatedAtUtc = DateTime.UtcNow;
        Audit(actor, "retention.updated", null, new { policy.Years });
        await db.SaveChangesAsync(ct);
        return Ok(policy);
    }

    private IQueryable<ReportRun> VisibleRuns(ReportActor actor)
    {
        var query = db.ReportRuns.AsNoTracking();
        return actor.IsAdmin ? query : query.Where(x => x.SubjectUserId == actor.UserId);
    }

    private async Task<DateTime> RetainUntilAsync(CancellationToken ct)
    {
        var years = await db.RetentionPolicies.Select(x => x.Years).SingleOrDefaultAsync(ct);
        return DateTime.UtcNow.AddYears(years == 0 ? 7 : years);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken ct)
    {
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("Upload exceeds 25 MB.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return output.ToArray();
    }

    private void Audit(ReportActor actor, string action, Guid? runId, object details) =>
        db.Audits.Add(new ReportAudit { TenantId = actor.TenantId, ActorId = actor.UserId,
            ReportRunId = runId, Action = action, DetailsJson = JsonSerializer.Serialize(details),
            OccurredAtUtc = DateTime.UtcNow });

    private static bool ValidDefinition(DefinitionRequest body) =>
        Enum.IsDefined(body.Kind) && Enum.IsDefined(body.Format) &&
        !string.IsNullOrWhiteSpace(body.Name) && body.Name.Length <= 160;

    private static ServiceResponse Ok<T>(T result) => new ServiceOkResponse<T>(result);
    private static ServiceResponse Created<T>(T result, string location) =>
        new ServiceOkResponse<T>(result, 201, location);
    private static ServiceResponse Accepted<T>(T result, string location) =>
        new ServiceOkResponse<T>(result, 202, location);
    private static ServiceResponse NoContent() => new ServiceOkResponse<object?>(null, 204);
    private static ServiceResponse Error(int status, string code) =>
        new ServiceErrorResponse(status, code, "The request could not be completed.");
}
