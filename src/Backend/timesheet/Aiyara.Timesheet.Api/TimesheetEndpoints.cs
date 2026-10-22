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
/// <summary>Whether the daily and month-end reminders are sent, and at what local time (HH:mm).</summary>
public sealed record ReminderSettingsRequest(bool DailyEnabled, bool MonthEndEnabled, TimeOnly SendAt);
public sealed record TaskRequest(string Name, string Status, Guid? ProjectId, Guid? CategoryId);
public sealed record EntryRequest(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    string TaskName, string? Detail, string? Notes, Guid? ProjectId, Guid? CategoryId,
    Guid? PersonalTaskId, string? Reason, bool IncludesBreak = false);
/// <summary>A day of leave: with the organization's leave types, <c>LeaveTypeId</c> names the type
/// and <c>Kind</c> is ignored; without them <c>Kind</c> is free text. <c>Minutes</c> is a part of the
/// day (at most the normal working day); leave it out for the whole day.</summary>
public sealed record LeaveRequest(DateOnly Date, string? Kind, string? Notes, Guid? LeaveTypeId = null, int? Minutes = null);
/// <summary>A leave type and the days a member may take each calendar year (0–366 in half days); null is no limit.</summary>
public sealed record LeaveTypeRequest(string Name, decimal? AnnualDays);
/// <summary>One leave type's year: its entitlement, the days taken (part days against the normal
/// working day), and what is left (null without a limit).</summary>
public sealed record LeaveBalance(Guid LeaveTypeId, string Name, decimal? AnnualDays, decimal UsedDays, decimal? RemainingDays);
/// <summary>Why a Tenant Admin returns a submitted month to its member (1–1000 characters).</summary>
public sealed record ReturnRequest(string? Reason);
/// <param name="Status">The member's approval status for the month: open, submitted, approved, or returned.</param>
/// <param name="LeaveDays">Days of leave, part days counted against the normal working day.</param>
public sealed record MemberMonthSummary(Guid OwnerId, int EntryCount, int TotalMinutes, decimal LeaveDays, string Status);

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
