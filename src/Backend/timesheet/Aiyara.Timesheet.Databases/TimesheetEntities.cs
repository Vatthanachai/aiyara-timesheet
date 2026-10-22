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

/// <summary>The organization's working weekdays (bit <c>n</c> is <see cref="DayOfWeek"/> <c>n</c>) and
/// the hours of its working day. An organization without a row works Monday to Friday, 09:00–17:00.</summary>
public sealed class WorkWeek : TenantRecord
{
    public const int DefaultMask = 0b0111110;
    public static readonly TimeOnly DefaultStartTime = new(9, 0);
    public static readonly TimeOnly DefaultEndTime = new(17, 0);
    public int WorkingDaysMask { get; set; } = DefaultMask;
    public TimeOnly StartTime { get; set; } = DefaultStartTime;
    public TimeOnly EndTime { get; set; } = DefaultEndTime;
    /// <summary>A break inside the working day that does not count as work; null when there is none.</summary>
    public TimeOnly? BreakStart { get; set; }
    public TimeOnly? BreakEnd { get; set; }

    public static int[] Days(int mask) => Enumerable.Range(0, 7).Where(day => (mask & (1 << day)) != 0).ToArray();
}

/// <summary>A kind of leave the organization offers, with the days a member may take each calendar
/// year; null days means no limit.</summary>
public sealed class LeaveType : TenantRecord
{
    public required string Name { get; set; }
    public decimal? AnnualDays { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class LeaveEntry : TenantRecord
{
    public Guid OwnerId { get; set; }
    public DateOnly Date { get; set; }
    /// <summary>The leave's name: its type's name when it has one, as it was when the leave was saved.</summary>
    public required string Kind { get; set; }
    public Guid? LeaveTypeId { get; set; }
    /// <summary>Minutes of leave in a part of the day; null for the whole working day.</summary>
    public int? Minutes { get; set; }
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
    /// <summary>The member worked through the organization's break, so it counts as work.</summary>
    public bool IncludesBreak { get; set; }
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

/// <summary>A member's month in the approval flow. A month without a row is open.</summary>
public sealed class MonthApproval : TenantRecord
{
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Returned = "returned";
    public Guid OwnerId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public required string Status { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? ReturnReason { get; set; }
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
