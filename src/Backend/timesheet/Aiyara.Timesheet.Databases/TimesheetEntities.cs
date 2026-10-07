namespace Aiyara.Timesheet.Databases;

public abstract class TenantRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
}

public sealed class Project : TenantRecord
{
    public required string Name { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class Category : TenantRecord
{
    public required string Name { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class PersonalTask : TenantRecord
{
    public Guid OwnerId { get; set; }
    public required string Name { get; set; }
    public string Status { get; set; } = "todo";
    public Guid? ProjectId { get; set; }
    public Guid? CategoryId { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class Holiday : TenantRecord
{
    public DateOnly Date { get; set; }
    public required string Name { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class LeaveEntry : TenantRecord
{
    public Guid OwnerId { get; set; }
    public DateOnly Date { get; set; }
    public required string Kind { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class TimeEntry : TenantRecord
{
    public Guid OwnerId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public required string TaskName { get; set; }
    public string? Detail { get; set; }
    public string? Notes { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? PersonalTaskId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class MonthLock : TenantRecord
{
    public int Year { get; set; }
    public int Month { get; set; }
    public Guid LockedBy { get; set; }
    public DateTime LockedAtUtc { get; set; }
}

public sealed class TimesheetAudit : TenantRecord
{
    public Guid ActorId { get; set; }
    public Guid SubjectId { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string Action { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Reason { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class TimesheetOutboxEvent : TenantRecord
{
    public required string EventType { get; set; }
    public Guid SubjectId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string TimeZoneId { get; set; } = "Asia/Bangkok";
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

public sealed class MonthSnapshot : TenantRecord
{
    public Guid OwnerId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public required string PayloadJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
