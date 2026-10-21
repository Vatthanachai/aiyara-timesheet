namespace Aiyara.Timesheet.Api;

public static class TimesheetRules
{
    public static int DurationMinutes(TimeOnly start, TimeOnly end)
    {
        var minutes = (int)(end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
        if (minutes < 0) minutes += 24 * 60;
        if (minutes <= 0 || minutes > 24 * 60)
            throw new ArgumentException("Entry duration must be greater than zero and at most 24 hours.");
        return minutes;
    }

    /// <summary>Minutes of an entry that fall in the break, counting both halves of an entry that
    /// runs past midnight. The break itself never crosses midnight.</summary>
    public static int BreakOverlapMinutes(TimeOnly start, TimeOnly end, TimeOnly breakStart, TimeOnly breakEnd)
    {
        static int Minutes(TimeOnly time) => time.Hour * 60 + time.Minute;
        static int Overlap(int from, int to, int breakFrom, int breakTo) => Math.Max(0, Math.Min(to, breakTo) - Math.Max(from, breakFrom));
        int s = Minutes(start), e = Minutes(end), bs = Minutes(breakStart), be = Minutes(breakEnd);
        return e > s ? Overlap(s, e, bs, be) : Overlap(s, 24 * 60, bs, be) + Overlap(0, e, bs, be);
    }

    /// <summary>The minutes an entry counts as work: its length less any break it overlaps, unless
    /// the member says they worked through the break.</summary>
    public static int WorkedMinutes(TimeOnly start, TimeOnly end, TimeOnly? breakStart, TimeOnly? breakEnd,
        bool includesBreak)
    {
        var minutes = DurationMinutes(start, end);
        return includesBreak || breakStart is not { } from || breakEnd is not { } to
            ? minutes : minutes - BreakOverlapMinutes(start, end, from, to);
    }

    public static bool IsCurrentMonth(DateOnly date, string timeZoneId, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        return date.Year == local.Year && date.Month == local.Month;
    }
}
