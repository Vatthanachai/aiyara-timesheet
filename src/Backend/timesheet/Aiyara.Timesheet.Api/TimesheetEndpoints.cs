using System.Text.Json;
using System.Data;
using Aiyara.Timesheet.Databases;
using Aiyara.Timesheet.Contracts.Messaging.V1;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Timesheet.Api;

public record NameRequest(string Name);
/// <summary>A new project: its name and, optionally, its billing details (see <see cref="ProjectBillingRequest"/>).</summary>
public sealed record ProjectRequest(string Name, string? Client = null, bool IsBillable = false,
    decimal? HourlyRate = null, string? CurrencyCode = null, decimal? BudgetHours = null) : NameRequest(Name);
/// <summary>A project's billing details, replaced as a whole: <c>Client</c> (free text, at most 200 characters),
/// whether its time is billable, the hourly rate (0 to 1,000,000, two decimals) with its ISO 4217
/// <c>CurrencyCode</c> (required with a rate), and the hours budget (above 0 to 1,000,000, two decimals).
/// Null clears a value.</summary>
public sealed record ProjectBillingRequest(string? Client, bool IsBillable, decimal? HourlyRate, string? CurrencyCode,
    decimal? BudgetHours);
public sealed record HolidayRequest(DateOnly Date, string Name);
/// <summary>Working weekdays as <see cref="DayOfWeek"/> numbers (Sunday = 0), and optionally when the
/// working day starts and ends with its break: sending the hours also sets the break (both break
/// times, or neither for none); leaving the hours out keeps the hours and the break.</summary>
public sealed record WorkWeekRequest(int[] WorkingDays, TimeOnly? StartTime = null, TimeOnly? EndTime = null,
    TimeOnly? BreakStart = null, TimeOnly? BreakEnd = null);
/// <summary>Whether the daily and month-end reminders are sent, and at what local time (HH:mm).</summary>
public sealed record ReminderSettingsRequest(bool DailyEnabled, bool MonthEndEnabled, TimeOnly SendAt);
public sealed record TaskRequest(string Name, string Status, Guid? ProjectId, Guid? CategoryId);
/// <summary><c>Id</c> is read only when creating: a client that may send the same create twice (an offline
/// queue replaying after a lost answer) names the entry itself, and a repeat of that id answers with the
/// entry already written instead of writing another. It is ignored on update.</summary>
public sealed record EntryRequest(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    string TaskName, string? Detail, string? Notes, Guid? ProjectId, Guid? CategoryId,
    Guid? PersonalTaskId, string? Reason, bool IncludesBreak = false, Guid? Id = null);
/// <summary>One entry written on each of 1–31 distinct dates, all or none.</summary>
public sealed record BulkEntryRequest(DateOnly[] Dates, TimeOnly StartTime, TimeOnly EndTime,
    string TaskName, string? Detail, string? Notes, Guid? ProjectId, Guid? CategoryId,
    Guid? PersonalTaskId, string? Reason, bool IncludesBreak = false);
/// <summary>Copies the member's entries on <c>Days</c> (1–7) days from <c>From</c> onto as many days from <c>To</c>.</summary>
public sealed record CopyEntriesRequest(DateOnly From, DateOnly To, int Days = 1);
/// <summary>A day a copy left alone: <c>holiday</c>, <c>day-off</c>, <c>leave</c> (whole day), or <c>has-entries</c>.</summary>
public sealed record SkippedDay(DateOnly Date, string Reason);
/// <summary>A member's saved entry: its name (1–100 characters, unique for the member) and what it fills in.</summary>
public sealed record EntryTemplateRequest(string Name, string TaskName, TimeOnly StartTime, TimeOnly EndTime,
    Guid? ProjectId, Guid? CategoryId, string? Detail, string? Notes, bool IncludesBreak = false);
/// <summary>What a member fills in when starting a timer; all of it may be left out and given when stopping.</summary>
public sealed record StartTimerRequest(string? TaskName = null, string? Detail = null, string? Notes = null,
    Guid? ProjectId = null, Guid? CategoryId = null, Guid? PersonalTaskId = null, bool IncludesBreak = false);
/// <summary>Stops the timer into an entry. A field left out keeps what the timer holds, or the clock: the
/// entry's date and start time are the timer's start and its end time is now, in the organization's zone.
/// Sending them places the entry elsewhere, for a timer left running past midnight or into a closed month.</summary>
public sealed record StopTimerRequest(string? TaskName = null, string? Detail = null, string? Notes = null,
    Guid? ProjectId = null, Guid? CategoryId = null, Guid? PersonalTaskId = null, bool? IncludesBreak = null,
    DateOnly? Date = null, TimeOnly? StartTime = null, TimeOnly? EndTime = null, string? Reason = null);
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
