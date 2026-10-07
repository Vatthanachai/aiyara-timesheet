using System.Text.Json;
using System.Data;
using Aiyara.Timesheet.Databases;
using Aiyara.Timesheet.Contracts.Messaging.V1;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Timesheet.Api;

public static class TimesheetEndpoints
{
    public static void MapTimesheetEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Timesheet");
        api.MapGet("/context", async (HttpContext ctx, TimesheetDbContext db) =>
        {
            if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById(actor.TimeZoneId));
            var locked = await db.MonthLocks.AnyAsync(x => x.Year == local.Year && x.Month == local.Month);
            return Results.Ok(new { currentMonth = $"{local.Year:D4}-{local.Month:D2}",
                timeZoneId = actor.TimeZoneId, locked, isAdmin = actor.IsAdmin });
        }).WithName("GetTimesheetContextV1");
        api.MapGet("/catalog", async (HttpContext ctx, TimesheetDbContext db) =>
        {
            if (!Actor.TryRead(ctx, out _)) return Results.Unauthorized();
            return Results.Ok(new
            {
                projects = await db.Projects.Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToListAsync(),
                categories = await db.Categories.Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToListAsync(),
                holidays = await db.Holidays.Where(x => !x.IsDeleted).OrderBy(x => x.Date).ToListAsync()
            });
        }).WithName("GetTimesheetCatalogV1");
        api.MapPost("/projects", (HttpContext ctx, TimesheetDbContext db, NameRequest body) =>
            SaveCatalog(ctx, db, body, "project"));
        api.MapPost("/categories", (HttpContext ctx, TimesheetDbContext db, NameRequest body) =>
            SaveCatalog(ctx, db, body, "category"));
        api.MapPut("/projects/{id:guid}", (HttpContext ctx, TimesheetDbContext db, Guid id, NameRequest body) =>
            UpdateCatalog(ctx, db, id, body, "project"));
        api.MapPut("/categories/{id:guid}", (HttpContext ctx, TimesheetDbContext db, Guid id, NameRequest body) =>
            UpdateCatalog(ctx, db, id, body, "category"));
        api.MapDelete("/projects/{id:guid}", (HttpContext ctx, TimesheetDbContext db, Guid id) =>
            DeleteCatalog(ctx, db, id, "project"));
        api.MapDelete("/categories/{id:guid}", (HttpContext ctx, TimesheetDbContext db, Guid id) =>
            DeleteCatalog(ctx, db, id, "category"));
        api.MapPost("/holidays", async (HttpContext ctx, TimesheetDbContext db, HolidayRequest body) =>
        {
            if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (!actor.IsAdmin) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 200)
                return Results.BadRequest("Holiday name is required and must be at most 200 characters.");
            var holiday = new Holiday { TenantId = actor.TenantId, Date = body.Date, Name = body.Name.Trim() };
            db.Holidays.Add(holiday);
            Audit(db, actor, "holiday", holiday.Id, "created", null, holiday);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/holidays/{holiday.Id}", holiday);
        }).WithName("CreateHolidayV1");
        api.MapDelete("/holidays/{id:guid}", async (HttpContext ctx, TimesheetDbContext db, Guid id) =>
        {
            if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (!actor.IsAdmin) return Results.Forbid();
            var holiday = await db.Holidays.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (holiday is null) return Results.NotFound();
            var before = JsonSerializer.Serialize(holiday);
            holiday.IsDeleted = true;
            Audit(db, actor, "holiday", id, "deleted", before, holiday);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).WithName("DeleteHolidayV1");
        api.MapPut("/holidays/{id:guid}", async (HttpContext ctx, TimesheetDbContext db,
            Guid id, HolidayRequest body) =>
        {
            if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (!actor.IsAdmin) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 200) return Results.BadRequest();
            var holiday = await db.Holidays.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (holiday is null) return Results.NotFound();
            var before = JsonSerializer.Serialize(holiday);
            holiday.Date = body.Date; holiday.Name = body.Name.Trim();
            Audit(db, actor, "holiday", id, "updated", before, holiday);
            await db.SaveChangesAsync();
            return Results.Ok(holiday);
        }).WithName("UpdateHolidayV1");
        api.MapGet("/tasks", async (HttpContext ctx, TimesheetDbContext db) =>
        {
            if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            return Results.Ok(await db.PersonalTasks.Where(x => x.OwnerId == actor.UserId && !x.IsDeleted)
                .OrderBy(x => x.Name).ToListAsync());
        }).WithName("GetPersonalTasksV1");
        api.MapPost("/tasks", async (HttpContext ctx, TimesheetDbContext db, TaskRequest body) =>
        {
            if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 200 ||
                !ValidStatus(body.Status) || !await ValidReferences(db, body.ProjectId, body.CategoryId))
                return Results.BadRequest("Invalid task.");
            var task = new PersonalTask { TenantId = actor.TenantId, OwnerId = actor.UserId,
                Name = body.Name.Trim(), Status = body.Status, ProjectId = body.ProjectId,
                CategoryId = body.CategoryId };
            db.PersonalTasks.Add(task);
            Audit(db, actor, "task", task.Id, "created", null, task);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/tasks/{task.Id}", task);
        }).WithName("CreatePersonalTaskV1");
        api.MapPut("/tasks/{id:guid}", async (HttpContext ctx, TimesheetDbContext db, Guid id, TaskRequest body) =>
        {
            if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            var task = await db.PersonalTasks.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == actor.UserId && !x.IsDeleted);
            if (task is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 200 ||
                !ValidStatus(body.Status) || !await ValidReferences(db, body.ProjectId, body.CategoryId))
                return Results.BadRequest("Invalid task.");
            var before = JsonSerializer.Serialize(task);
            task.Name = body.Name.Trim(); task.Status = body.Status;
            task.ProjectId = body.ProjectId; task.CategoryId = body.CategoryId;
            Audit(db, actor, "task", id, "updated", before, task);
            await db.SaveChangesAsync();
            return Results.Ok(task);
        }).WithName("UpdatePersonalTaskV1");
        api.MapDelete("/tasks/{id:guid}", async (HttpContext ctx, TimesheetDbContext db, Guid id) =>
        {
            if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            var task = await db.PersonalTasks.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == actor.UserId && !x.IsDeleted);
            if (task is null) return Results.NotFound();
            var before = JsonSerializer.Serialize(task);
            task.IsDeleted = true;
            Audit(db, actor, "task", id, "deleted", before, task);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).WithName("DeletePersonalTaskV1");
        api.MapGet("/entries", async (HttpContext ctx, TimesheetDbContext db, int year, int month) =>
        {
            if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (year is < 2000 or > 2100 || month is < 1 or > 12) return Results.BadRequest();
            return Results.Ok(await db.TimeEntries.AsNoTracking()
                .Where(x => x.OwnerId == actor.UserId && !x.IsDeleted && x.Date.Year == year && x.Date.Month == month)
                .OrderBy(x => x.Date).ThenBy(x => x.StartTime).ToListAsync());
        }).WithName("GetTimeEntriesV1");
        api.MapPost("/entries", (HttpContext ctx, TimesheetDbContext db, EntryRequest body) =>
            SaveEntry(ctx, db, null, body)).WithName("CreateTimeEntryV1");
        api.MapPut("/entries/{id:guid}", (HttpContext ctx, TimesheetDbContext db, Guid id, EntryRequest body) =>
            SaveEntry(ctx, db, id, body)).WithName("UpdateTimeEntryV1");
        api.MapDelete("/entries/{id:guid}", async (HttpContext ctx, TimesheetDbContext db, Guid id, string? reason) =>
        {
            if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var entry = await db.TimeEntries.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == actor.UserId && !x.IsDeleted);
            if (entry is null) return Results.NotFound();
            if (!await CanMutate(db, actor, entry.Date)) return Results.Conflict("Month is closed or locked.");
            var before = JsonSerializer.Serialize(entry);
            entry.IsDeleted = true; entry.UpdatedAtUtc = DateTime.UtcNow;
            Audit(db, actor, "entry", id, "deleted", before, entry, reason);
            Changed(db, actor, entry.Date);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.NoContent();
        }).WithName("DeleteTimeEntryV1");
        api.MapGet("/leave", async (HttpContext ctx, TimesheetDbContext db, int year, int month) =>
        {
            if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (year is < 2000 or > 2100 || month is < 1 or > 12) return Results.BadRequest();
            return Results.Ok(await db.LeaveEntries.AsNoTracking()
                .Where(x => x.OwnerId == actor.UserId && !x.IsDeleted && x.Date.Year == year && x.Date.Month == month)
                .OrderBy(x => x.Date).ToListAsync());
        }).WithName("GetLeaveEntriesV1");
        api.MapPost("/leave", (HttpContext ctx, TimesheetDbContext db, LeaveRequest body) =>
            SaveLeave(ctx, db, null, body)).WithName("CreateLeaveEntryV1");
        api.MapPut("/leave/{id:guid}", (HttpContext ctx, TimesheetDbContext db, Guid id, LeaveRequest body) =>
            SaveLeave(ctx, db, id, body)).WithName("UpdateLeaveEntryV1");
        api.MapDelete("/leave/{id:guid}", async (HttpContext ctx, TimesheetDbContext db, Guid id) =>
        {
            if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var leave = await db.LeaveEntries.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == actor.UserId && !x.IsDeleted);
            if (leave is null) return Results.NotFound();
            if (!await CanMutate(db, actor, leave.Date)) return Results.Conflict("Month is closed or locked.");
            var before = JsonSerializer.Serialize(leave);
            leave.IsDeleted = true;
            Audit(db, actor, "leave", id, "deleted", before, leave);
            Changed(db, actor, leave.Date);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.NoContent();
        }).WithName("DeleteLeaveEntryV1");
        api.MapPost("/months/{year:int}/{month:int}/lock", async (HttpContext ctx, TimesheetDbContext db, int year, int month) =>
        {
            if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            if (!actor.IsAdmin) return Results.Forbid();
            if (year is < 2000 or > 2100 || month is < 1 or > 12) return Results.BadRequest();
            if (await db.MonthLocks.AnyAsync(x => x.Year == year && x.Month == month)) return Results.Conflict();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var entries = await db.TimeEntries.AsNoTracking().Where(x => !x.IsDeleted &&
                x.Date.Year == year && x.Date.Month == month).ToListAsync();
            var leaves = await db.LeaveEntries.AsNoTracking().Where(x => !x.IsDeleted &&
                x.Date.Year == year && x.Date.Month == month).ToListAsync();
            var holidays = await db.Holidays.AsNoTracking().Where(x => !x.IsDeleted &&
                x.Date.Year == year && x.Date.Month == month).ToListAsync();
            db.MonthSnapshots.Add(new MonthSnapshot { TenantId = actor.TenantId,
                OwnerId = Guid.Empty, Year = year, Month = month, CreatedAtUtc = DateTime.UtcNow,
                PayloadJson = JsonSerializer.Serialize(new { entries, leaves, holidays }) });
            foreach (var ownerId in entries.Select(x => x.OwnerId).Concat(leaves.Select(x => x.OwnerId)).Distinct())
                db.MonthSnapshots.Add(new MonthSnapshot { TenantId = actor.TenantId, OwnerId = ownerId,
                    Year = year, Month = month, CreatedAtUtc = DateTime.UtcNow,
                    PayloadJson = JsonSerializer.Serialize(new { entries = entries.Where(x => x.OwnerId == ownerId),
                        leaves = leaves.Where(x => x.OwnerId == ownerId), holidays }) });
            var monthLock = new MonthLock { TenantId = actor.TenantId, Year = year, Month = month,
                LockedBy = actor.UserId, LockedAtUtc = DateTime.UtcNow };
            db.MonthLocks.Add(monthLock);
            Audit(db, actor, "month", monthLock.Id, "locked", null, monthLock);
            db.OutboxEvents.Add(new TimesheetOutboxEvent { TenantId = actor.TenantId,
                EventType = MessageTypes.TimesheetMonthLocked, SubjectId = Guid.Empty, Year = year,
                Month = month, OccurredAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Ok(monthLock);
        }).WithName("LockTimesheetMonthV1");
        api.MapGet("/months/{year:int}/{month:int}/snapshot", async (HttpContext ctx,
            TimesheetDbContext db, int year, int month, Guid? ownerId) =>
        {
            if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
            var subject = actor.IsAdmin && ownerId is not null ? ownerId.Value : actor.UserId;
            var snapshot = await db.MonthSnapshots.AsNoTracking().SingleOrDefaultAsync(x =>
                x.OwnerId == subject && x.Year == year && x.Month == month);
            return snapshot is null ? Results.NotFound() : Results.Content(snapshot.PayloadJson, "application/json");
        }).WithName("GetTimesheetMonthSnapshotV1");
    }

    private static async Task<IResult> SaveCatalog(HttpContext ctx, TimesheetDbContext db, NameRequest body, string kind)
    {
        if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
        if (!actor.IsAdmin) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 200) return Results.BadRequest();
        TenantRecord item = kind == "project" ? new Project { TenantId = actor.TenantId, Name = body.Name.Trim() }
            : new Category { TenantId = actor.TenantId, Name = body.Name.Trim() };
        db.Add(item);
        Audit(db, actor, kind, item.Id, "created", null, item);
        await db.SaveChangesAsync();
        return Results.Created($"/api/v1/{kind}s/{item.Id}", item);
    }

    private static async Task<IResult> DeleteCatalog(HttpContext ctx, TimesheetDbContext db, Guid id, string kind)
    {
        if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
        if (!actor.IsAdmin) return Results.Forbid();
        TenantRecord? item = kind == "project" ? await db.Projects.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            : await db.Categories.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (item is null) return Results.NotFound();
        var before = JsonSerializer.Serialize(item, item.GetType());
        if (item is Project project) project.IsDeleted = true;
        if (item is Category category) category.IsDeleted = true;
        Audit(db, actor, kind, id, "deleted", before, item);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateCatalog(HttpContext ctx, TimesheetDbContext db,
        Guid id, NameRequest body, string kind)
    {
        if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
        if (!actor.IsAdmin) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 200) return Results.BadRequest();
        TenantRecord? item = kind == "project" ? await db.Projects.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            : await db.Categories.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (item is null) return Results.NotFound();
        var before = JsonSerializer.Serialize(item, item.GetType());
        if (item is Project project) project.Name = body.Name.Trim();
        if (item is Category category) category.Name = body.Name.Trim();
        Audit(db, actor, kind, id, "updated", before, item);
        await db.SaveChangesAsync();
        return Results.Ok(item);
    }

    private static async Task<IResult> SaveEntry(HttpContext ctx, TimesheetDbContext db, Guid? id, EntryRequest body)
    {
        if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var entry = id is null ? null : await db.TimeEntries.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == actor.UserId && !x.IsDeleted);
        if (id is not null && entry is null) return Results.NotFound();
        if (!await CanMutate(db, actor, body.Date) ||
            entry is not null && !await CanMutate(db, actor, entry.Date)) return Results.Conflict("Month is closed or locked.");
        if (string.IsNullOrWhiteSpace(body.TaskName) || body.TaskName.Length > 200 ||
            body.Detail?.Length > 4000 || body.Notes?.Length > 4000 ||
            !await ValidReferences(db, body.ProjectId, body.CategoryId) ||
            body.PersonalTaskId is not null && !await db.PersonalTasks.AnyAsync(x => x.Id == body.PersonalTaskId && x.OwnerId == actor.UserId && !x.IsDeleted))
            return Results.BadRequest("Invalid entry.");
        int duration;
        try { duration = TimesheetRules.DurationMinutes(body.StartTime, body.EndTime); }
        catch (ArgumentException error) { return Results.BadRequest(error.Message); }
        var before = entry is null ? null : JsonSerializer.Serialize(entry);
        entry ??= new TimeEntry { TenantId = actor.TenantId, OwnerId = actor.UserId, TaskName = body.TaskName.Trim() };
        if (id is null) db.TimeEntries.Add(entry);
        entry.Date = body.Date; entry.StartTime = body.StartTime; entry.EndTime = body.EndTime;
        entry.DurationMinutes = duration; entry.TaskName = body.TaskName.Trim();
        entry.Detail = body.Detail; entry.Notes = body.Notes;
        entry.ProjectId = body.ProjectId; entry.CategoryId = body.CategoryId;
        entry.PersonalTaskId = body.PersonalTaskId; entry.UpdatedAtUtc = DateTime.UtcNow;
        Audit(db, actor, "entry", entry.Id, id is null ? "created" : "updated", before, entry, body.Reason);
        Changed(db, actor, body.Date);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return id is null ? Results.Created($"/api/v1/entries/{entry.Id}", entry) : Results.Ok(entry);
    }

    private static async Task<IResult> SaveLeave(HttpContext ctx, TimesheetDbContext db, Guid? id, LeaveRequest body)
    {
        if (!Actor.TryRead(ctx, out var actor)) return Results.Unauthorized();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var leave = id is null ? null : await db.LeaveEntries.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == actor.UserId && !x.IsDeleted);
        if (id is not null && leave is null) return Results.NotFound();
        if (!await CanMutate(db, actor, body.Date) ||
            leave is not null && !await CanMutate(db, actor, leave.Date)) return Results.Conflict("Month is closed or locked.");
        if (string.IsNullOrWhiteSpace(body.Kind) || body.Kind.Length > 60 || body.Notes?.Length > 4000)
            return Results.BadRequest();
        var before = leave is null ? null : JsonSerializer.Serialize(leave);
        leave ??= new LeaveEntry { TenantId = actor.TenantId, OwnerId = actor.UserId, Kind = body.Kind.Trim() };
        if (id is null) db.LeaveEntries.Add(leave);
        leave.Date = body.Date; leave.Kind = body.Kind.Trim(); leave.Notes = body.Notes;
        Audit(db, actor, "leave", leave.Id, id is null ? "created" : "updated", before, leave);
        Changed(db, actor, body.Date);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return id is null ? Results.Created($"/api/v1/leave/{leave.Id}", leave) : Results.Ok(leave);
    }

    private static async Task<bool> CanMutate(TimesheetDbContext db, Actor actor, DateOnly date) =>
        TimesheetRules.IsCurrentMonth(date, actor.TimeZoneId, DateTimeOffset.UtcNow) &&
        !await db.MonthLocks.AnyAsync(x => x.Year == date.Year && x.Month == date.Month);

    private static async Task<bool> ValidReferences(TimesheetDbContext db, Guid? projectId, Guid? categoryId) =>
        (projectId is null || await db.Projects.AnyAsync(x => x.Id == projectId && !x.IsDeleted)) &&
        (categoryId is null || await db.Categories.AnyAsync(x => x.Id == categoryId && !x.IsDeleted));

    private static bool ValidStatus(string status) => status is "todo" or "doing" or "done";

    private static void Audit(TimesheetDbContext db, Actor actor, string entityType, Guid entityId,
        string action, string? before, object? after, string? reason = null) =>
        db.Audits.Add(new TimesheetAudit { TenantId = actor.TenantId, ActorId = actor.UserId,
            SubjectId = actor.UserId, EntityType = entityType, EntityId = entityId, Action = action,
            BeforeJson = before, AfterJson = after is null ? null : JsonSerializer.Serialize(after, after.GetType()),
            Reason = reason, OccurredAtUtc = DateTime.UtcNow });

    private static void Changed(TimesheetDbContext db, Actor actor, DateOnly date) =>
        db.OutboxEvents.Add(new TimesheetOutboxEvent { TenantId = actor.TenantId,
            EventType = MessageTypes.TimesheetMonthChanged, SubjectId = actor.UserId,
            Year = date.Year, Month = date.Month, OccurredAtUtc = DateTime.UtcNow });
}

public sealed record NameRequest(string Name);
public sealed record HolidayRequest(DateOnly Date, string Name);
public sealed record TaskRequest(string Name, string Status, Guid? ProjectId, Guid? CategoryId);
public sealed record EntryRequest(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    string TaskName, string? Detail, string? Notes, Guid? ProjectId, Guid? CategoryId,
    Guid? PersonalTaskId, string? Reason);
public sealed record LeaveRequest(DateOnly Date, string Kind, string? Notes);

public sealed record Actor(Guid TenantId, Guid UserId, string Role, string TimeZoneId)
{
    public bool IsAdmin => Role is "TenantAdmin" or "PlatformAdmin";

    public static bool TryRead(HttpContext ctx, out Actor actor)
    {
        actor = default!;
        if (!Guid.TryParse(ctx.Request.Headers["X-Tenant-Id"], out var tenantId) ||
            !Guid.TryParse(ctx.Request.Headers["X-User-Id"], out var userId)) return false;
        var zone = ctx.Request.Headers["X-Time-Zone-Id"].ToString();
        if (string.IsNullOrWhiteSpace(zone)) return false;
        try { TimeZoneInfo.FindSystemTimeZoneById(zone); }
        catch (TimeZoneNotFoundException) { return false; }
        actor = new Actor(tenantId, userId, ctx.Request.Headers["X-Role"].ToString(), zone);
        return true;
    }
}
