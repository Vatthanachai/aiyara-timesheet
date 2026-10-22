using System.Globalization;
using Aiyara.Timesheet.Databases;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Timesheet.Api;

/// <summary>A member who may be reminded; <c>JoinedAtUtc</c> is when the membership was created.</summary>
public sealed record ReminderMember(Guid Id, string Email, string Name, DateTime JoinedAtUtc);

/// <summary>A reminder email. <c>Period</c> is the day (daily) or the month (month-end) it is about;
/// <c>MissingDays</c> are working days with nothing logged.</summary>
public sealed record ReminderEmail(string Email, string MemberName, string Kind, string Period,
    decimal LoggedHours, decimal ExpectedHours, IReadOnlyList<string> MissingDays);

/// <summary>The organization's time zone and its members, from Identity.</summary>
public interface IReminderDirectory
{
    /// <summary>The organization's time zone, or null for an unknown organization.</summary>
    Task<string?> TimeZoneAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<ReminderMember>> MembersAsync(Guid tenantId, CancellationToken ct);
}

public interface IReminderSender
{
    /// <summary>Sends the email; best effort, never throws.</summary>
    Task SendAsync(ReminderEmail email, CancellationToken ct);
}

/// <summary>Emails members about time they have not logged: the daily reminder about the previous
/// working day, and the month-end reminder on the month's last working day. See the Missing Entry
/// Reminders Plan.</summary>
public sealed class MissingEntryReminders(TimesheetDbContext db, TimesheetTenantScope scope,
    IReminderDirectory directory, IReminderSender sender, ILogger<MissingEntryReminders> logger)
{
    private const int MaxMissingDays = 10;

    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var tenants = await db.ReminderSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.DailyEnabled || x.MonthEndEnabled).Select(x => x.TenantId).ToListAsync(ct);
        foreach (var tenantId in tenants)
        {
            scope.TenantId = tenantId;
            try { await RunTenantAsync(tenantId, now, ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // The day is not marked as run, so the next minute tries again.
                db.ChangeTracker.Clear();
                logger.LogWarning("Reminders for tenant {TenantId} failed: {FailureType}.", tenantId, error.GetType().Name);
            }
        }
    }

    private async Task RunTenantAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var settings = await db.ReminderSettings.SingleAsync(ct);
        var timeZoneId = await directory.TimeZoneAsync(tenantId, ct);
        if (timeZoneId is null || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)) return;
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var today = DateOnly.FromDateTime(local.DateTime);
        if (TimeOnly.FromDateTime(local.DateTime) < settings.SendAt) return;
        var dailyDue = settings.DailyEnabled && settings.DailyRanOn != today;
        var monthEndDue = settings.MonthEndEnabled && settings.MonthEndRanOn != today;
        if (!dailyDue && !monthEndDue) return;

        var calendar = await CalendarAsync(today, ct);
        IReadOnlyList<ReminderMember>? members = null;
        async Task<IReadOnlyList<ReminderMember>> Members() => members ??= await directory.MembersAsync(tenantId, ct);
        if (dailyDue)
        {
            if (calendar.IsWorkingDay(today) && calendar.PreviousWorkingDay(today) is { } day &&
                day.Month == today.Month && day.Year == today.Year && !calendar.Locked)
                await DailyAsync(day, zone, await Members(), ct);
            settings.DailyRanOn = today;
        }
        if (monthEndDue)
        {
            if (calendar.IsLastWorkingDay(today) && !calendar.Locked)
                await MonthEndAsync(today, zone, calendar, await Members(), ct);
            settings.MonthEndRanOn = today;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Members who joined by <paramref name="day"/>, logged nothing on it, and can still change its month.</summary>
    private async Task DailyAsync(DateOnly day, TimeZoneInfo zone, IReadOnlyList<ReminderMember> members,
        CancellationToken ct)
    {
        var logged = await LoggedOwnersAsync(day, day, ct);
        var closed = await ClosedOwnersAsync(day.Year, day.Month, ct);
        foreach (var member in members)
        {
            if (Joined(member, zone) > day || logged.Contains(member.Id) || closed.Contains(member.Id)) continue;
            await DeliverAsync(member, ReminderDelivery.Daily, day, new ReminderEmail(member.Email, member.Name,
                ReminderDelivery.Daily, day.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture), 0, 0, []), ct);
        }
    }

    /// <summary>Members whose hours and leave fall short of the month's working days before
    /// <paramref name="today"/> times the normal working day.</summary>
    private async Task MonthEndAsync(DateOnly today, TimeZoneInfo zone, Calendar calendar,
        IReadOnlyList<ReminderMember> members, CancellationToken ct)
    {
        var first = new DateOnly(today.Year, today.Month, 1);
        var days = Enumerable.Range(0, today.Day - 1).Select(first.AddDays).Where(calendar.IsWorkingDay).ToList();
        if (days.Count == 0) return;
        var last = today.AddDays(-1);
        var entries = await db.TimeEntries.AsNoTracking().Where(x => !x.IsDeleted && x.Date >= first && x.Date <= last)
            .Select(x => new { x.OwnerId, x.Date, x.DurationMinutes }).ToListAsync(ct);
        var leaves = await db.LeaveEntries.AsNoTracking().Where(x => !x.IsDeleted && x.Date >= first && x.Date <= last)
            .Select(x => new { x.OwnerId, x.Date, x.Minutes }).ToListAsync(ct);
        var closed = await ClosedOwnersAsync(today.Year, today.Month, ct);
        foreach (var member in members)
        {
            if (closed.Contains(member.Id)) continue;
            var joined = Joined(member, zone);
            var expectedDays = days.Where(day => day >= joined).ToList();
            if (expectedDays.Count == 0) continue;
            var expected = expectedDays.Count * calendar.StandardMinutes;
            var worked = entries.Where(x => x.OwnerId == member.Id && x.Date >= joined).Sum(x => x.DurationMinutes);
            var leave = leaves.Where(x => x.OwnerId == member.Id && expectedDays.Contains(x.Date))
                .Sum(x => x.Minutes ?? calendar.StandardMinutes);
            if (worked + leave >= expected) continue;
            var covered = entries.Where(x => x.OwnerId == member.Id).Select(x => x.Date)
                .Concat(leaves.Where(x => x.OwnerId == member.Id).Select(x => x.Date)).ToHashSet();
            var missing = expectedDays.Where(day => !covered.Contains(day)).Take(MaxMissingDays)
                .Select(day => day.ToString("ddd d MMM", CultureInfo.InvariantCulture)).ToList();
            await DeliverAsync(member, ReminderDelivery.MonthEnd, today, new ReminderEmail(member.Email, member.Name,
                ReminderDelivery.MonthEnd, today.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
                Hours(worked + leave), Hours(expected), missing), ct);
        }
    }

    /// <summary>Records the delivery first, so a second run or another worker finds it and sends nothing.</summary>
    private async Task DeliverAsync(ReminderMember member, string kind, DateOnly date, ReminderEmail email,
        CancellationToken ct)
    {
        if (await db.ReminderDeliveries.AnyAsync(x => x.OwnerId == member.Id && x.Kind == kind && x.Date == date, ct))
            return;
        var delivery = new ReminderDelivery { TenantId = scope.TenantId!.Value, OwnerId = member.Id, Kind = kind,
            Date = date, CreatedAtUtc = DateTime.UtcNow };
        db.ReminderDeliveries.Add(delivery);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(delivery).State = EntityState.Detached;
            return;
        }
        await sender.SendAsync(email, ct);
    }

    private async Task<HashSet<Guid>> LoggedOwnersAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        (await db.TimeEntries.AsNoTracking().Where(x => !x.IsDeleted && x.Date >= from && x.Date <= to)
            .Select(x => x.OwnerId)
            .Concat(db.LeaveEntries.AsNoTracking().Where(x => !x.IsDeleted && x.Date >= from && x.Date <= to)
                .Select(x => x.OwnerId))
            .ToListAsync(ct)).ToHashSet();

    /// <summary>Members whose month is submitted or approved, so it can no longer be changed.</summary>
    private async Task<HashSet<Guid>> ClosedOwnersAsync(int year, int month, CancellationToken ct) =>
        (await db.MonthApprovals.AsNoTracking().Where(x => x.Year == year && x.Month == month &&
            (x.Status == MonthApproval.Submitted || x.Status == MonthApproval.Approved))
            .Select(x => x.OwnerId).ToListAsync(ct)).ToHashSet();

    private static DateOnly Joined(ReminderMember member, TimeZoneInfo zone) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(member.JoinedAtUtc, DateTimeKind.Utc), zone));

    private static decimal Hours(int minutes) => Math.Round(minutes / 60m, 2);

    private async Task<Calendar> CalendarAsync(DateOnly today, CancellationToken ct)
    {
        var week = await db.WorkWeeks.AsNoTracking().SingleOrDefaultAsync(ct) ?? new WorkWeek();
        // Two weeks back is enough to find the previous working day past a long holiday.
        var from = today.AddDays(-14);
        var to = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        var holidays = (await db.Holidays.AsNoTracking().Where(x => !x.IsDeleted && x.Date >= from && x.Date < to)
            .Select(x => x.Date).ToListAsync(ct)).ToHashSet();
        var minutes = (int)(week.EndTime - week.StartTime).TotalMinutes;
        if (week.BreakStart is { } breakStart && week.BreakEnd is { } breakEnd) minutes -= (int)(breakEnd - breakStart).TotalMinutes;
        var locked = await db.MonthLocks.AnyAsync(x => x.Year == today.Year && x.Month == today.Month, ct);
        return new Calendar(week.WorkingDaysMask, holidays, minutes, locked);
    }

    /// <param name="Locked">The current month is locked.</param>
    private sealed record Calendar(int WorkingDaysMask, HashSet<DateOnly> Holidays, int StandardMinutes, bool Locked)
    {
        public bool IsWorkingDay(DateOnly day) =>
            (WorkingDaysMask & (1 << (int)day.DayOfWeek)) != 0 && !Holidays.Contains(day);

        public DateOnly? PreviousWorkingDay(DateOnly day)
        {
            for (var back = 1; back <= 14; back++)
                if (IsWorkingDay(day.AddDays(-back))) return day.AddDays(-back);
            return null;
        }

        public bool IsLastWorkingDay(DateOnly day)
        {
            if (!IsWorkingDay(day)) return false;
            for (var next = day.AddDays(1); next.Month == day.Month; next = next.AddDays(1))
                if (IsWorkingDay(next)) return false;
            return true;
        }
    }
}
