using System.Text.Json;
using System.Data;
using Aiyara.Timesheet.Databases;
using Aiyara.Timesheet.Contracts.Messaging.V1;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Timesheet.Api;

public sealed record NameRequest(string Name);
public sealed record HolidayRequest(DateOnly Date, string Name);
/// <summary>Working weekdays as <see cref="DayOfWeek"/> numbers (Sunday = 0), and optionally when the
/// working day starts and ends with its break: sending the hours also sets the break (both break
/// times, or neither for none); leaving the hours out keeps the hours and the break.</summary>
public sealed record WorkWeekRequest(int[] WorkingDays, TimeOnly? StartTime = null, TimeOnly? EndTime = null,
    TimeOnly? BreakStart = null, TimeOnly? BreakEnd = null);
public sealed record TaskRequest(string Name, string Status, Guid? ProjectId, Guid? CategoryId);
public sealed record EntryRequest(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    string TaskName, string? Detail, string? Notes, Guid? ProjectId, Guid? CategoryId,
    Guid? PersonalTaskId, string? Reason, bool IncludesBreak = false);
public sealed record LeaveRequest(DateOnly Date, string Kind, string? Notes);
public sealed record MemberMonthSummary(Guid OwnerId, int EntryCount, int TotalMinutes, int LeaveDays);

public sealed record Actor(Guid TenantId, Guid UserId, string Role, string TimeZoneId)
{
    public const string ContextKey = "TimesheetValidatedActor";
    // Tenant data is managed only by that tenant's admins; Platform Admins manage
    // organizations through Identity's platform routes, never tenant settings.
    public bool IsAdmin => Role is "TenantAdmin";

    public static bool TryRead(HttpContext ctx, out Actor actor)
    {
        actor = ctx.Items[ContextKey] as Actor ?? default!;
        return actor is not null;
    }
}
