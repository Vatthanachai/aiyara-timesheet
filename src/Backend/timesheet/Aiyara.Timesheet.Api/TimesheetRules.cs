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

    public static bool IsCurrentMonth(DateOnly date, string timeZoneId, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        return date.Year == local.Year && date.Month == local.Month;
    }
}
