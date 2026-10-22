using System.Data;
using System.Globalization;
using System.Text.Json;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Aiyara.Timesheet.Contracts.Messaging.V1;
using Aiyara.Timesheet.Databases;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Timesheet.Api;

/// <param name="notifier">Emails a member whose month was returned; none in tests that do not need it.</param>
public sealed class TimesheetOperations(TimesheetDbContext db, IMonthReturnNotifier? notifier = null)
{
    public async Task<ServiceResponse> Context(Actor actor, CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(actor.TimeZoneId));
        var locked = await db.MonthLocks.AnyAsync(x => x.Year == local.Year && x.Month == local.Month, ct);
        return Ok(new { currentMonth = $"{local.Year:D4}-{local.Month:D2}",
            timeZoneId = actor.TimeZoneId, locked, isAdmin = actor.IsAdmin });
    }

    public async Task<ServiceResponse> Catalog(Actor actor, CancellationToken ct)
    {
        var (workingDays, workHours) = await ScheduleAsync(ct);
        return Ok(new
        {
            projects = await db.Projects.Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToListAsync(ct),
            categories = await db.Categories.Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToListAsync(ct),
            holidays = await db.Holidays.Where(x => !x.IsDeleted).OrderBy(x => x.Date).ToListAsync(ct),
            leaveTypes = await db.LeaveTypes.Where(x => !x.IsDeleted).OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name, x.AnnualDays }).ToListAsync(ct),
            workingDays, workHours
        });
    }

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

    /// <summary>Tenant Admin only: adds a leave type (<paramref name="id"/> null) or renames it and changes its days.
    /// Leave already taken keeps the name it was saved with.</summary>
    public async Task<ServiceResponse> SaveLeaveType(Actor actor, Guid? id, LeaveTypeRequest body, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        var name = body.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 60 ||
            body.AnnualDays is { } days && (days is < 0 or > 366 || days * 2 != decimal.Truncate(days * 2)))
            return Error(400, "VALIDATION_FAILED");
        var type = id is null ? null : await db.LeaveTypes.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (id is not null && type is null) return Error(404, "NOT_FOUND");
        if (await db.LeaveTypes.AnyAsync(x => !x.IsDeleted && x.Id != id && x.Name.ToLower() == name.ToLower(), ct))
            return Error(409, "LEAVE_TYPE_EXISTS");
        var before = type is null ? null : JsonSerializer.Serialize(type);
        if (type is null)
        {
            type = new LeaveType { TenantId = actor.TenantId, Name = name };
            db.LeaveTypes.Add(type);
        }
        type.Name = name; type.AnnualDays = body.AnnualDays;
        Audit(actor, "leave_type", type.Id, before is null ? "created" : "updated", before, type);
        await db.SaveChangesAsync(ct);
        return id is null ? Created(type, $"/api/v1/leave-types/{type.Id}") : Ok(type);
    }

    /// <summary>Tenant Admin only: retires a leave type; leave already taken with it is kept.</summary>
    public async Task<ServiceResponse> DeleteLeaveType(Actor actor, Guid id, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        var type = await db.LeaveTypes.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (type is null) return Error(404, "NOT_FOUND");
        var before = JsonSerializer.Serialize(type);
        type.IsDeleted = true;
        Audit(actor, "leave_type", id, "deleted", before, type);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>The signed-in member's leave balance for a calendar year, one row per leave type.</summary>
    public Task<ServiceResponse> MyLeaveBalance(Actor actor, int year, CancellationToken ct) =>
        LeaveBalanceAsync(actor.UserId, year, ct);

    /// <summary>Tenant Admin only: a member's leave balance for a calendar year.</summary>
    public Task<ServiceResponse> MemberLeaveBalance(Actor actor, Guid ownerId, int year, CancellationToken ct) =>
        actor.IsAdmin ? LeaveBalanceAsync(ownerId, year, ct) : Task.FromResult(Error(403, "FORBIDDEN"));

    private async Task<ServiceResponse> LeaveBalanceAsync(Guid ownerId, int year, CancellationToken ct)
    {
        if (!ValidMonth(year, 1)) return Error(400, "INVALID_DATE");
        var types = await db.LeaveTypes.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToListAsync(ct);
        var taken = await db.LeaveEntries.AsNoTracking().Where(x => x.OwnerId == ownerId && !x.IsDeleted &&
            x.Date.Year == year && x.LeaveTypeId != null).Select(x => new { x.LeaveTypeId, x.Minutes }).ToListAsync(ct);
        var standard = await StandardMinutesAsync(ct);
        return Ok(new
        {
            year,
            balances = types.Select(type =>
            {
                var used = taken.Where(x => x.LeaveTypeId == type.Id).Sum(x => LeaveDays(x.Minutes, standard));
                return new LeaveBalance(type.Id, type.Name, type.AnnualDays, used,
                    type.AnnualDays is { } days ? days - used : null);
            })
        });
    }

    public async Task<ServiceResponse> SaveWorkWeek(Actor actor, WorkWeekRequest body, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        var days = body.WorkingDays ?? [];
        if (days.Length is 0 or > 7 || days.Any(day => day is < 0 or > 6) || days.Distinct().Count() != days.Length)
            return Error(400, "VALIDATION_FAILED");
        // Both times or neither; whole minutes; the day ends after it starts (no overnight shifts).
        // A break comes only with the hours, both of its times or neither, inside the working day.
        if (body.StartTime.HasValue != body.EndTime.HasValue || body.BreakStart.HasValue != body.BreakEnd.HasValue ||
            body.BreakStart.HasValue && !body.StartTime.HasValue ||
            !new[] { body.StartTime, body.EndTime, body.BreakStart, body.BreakEnd }.All(WholeMinutes) ||
            body.StartTime is { } start && body.EndTime is { } end && start >= end ||
            body.BreakStart is { } breakStart && body.BreakEnd is { } breakEnd &&
            (breakStart >= breakEnd || breakStart < body.StartTime || breakEnd > body.EndTime))
            return Error(400, "VALIDATION_FAILED");
        var workWeek = await db.WorkWeeks.SingleOrDefaultAsync(ct);
        var before = workWeek is null ? null : JsonSerializer.Serialize(workWeek);
        if (workWeek is null)
        {
            workWeek = new WorkWeek { TenantId = actor.TenantId };
            db.WorkWeeks.Add(workWeek);
        }
        workWeek.WorkingDaysMask = days.Aggregate(0, (mask, day) => mask | 1 << day);
        if (body.StartTime is { } startTime && body.EndTime is { } endTime)
        {
            workWeek.StartTime = startTime;
            workWeek.EndTime = endTime;
            workWeek.BreakStart = body.BreakStart;
            workWeek.BreakEnd = body.BreakEnd;
        }
        Audit(actor, "work_week", workWeek.Id, before is null ? "created" : "updated", before, workWeek);
        await db.SaveChangesAsync(ct);
        return Ok(new { workingDays = WorkWeek.Days(workWeek.WorkingDaysMask), workHours = Hours(workWeek) });
    }

    /// <summary>Tenant Admin only: the organization's reminder settings; off without a row.</summary>
    public async Task<ServiceResponse> Reminders(Actor actor, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        return Ok(Reminder(await db.ReminderSettings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new ReminderSettings()));
    }

    public async Task<ServiceResponse> SaveReminders(Actor actor, ReminderSettingsRequest body, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        if (!WholeMinutes(body.SendAt)) return Error(400, "VALIDATION_FAILED");
        var settings = await db.ReminderSettings.SingleOrDefaultAsync(ct);
        var before = settings is null ? null : JsonSerializer.Serialize(settings);
        if (settings is null)
        {
            settings = new ReminderSettings { TenantId = actor.TenantId };
            db.ReminderSettings.Add(settings);
        }
        settings.DailyEnabled = body.DailyEnabled; settings.MonthEndEnabled = body.MonthEndEnabled;
        settings.SendAt = body.SendAt;
        Audit(actor, "reminder_settings", settings.Id, before is null ? "created" : "updated", before, settings);
        await db.SaveChangesAsync(ct);
        return Ok(Reminder(settings));
    }

    private static object Reminder(ReminderSettings settings) => new
    {
        dailyEnabled = settings.DailyEnabled, monthEndEnabled = settings.MonthEndEnabled,
        sendAt = settings.SendAt.ToString("HH:mm", CultureInfo.InvariantCulture)
    };

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
        var week = await db.WorkWeeks.AsNoTracking().SingleOrDefaultAsync(ct);
        try
        {
            duration = TimesheetRules.WorkedMinutes(body.StartTime, body.EndTime, week?.BreakStart, week?.BreakEnd,
                body.IncludesBreak);
        }
        catch (ArgumentException) { return Error(400, "INVALID_TIME_RANGE"); }
        // Wholly inside the break and not worked through it: the entry would count for nothing.
        if (duration == 0) return Error(400, "ENTRY_IN_BREAK");
        var before = entry is null ? null : JsonSerializer.Serialize(entry);
        entry ??= new TimeEntry { TenantId = actor.TenantId, OwnerId = actor.UserId,
            TaskName = body.TaskName.Trim() };
        if (id is null) db.TimeEntries.Add(entry);
        entry.Date = body.Date; entry.StartTime = body.StartTime; entry.EndTime = body.EndTime;
        entry.DurationMinutes = duration; entry.TaskName = body.TaskName.Trim();
        entry.Detail = body.Detail; entry.Notes = body.Notes;
        entry.ProjectId = body.ProjectId; entry.CategoryId = body.CategoryId;
        entry.PersonalTaskId = body.PersonalTaskId; entry.IncludesBreak = body.IncludesBreak;
        entry.UpdatedAtUtc = DateTime.UtcNow;
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
        // With leave types, the leave names one of them; without, its kind is free text.
        var types = await db.LeaveTypes.AsNoTracking().Where(x => !x.IsDeleted).ToListAsync(ct);
        var type = types.SingleOrDefault(x => x.Id == body.LeaveTypeId);
        var kind = types.Count > 0 ? type?.Name : body.Kind?.Trim();
        if (string.IsNullOrEmpty(kind) || kind.Length > 60 || body.Notes?.Length > 4000 ||
            types.Count == 0 && body.LeaveTypeId is not null ||
            body.Minutes is { } minutes && (minutes < 1 || minutes > await StandardMinutesAsync(ct)))
            return Error(400, "VALIDATION_FAILED");
        var before = leave is null ? null : JsonSerializer.Serialize(leave);
        leave ??= new LeaveEntry { TenantId = actor.TenantId, OwnerId = actor.UserId, Kind = kind };
        if (id is null) db.LeaveEntries.Add(leave);
        leave.Date = body.Date; leave.Kind = kind; leave.Notes = body.Notes;
        leave.LeaveTypeId = type?.Id; leave.Minutes = body.Minutes;
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
        // Members whose month was approved keep the snapshot taken then.
        var taken = await db.MonthSnapshots.AsNoTracking().Where(x => x.Year == year && x.Month == month)
            .Select(x => x.OwnerId).ToListAsync(ct);
        foreach (var (ownerId, payload) in await SnapshotPayloadsAsync(year, month, null, ct))
            if (!taken.Contains(ownerId))
                db.MonthSnapshots.Add(new MonthSnapshot { TenantId = actor.TenantId, OwnerId = ownerId,
                    Year = year, Month = month, CreatedAtUtc = DateTime.UtcNow, PayloadJson = payload });
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

    /// <summary>The signed-in member's month: its approval status, why it was returned, whether the
    /// organization has locked it, and whether the member may still change it.</summary>
    public async Task<ServiceResponse> MyMonth(Actor actor, int year, int month, CancellationToken ct)
    {
        if (!ValidMonth(year, month)) return Error(400, "INVALID_DATE");
        var locked = await IsLocked(year, month, ct);
        var approval = await FindApproval(actor.UserId, year, month, ct);
        return Ok(new { year, month, locked, status = StatusOf(approval), returnReason = approval?.ReturnReason,
            editable = Editable(actor, year, month, locked, approval) });
    }

    /// <summary>A member submits their own open or returned month, the current one or a past one, for approval.</summary>
    public async Task<ServiceResponse> SubmitMonth(Actor actor, int year, int month, CancellationToken ct)
    {
        if (!ValidMonth(year, month) ||
            TimesheetRules.IsFutureMonth(year, month, actor.TimeZoneId, DateTimeOffset.UtcNow))
            return Error(400, "INVALID_DATE");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await IsLocked(year, month, ct)) return Error(409, "MONTH_CLOSED");
        var approval = await FindApproval(actor.UserId, year, month, ct);
        if (approval is not null && approval.Status != MonthApproval.Returned) return Error(409, "INVALID_TRANSITION");
        var before = approval is null ? null : JsonSerializer.Serialize(approval);
        if (approval is null)
        {
            approval = new MonthApproval { TenantId = actor.TenantId, OwnerId = actor.UserId, Year = year,
                Month = month, Status = MonthApproval.Submitted };
            db.MonthApprovals.Add(approval);
        }
        approval.Status = MonthApproval.Submitted; approval.SubmittedAtUtc = DateTime.UtcNow;
        approval.DecidedBy = null; approval.DecidedAtUtc = null; approval.ReturnReason = null;
        Audit(actor, "month_approval", approval.Id, "submitted", before, approval);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(approval);
    }

    /// <summary>Tenant Admin only: approves a submitted month and takes the member's snapshot of it.</summary>
    public async Task<ServiceResponse> ApproveMonth(Actor actor, Guid ownerId, int year, int month, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        if (!ValidMonth(year, month)) return Error(400, "INVALID_DATE");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await IsLocked(year, month, ct)) return Error(409, "MONTH_CLOSED");
        var approval = await FindApproval(ownerId, year, month, ct);
        if (approval?.Status != MonthApproval.Submitted) return Error(409, "INVALID_TRANSITION");
        var before = JsonSerializer.Serialize(approval);
        var payloads = await SnapshotPayloadsAsync(year, month, ownerId, ct);
        db.MonthSnapshots.Add(new MonthSnapshot { TenantId = actor.TenantId, OwnerId = ownerId, Year = year,
            Month = month, CreatedAtUtc = DateTime.UtcNow, PayloadJson = payloads[ownerId] });
        approval.Status = MonthApproval.Approved; approval.DecidedBy = actor.UserId; approval.DecidedAtUtc = DateTime.UtcNow;
        Audit(actor, "month_approval", approval.Id, "approved", before, approval, subject: ownerId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(approval);
    }

    /// <summary>Tenant Admin only: returns a submitted month to its member with a reason, opening it
    /// for edits again, and emails the member.</summary>
    public async Task<ServiceResponse> ReturnMonth(Actor actor, Guid ownerId, int year, int month,
        ReturnRequest body, CancellationToken ct)
    {
        if (!actor.IsAdmin) return Error(403, "FORBIDDEN");
        if (!ValidMonth(year, month)) return Error(400, "INVALID_DATE");
        var reason = body.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 1000) return Error(400, "VALIDATION_FAILED");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await IsLocked(year, month, ct)) return Error(409, "MONTH_CLOSED");
        var approval = await FindApproval(ownerId, year, month, ct);
        if (approval?.Status != MonthApproval.Submitted) return Error(409, "INVALID_TRANSITION");
        var before = JsonSerializer.Serialize(approval);
        approval.Status = MonthApproval.Returned; approval.DecidedBy = actor.UserId;
        approval.DecidedAtUtc = DateTime.UtcNow; approval.ReturnReason = reason;
        Audit(actor, "month_approval", approval.Id, "returned", before, approval, reason, ownerId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        // Best effort once the return is saved; the reason is also on the member's Timesheet page.
        if (notifier is not null) await notifier.MonthReturnedAsync(actor.TenantId, ownerId, year, month, reason, ct);
        return Ok(approval);
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
        var standard = await StandardMinutesAsync(ct);
        var leaves = (await db.LeaveEntries.AsNoTracking().Where(x => !x.IsDeleted &&
                x.Date.Year == year && x.Date.Month == month)
            .Select(x => new { x.OwnerId, x.Minutes }).ToListAsync(ct))
            .GroupBy(x => x.OwnerId)
            .Select(x => new { OwnerId = x.Key, LeaveDays = x.Sum(leave => LeaveDays(leave.Minutes, standard)) })
            .ToList();
        var statuses = await db.MonthApprovals.AsNoTracking().Where(x => x.Year == year && x.Month == month)
            .ToDictionaryAsync(x => x.OwnerId, x => x.Status, ct);
        var members = entries.Select(x => x.OwnerId).Union(leaves.Select(x => x.OwnerId)).Union(statuses.Keys)
            .Select(ownerId =>
            {
                var worked = entries.SingleOrDefault(x => x.OwnerId == ownerId);
                return new MemberMonthSummary(ownerId, worked?.EntryCount ?? 0, worked?.TotalMinutes ?? 0,
                    leaves.SingleOrDefault(x => x.OwnerId == ownerId)?.LeaveDays ?? 0,
                    statuses.GetValueOrDefault(ownerId, Open));
            })
            .OrderByDescending(x => x.TotalMinutes).ToArray();
        return Ok(new { year, month, locked = await IsLocked(year, month, ct), members });
    }

    /// <summary>Tenant Admin view of one member's month: entries with project and category names,
    /// leave, the month's holidays, and the working weekdays. Read-only; another tenant's member simply has no rows.</summary>
    public Task<ServiceResponse> MemberTimesheet(Actor actor, Guid ownerId, int year, int month,
        CancellationToken ct) => actor.IsAdmin
        ? MonthTimesheet(ownerId, year, month, ct)
        : Task.FromResult(Error(403, "FORBIDDEN"));

    /// <summary>Any member's own month in the same shape as <see cref="MemberTimesheet"/>, read live,
    /// so the open month is included.</summary>
    public Task<ServiceResponse> MyTimesheet(Actor actor, int year, int month, CancellationToken ct) =>
        MonthTimesheet(actor.UserId, year, month, ct);

    private async Task<ServiceResponse> MonthTimesheet(Guid ownerId, int year, int month, CancellationToken ct)
    {
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
                x.IncludesBreak, x.UpdatedAtUtc
            }).ToArray();
        var leaves = await db.LeaveEntries.AsNoTracking().Where(x => x.OwnerId == ownerId &&
                !x.IsDeleted && x.Date.Year == year && x.Date.Month == month)
            .OrderBy(x => x.Date).Select(x => new { x.Id, x.Date, x.Kind, x.Notes, x.LeaveTypeId, x.Minutes }).ToListAsync(ct);
        var holidays = await db.Holidays.AsNoTracking().Where(x => !x.IsDeleted &&
                x.Date.Year == year && x.Date.Month == month)
            .OrderBy(x => x.Date).Select(x => new { x.Date, x.Name }).ToListAsync(ct);
        var (workingDays, workHours) = await ScheduleAsync(ct);
        var approval = await db.MonthApprovals.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OwnerId == ownerId && x.Year == year && x.Month == month, ct);
        return Ok(new { ownerId, year, month, locked = await IsLocked(year, month, ct), status = StatusOf(approval),
            returnReason = approval?.ReturnReason, entries, leaves, holidays, workingDays, workHours });
    }

    /// <summary>The working weekdays and hours; an organization without a work week gets the defaults.</summary>
    private async Task<(int[] WorkingDays, object WorkHours)> ScheduleAsync(CancellationToken ct)
    {
        var week = await db.WorkWeeks.AsNoTracking().SingleOrDefaultAsync(ct) ?? new WorkWeek();
        return (WorkWeek.Days(week.WorkingDaysMask), Hours(week));
    }

    /// <summary>Minutes of the normal working day: the working hours less the break.</summary>
    private async Task<int> StandardMinutesAsync(CancellationToken ct)
    {
        var week = await db.WorkWeeks.AsNoTracking().SingleOrDefaultAsync(ct) ?? new WorkWeek();
        var minutes = (int)(week.EndTime - week.StartTime).TotalMinutes;
        return week.BreakStart is { } from && week.BreakEnd is { } to ? minutes - (int)(to - from).TotalMinutes : minutes;
    }

    /// <summary>A leave in days: the whole day, or its minutes against the normal working day, to two places.</summary>
    private static decimal LeaveDays(int? minutes, int standardMinutes) =>
        minutes is { } part && standardMinutes > 0 ? Math.Round((decimal)part / standardMinutes, 2) : 1;

    private static object Hours(WorkWeek week) => new
    {
        startTime = week.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        endTime = week.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        breakStart = week.BreakStart?.ToString("HH:mm", CultureInfo.InvariantCulture),
        breakEnd = week.BreakEnd?.ToString("HH:mm", CultureInfo.InvariantCulture)
    };

    private static bool WholeMinutes(TimeOnly? time) => time is not { } value || value is { Second: 0, Millisecond: 0 };

    private Task<bool> IsLocked(int year, int month, CancellationToken ct) =>
        db.MonthLocks.AnyAsync(x => x.Year == year && x.Month == month, ct);

    private async Task<TenantRecord?> FindCatalog(Guid id, string kind, CancellationToken ct) =>
        kind == "project"
            ? await db.Projects.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            : await db.Categories.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

    private async Task<bool> CanMutate(Actor actor, DateOnly date, CancellationToken ct) =>
        Editable(actor, date.Year, date.Month, await IsLocked(date.Year, date.Month, ct),
            await FindApproval(actor.UserId, date.Year, date.Month, ct));

    /// <summary>A member changes their current month, or a month returned to them, until it is
    /// submitted, approved, or locked.</summary>
    private static bool Editable(Actor actor, int year, int month, bool locked, MonthApproval? approval) =>
        !locked && (approval is null
            ? TimesheetRules.IsCurrentMonth(new DateOnly(year, month, 1), actor.TimeZoneId, DateTimeOffset.UtcNow)
            : approval.Status == MonthApproval.Returned);

    private Task<MonthApproval?> FindApproval(Guid ownerId, int year, int month, CancellationToken ct) =>
        db.MonthApprovals.SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.Year == year && x.Month == month, ct);

    private const string Open = "open";
    private static string StatusOf(MonthApproval? approval) => approval?.Status ?? Open;

    /// <summary>Month snapshot payloads: the organization's (<see cref="Guid.Empty"/>) and one for each
    /// member with entries or leave, or, given a member, that member's alone.</summary>
    private async Task<Dictionary<Guid, string>> SnapshotPayloadsAsync(int year, int month, Guid? ownerId,
        CancellationToken ct)
    {
        var entries = await db.TimeEntries.AsNoTracking().Where(x => !x.IsDeleted &&
            x.Date.Year == year && x.Date.Month == month && (ownerId == null || x.OwnerId == ownerId)).ToListAsync(ct);
        var leaves = await db.LeaveEntries.AsNoTracking().Where(x => !x.IsDeleted &&
            x.Date.Year == year && x.Date.Month == month && (ownerId == null || x.OwnerId == ownerId)).ToListAsync(ct);
        var holidays = await db.Holidays.AsNoTracking().Where(x => !x.IsDeleted &&
            x.Date.Year == year && x.Date.Month == month).ToListAsync(ct);
        var (workingDays, workHours) = await ScheduleAsync(ct);
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
            entry.PersonalTaskId, entry.IncludesBreak, entry.IsDeleted, entry.UpdatedAtUtc
        }).ToArray();
        string Payload(Guid? owner) => JsonSerializer.Serialize(new
        {
            entries = entrySnapshots.Where(x => owner is null || x.OwnerId == owner),
            leaves = leaves.Where(x => owner is null || x.OwnerId == owner), holidays, workingDays, workHours
        });
        if (ownerId is { } member) return new Dictionary<Guid, string> { [member] = Payload(member) };
        var payloads = new Dictionary<Guid, string> { [Guid.Empty] = Payload(null) };
        foreach (var owner in entries.Select(x => x.OwnerId).Concat(leaves.Select(x => x.OwnerId)).Distinct())
            payloads[owner] = Payload(owner);
        return payloads;
    }

    private async Task<bool> ValidReferences(Guid? projectId, Guid? categoryId, CancellationToken ct) =>
        (projectId is null || await db.Projects.AnyAsync(x => x.Id == projectId && !x.IsDeleted, ct)) &&
        (categoryId is null || await db.Categories.AnyAsync(x => x.Id == categoryId && !x.IsDeleted, ct));

    private async Task<bool> ValidTask(TaskRequest body, CancellationToken ct) =>
        !string.IsNullOrWhiteSpace(body.Name) && body.Name.Length <= 200 &&
        body.Status is "todo" or "doing" or "done" &&
        await ValidReferences(body.ProjectId, body.CategoryId, ct);

    private static bool ValidMonth(int year, int month) => year is >= 2000 and <= 2100 &&
        month is >= 1 and <= 12;

    /// <param name="subject">The member the change is about, when it is not the actor.</param>
    private void Audit(Actor actor, string entityType, Guid entityId, string action,
        string? before, object? after, string? reason = null, Guid? subject = null) =>
        db.Audits.Add(new TimesheetAudit { TenantId = actor.TenantId, ActorId = actor.UserId,
            SubjectId = subject ?? actor.UserId, EntityType = entityType, EntityId = entityId, Action = action,
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
