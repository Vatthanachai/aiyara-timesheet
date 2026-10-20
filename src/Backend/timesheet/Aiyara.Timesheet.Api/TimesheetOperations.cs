using System.Data;
using System.Text.Json;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Contracts.Messaging.V1;
using Aiyara.Timesheet.Databases;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Timesheet.Api;

public sealed class TimesheetOperations(TimesheetDbContext db)
{
    public async Task<ServiceResponse> Context(Actor actor, CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(actor.TimeZoneId));
        var locked = await db.MonthLocks.AnyAsync(x => x.Year == local.Year && x.Month == local.Month, ct);
        return Ok(new { currentMonth = $"{local.Year:D4}-{local.Month:D2}",
            timeZoneId = actor.TimeZoneId, locked, isAdmin = actor.IsAdmin });
    }

    public async Task<ServiceResponse> Catalog(Actor actor, CancellationToken ct) => Ok(new
    {
        projects = await db.Projects.Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToListAsync(ct),
        categories = await db.Categories.Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToListAsync(ct),
        holidays = await db.Holidays.Where(x => !x.IsDeleted).OrderBy(x => x.Date).ToListAsync(ct)
    });

    public async Task<ServiceResponse> SaveCatalog(Actor actor, NameRequest body,
        string kind, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 200)
            return Error(400, "VALIDATION_FAILED");
        TenantRecord item = kind == "project"
            ? new Project { TenantId = actor.TenantId, Name = body.Name.Trim() }
            : new Category { TenantId = actor.TenantId, Name = body.Name.Trim() };
        db.Add(item);
        Audit(actor, kind, item.Id, "created", null, item);
        await db.SaveChangesAsync(ct);
        return Created(item, $"/api/v1/{kind}s/{item.Id}");
    }

    public async Task<ServiceResponse> UpdateCatalog(Actor actor, Guid id, NameRequest body,
        string kind, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 200)
            return Error(400, "VALIDATION_FAILED");
        var item = await FindCatalog(id, kind, ct);
        if (item is null) return Error(404, "NOT_FOUND");
        var before = JsonSerializer.Serialize(item, item.GetType());
        if (item is Project project) project.Name = body.Name.Trim();
        if (item is Category category) category.Name = body.Name.Trim();
        Audit(actor, kind, id, "updated", before, item);
        await db.SaveChangesAsync(ct);
        return Ok(item);
    }

    public async Task<ServiceResponse> DeleteCatalog(Actor actor, Guid id, string kind,
        CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        var item = await FindCatalog(id, kind, ct);
        if (item is null) return Error(404, "NOT_FOUND");
        var before = JsonSerializer.Serialize(item, item.GetType());
        if (item is Project project) project.IsDeleted = true;
        if (item is Category category) category.IsDeleted = true;
        Audit(actor, kind, id, "deleted", before, item);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public async Task<ServiceResponse> CreateHoliday(Actor actor, HolidayRequest body, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 200)
            return Error(400, "VALIDATION_FAILED");
        var holiday = new Holiday { TenantId = actor.TenantId, Date = body.Date, Name = body.Name.Trim() };
        db.Holidays.Add(holiday);
        Audit(actor, "holiday", holiday.Id, "created", null, holiday);
        await db.SaveChangesAsync(ct);
        return Created(holiday, $"/api/v1/holidays/{holiday.Id}");
    }

    public async Task<ServiceResponse> UpdateHoliday(Actor actor, Guid id, HolidayRequest body,
        CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 200)
            return Error(400, "VALIDATION_FAILED");
        var holiday = await db.Holidays.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (holiday is null) return Error(404, "NOT_FOUND");
        var before = JsonSerializer.Serialize(holiday);
        holiday.Date = body.Date; holiday.Name = body.Name.Trim();
        Audit(actor, "holiday", id, "updated", before, holiday);
        await db.SaveChangesAsync(ct);
        return Ok(holiday);
    }

    public async Task<ServiceResponse> DeleteHoliday(Actor actor, Guid id, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        var holiday = await db.Holidays.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (holiday is null) return Error(404, "NOT_FOUND");
        var before = JsonSerializer.Serialize(holiday);
        holiday.IsDeleted = true;
        Audit(actor, "holiday", id, "deleted", before, holiday);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public async Task<ServiceResponse> Tasks(Actor actor, CancellationToken ct) =>
        Ok(await db.PersonalTasks.Where(x => x.OwnerId == actor.UserId && !x.IsDeleted)
            .OrderBy(x => x.Name).ToListAsync(ct));

    public async Task<ServiceResponse> CreateTask(Actor actor, TaskRequest body, CancellationToken ct)
    {
        if (!await ValidTask(body, ct)) return Error(400, "VALIDATION_FAILED");
        var task = new PersonalTask { TenantId = actor.TenantId, OwnerId = actor.UserId,
            Name = body.Name.Trim(), Status = body.Status, ProjectId = body.ProjectId,
            CategoryId = body.CategoryId };
        db.PersonalTasks.Add(task);
        Audit(actor, "task", task.Id, "created", null, task);
        await db.SaveChangesAsync(ct);
        return Created(task, $"/api/v1/tasks/{task.Id}");
    }

    public async Task<ServiceResponse> UpdateTask(Actor actor, Guid id, TaskRequest body,
        CancellationToken ct)
    {
        var task = await db.PersonalTasks.SingleOrDefaultAsync(x => x.Id == id &&
            x.OwnerId == actor.UserId && !x.IsDeleted, ct);
        if (task is null) return Error(404, "NOT_FOUND");
        if (!await ValidTask(body, ct)) return Error(400, "VALIDATION_FAILED");
        var before = JsonSerializer.Serialize(task);
        task.Name = body.Name.Trim(); task.Status = body.Status;
        task.ProjectId = body.ProjectId; task.CategoryId = body.CategoryId;
        Audit(actor, "task", id, "updated", before, task);
        await db.SaveChangesAsync(ct);
        return Ok(task);
    }

    public async Task<ServiceResponse> DeleteTask(Actor actor, Guid id, CancellationToken ct)
    {
        var task = await db.PersonalTasks.SingleOrDefaultAsync(x => x.Id == id &&
            x.OwnerId == actor.UserId && !x.IsDeleted, ct);
        if (task is null) return Error(404, "NOT_FOUND");
        var before = JsonSerializer.Serialize(task);
        task.IsDeleted = true;
        Audit(actor, "task", id, "deleted", before, task);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public async Task<ServiceResponse> Entries(Actor actor, int year, int month, CancellationToken ct)
    {
        if (!ValidMonth(year, month)) return Error(400, "INVALID_DATE");
        return Ok(await db.TimeEntries.AsNoTracking().Where(x => x.OwnerId == actor.UserId &&
            !x.IsDeleted && x.Date.Year == year && x.Date.Month == month)
            .OrderBy(x => x.Date).ThenBy(x => x.StartTime).ToListAsync(ct));
    }

    public async Task<ServiceResponse> SaveEntry(Actor actor, Guid? id, EntryRequest body,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var entry = id is null ? null : await db.TimeEntries.SingleOrDefaultAsync(x =>
            x.Id == id && x.OwnerId == actor.UserId && !x.IsDeleted, ct);
        if (id is not null && entry is null) return Error(404, "NOT_FOUND");
        if (!await CanMutate(actor, body.Date, ct) ||
            entry is not null && !await CanMutate(actor, entry.Date, ct))
            return Error(409, "MONTH_CLOSED");
        if (string.IsNullOrWhiteSpace(body.TaskName) || body.TaskName.Length > 200 ||
            body.Detail?.Length > 4000 || body.Notes?.Length > 4000 ||
            !await ValidReferences(body.ProjectId, body.CategoryId, ct) ||
            body.PersonalTaskId is not null && !await db.PersonalTasks.AnyAsync(x =>
                x.Id == body.PersonalTaskId && x.OwnerId == actor.UserId && !x.IsDeleted, ct))
            return Error(400, "VALIDATION_FAILED");
        int duration;
        try { duration = TimesheetRules.DurationMinutes(body.StartTime, body.EndTime); }
        catch (ArgumentException) { return Error(400, "INVALID_TIME_RANGE"); }
        var before = entry is null ? null : JsonSerializer.Serialize(entry);
        entry ??= new TimeEntry { TenantId = actor.TenantId, OwnerId = actor.UserId,
            TaskName = body.TaskName.Trim() };
        if (id is null) db.TimeEntries.Add(entry);
        entry.Date = body.Date; entry.StartTime = body.StartTime; entry.EndTime = body.EndTime;
        entry.DurationMinutes = duration; entry.TaskName = body.TaskName.Trim();
        entry.Detail = body.Detail; entry.Notes = body.Notes;
        entry.ProjectId = body.ProjectId; entry.CategoryId = body.CategoryId;
        entry.PersonalTaskId = body.PersonalTaskId; entry.UpdatedAtUtc = DateTime.UtcNow;
        Audit(actor, "entry", entry.Id, id is null ? "created" : "updated", before, entry, body.Reason);
        Changed(actor, body.Date);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return id is null ? Created(entry, $"/api/v1/entries/{entry.Id}") : Ok(entry);
    }

    public async Task<ServiceResponse> DeleteEntry(Actor actor, Guid id, string? reason,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var entry = await db.TimeEntries.SingleOrDefaultAsync(x => x.Id == id &&
            x.OwnerId == actor.UserId && !x.IsDeleted, ct);
        if (entry is null) return Error(404, "NOT_FOUND");
        if (!await CanMutate(actor, entry.Date, ct)) return Error(409, "MONTH_CLOSED");
        var before = JsonSerializer.Serialize(entry);
        entry.IsDeleted = true; entry.UpdatedAtUtc = DateTime.UtcNow;
        Audit(actor, "entry", id, "deleted", before, entry, reason);
        Changed(actor, entry.Date);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    public async Task<ServiceResponse> Leaves(Actor actor, int year, int month, CancellationToken ct)
    {
        if (!ValidMonth(year, month)) return Error(400, "INVALID_DATE");
        return Ok(await db.LeaveEntries.AsNoTracking().Where(x => x.OwnerId == actor.UserId &&
            !x.IsDeleted && x.Date.Year == year && x.Date.Month == month)
            .OrderBy(x => x.Date).ToListAsync(ct));
    }

    public async Task<ServiceResponse> SaveLeave(Actor actor, Guid? id, LeaveRequest body,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var leave = id is null ? null : await db.LeaveEntries.SingleOrDefaultAsync(x =>
            x.Id == id && x.OwnerId == actor.UserId && !x.IsDeleted, ct);
        if (id is not null && leave is null) return Error(404, "NOT_FOUND");
        if (!await CanMutate(actor, body.Date, ct) ||
            leave is not null && !await CanMutate(actor, leave.Date, ct))
            return Error(409, "MONTH_CLOSED");
        if (string.IsNullOrWhiteSpace(body.Kind) || body.Kind.Length > 60 || body.Notes?.Length > 4000)
            return Error(400, "VALIDATION_FAILED");
        var before = leave is null ? null : JsonSerializer.Serialize(leave);
        leave ??= new LeaveEntry { TenantId = actor.TenantId, OwnerId = actor.UserId,
            Kind = body.Kind.Trim() };
        if (id is null) db.LeaveEntries.Add(leave);
        leave.Date = body.Date; leave.Kind = body.Kind.Trim(); leave.Notes = body.Notes;
        Audit(actor, "leave", leave.Id, id is null ? "created" : "updated", before, leave);
        Changed(actor, body.Date);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return id is null ? Created(leave, $"/api/v1/leave/{leave.Id}") : Ok(leave);
    }

    public async Task<ServiceResponse> DeleteLeave(Actor actor, Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var leave = await db.LeaveEntries.SingleOrDefaultAsync(x => x.Id == id &&
            x.OwnerId == actor.UserId && !x.IsDeleted, ct);
        if (leave is null) return Error(404, "NOT_FOUND");
        if (!await CanMutate(actor, leave.Date, ct)) return Error(409, "MONTH_CLOSED");
        var before = JsonSerializer.Serialize(leave);
        leave.IsDeleted = true;
        Audit(actor, "leave", id, "deleted", before, leave);
        Changed(actor, leave.Date);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    public async Task<ServiceResponse> LockMonth(Actor actor, int year, int month, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        if (!ValidMonth(year, month)) return Error(400, "INVALID_DATE");
        if (await db.MonthLocks.AnyAsync(x => x.Year == year && x.Month == month, ct))
            return Error(409, "MONTH_CLOSED");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var entries = await db.TimeEntries.AsNoTracking().Where(x => !x.IsDeleted &&
            x.Date.Year == year && x.Date.Month == month).ToListAsync(ct);
        var leaves = await db.LeaveEntries.AsNoTracking().Where(x => !x.IsDeleted &&
            x.Date.Year == year && x.Date.Month == month).ToListAsync(ct);
        var holidays = await db.Holidays.AsNoTracking().Where(x => !x.IsDeleted &&
            x.Date.Year == year && x.Date.Month == month).ToListAsync(ct);
        var projects = await db.Projects.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var entrySnapshots = entries.Select(entry => new
        {
            entry.Id, entry.TenantId, entry.OwnerId, entry.Date, entry.StartTime,
            entry.EndTime, entry.DurationMinutes, entry.TaskName, entry.Detail,
            entry.Notes, entry.ProjectId,
            ProjectName = entry.ProjectId is { } projectId && projects.TryGetValue(projectId, out var projectName)
                ? projectName : "",
            entry.CategoryId,
            CategoryName = entry.CategoryId is { } categoryId && categories.TryGetValue(categoryId, out var categoryName)
                ? categoryName : "",
            entry.PersonalTaskId, entry.IsDeleted, entry.UpdatedAtUtc
        }).ToArray();
        db.MonthSnapshots.Add(new MonthSnapshot { TenantId = actor.TenantId,
            OwnerId = Guid.Empty, Year = year, Month = month, CreatedAtUtc = DateTime.UtcNow,
            PayloadJson = JsonSerializer.Serialize(new { entries = entrySnapshots, leaves, holidays }) });
        foreach (var ownerId in entries.Select(x => x.OwnerId).Concat(leaves.Select(x => x.OwnerId)).Distinct())
            db.MonthSnapshots.Add(new MonthSnapshot { TenantId = actor.TenantId, OwnerId = ownerId,
                Year = year, Month = month, CreatedAtUtc = DateTime.UtcNow,
                PayloadJson = JsonSerializer.Serialize(new { entries = entrySnapshots.Where(x => x.OwnerId == ownerId),
                    leaves = leaves.Where(x => x.OwnerId == ownerId), holidays }) });
        var monthLock = new MonthLock { TenantId = actor.TenantId, Year = year, Month = month,
            LockedBy = actor.UserId, LockedAtUtc = DateTime.UtcNow };
        db.MonthLocks.Add(monthLock);
        Audit(actor, "month", monthLock.Id, "locked", null, monthLock);
        db.OutboxEvents.Add(new TimesheetOutboxEvent { TenantId = actor.TenantId,
            EventType = MessageTypes.TimesheetMonthLocked, SubjectId = Guid.Empty, Year = year,
            Month = month, TimeZoneId = actor.TimeZoneId, OccurredAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(monthLock);
    }

    public async Task<ServiceResponse> Snapshot(Actor actor, int year, int month,
        Guid? ownerId, CancellationToken ct)
    {
        var subject = actor.IsAdmin && ownerId is not null ? ownerId.Value : actor.UserId;
        var snapshot = await db.MonthSnapshots.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OwnerId == subject && x.Year == year && x.Month == month, ct);
        return snapshot is null ? Error(404, "NOT_FOUND") :
            Ok(JsonSerializer.Deserialize<JsonElement>(snapshot.PayloadJson));
    }

    /// <summary>Tenant Admin overview of one month: every member with entries or leave in it,
    /// read live, so the current open month is included.</summary>
    public async Task<ServiceResponse> MemberSummary(Actor actor, int year, int month, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        if (!ValidMonth(year, month)) return Error(400, "INVALID_DATE");
        var entries = await db.TimeEntries.AsNoTracking().Where(x => !x.IsDeleted &&
                x.Date.Year == year && x.Date.Month == month)
            .GroupBy(x => x.OwnerId)
            .Select(x => new { OwnerId = x.Key, EntryCount = x.Count(), TotalMinutes = x.Sum(e => e.DurationMinutes) })
            .ToListAsync(ct);
        var leaves = await db.LeaveEntries.AsNoTracking().Where(x => !x.IsDeleted &&
                x.Date.Year == year && x.Date.Month == month)
            .GroupBy(x => x.OwnerId)
            .Select(x => new { OwnerId = x.Key, LeaveDays = x.Count() })
            .ToListAsync(ct);
        var members = entries.Select(x => x.OwnerId).Union(leaves.Select(x => x.OwnerId))
            .Select(ownerId =>
            {
                var worked = entries.SingleOrDefault(x => x.OwnerId == ownerId);
                return new MemberMonthSummary(ownerId, worked?.EntryCount ?? 0, worked?.TotalMinutes ?? 0,
                    leaves.SingleOrDefault(x => x.OwnerId == ownerId)?.LeaveDays ?? 0);
            })
            .OrderByDescending(x => x.TotalMinutes).ToArray();
        return Ok(new { year, month, locked = await IsLocked(year, month, ct), members });
    }

    /// <summary>Tenant Admin view of one member's month: entries with project and category names,
    /// leave, and the month's holidays. Read-only; another tenant's member simply has no rows.</summary>
    public async Task<ServiceResponse> MemberTimesheet(Actor actor, Guid ownerId, int year, int month,
        CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        if (!ValidMonth(year, month)) return Error(400, "INVALID_DATE");
        var projects = await db.Projects.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var entries = (await db.TimeEntries.AsNoTracking().Where(x => x.OwnerId == ownerId &&
                !x.IsDeleted && x.Date.Year == year && x.Date.Month == month)
            .OrderBy(x => x.Date).ThenBy(x => x.StartTime).ToListAsync(ct))
            .Select(x => new
            {
                x.Id, x.Date, x.StartTime, x.EndTime, x.DurationMinutes, x.TaskName, x.Detail, x.Notes,
                x.ProjectId,
                ProjectName = x.ProjectId is { } projectId && projects.TryGetValue(projectId, out var project) ? project : null,
                x.CategoryId,
                CategoryName = x.CategoryId is { } categoryId && categories.TryGetValue(categoryId, out var category) ? category : null,
                x.UpdatedAtUtc
            }).ToArray();
        var leaves = await db.LeaveEntries.AsNoTracking().Where(x => x.OwnerId == ownerId &&
                !x.IsDeleted && x.Date.Year == year && x.Date.Month == month)
            .OrderBy(x => x.Date).Select(x => new { x.Id, x.Date, x.Kind, x.Notes }).ToListAsync(ct);
        var holidays = await db.Holidays.AsNoTracking().Where(x => !x.IsDeleted &&
                x.Date.Year == year && x.Date.Month == month)
            .OrderBy(x => x.Date).Select(x => new { x.Date, x.Name }).ToListAsync(ct);
        return Ok(new { ownerId, year, month, locked = await IsLocked(year, month, ct), entries, leaves, holidays });
    }

    private Task<bool> IsLocked(int year, int month, CancellationToken ct) =>
        db.MonthLocks.AnyAsync(x => x.Year == year && x.Month == month, ct);

    private async Task<TenantRecord?> FindCatalog(Guid id, string kind, CancellationToken ct) =>
        kind == "project"
            ? await db.Projects.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            : await db.Categories.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

    private async Task<bool> CanMutate(Actor actor, DateOnly date, CancellationToken ct) =>
        TimesheetRules.IsCurrentMonth(date, actor.TimeZoneId, DateTimeOffset.UtcNow) &&
        !await db.MonthLocks.AnyAsync(x => x.Year == date.Year && x.Month == date.Month, ct);

    private async Task<bool> ValidReferences(Guid? projectId, Guid? categoryId, CancellationToken ct) =>
        (projectId is null || await db.Projects.AnyAsync(x => x.Id == projectId && !x.IsDeleted, ct)) &&
        (categoryId is null || await db.Categories.AnyAsync(x => x.Id == categoryId && !x.IsDeleted, ct));

    private async Task<bool> ValidTask(TaskRequest body, CancellationToken ct) =>
        !string.IsNullOrWhiteSpace(body.Name) && body.Name.Length <= 200 &&
        body.Status is "todo" or "doing" or "done" &&
        await ValidReferences(body.ProjectId, body.CategoryId, ct);

    private static bool ValidMonth(int year, int month) => year is >= 2000 and <= 2100 &&
        month is >= 1 and <= 12;

    private void Audit(Actor actor, string entityType, Guid entityId, string action,
        string? before, object? after, string? reason = null) =>
        db.Audits.Add(new TimesheetAudit { TenantId = actor.TenantId, ActorId = actor.UserId,
            SubjectId = actor.UserId, EntityType = entityType, EntityId = entityId, Action = action,
            BeforeJson = before, AfterJson = after is null ? null : JsonSerializer.Serialize(after, after.GetType()),
            Reason = reason, OccurredAtUtc = DateTime.UtcNow });

    private void Changed(Actor actor, DateOnly date) =>
        db.OutboxEvents.Add(new TimesheetOutboxEvent { TenantId = actor.TenantId,
            EventType = MessageTypes.TimesheetMonthChanged, SubjectId = actor.UserId,
            Year = date.Year, Month = date.Month, OccurredAtUtc = DateTime.UtcNow });

    private static ServiceResponse Ok<T>(T result) => new ServiceOkResponse<T>(result);
    private static ServiceResponse Created<T>(T result, string location) =>
        new ServiceOkResponse<T>(result, 201, location);
    private static ServiceResponse NoContent() => new ServiceOkResponse<object?>(null, 204);
    private static ServiceResponse Error(int status, string code) =>
        new ServiceErrorResponse(status, code, "The request could not be completed.");
}
