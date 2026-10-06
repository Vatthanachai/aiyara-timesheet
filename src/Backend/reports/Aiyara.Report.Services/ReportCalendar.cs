using Aiyara.Report.Models;

namespace Aiyara.Report.Services;

public readonly record struct ReportPeriod(DateTimeOffset StartUtc, DateTimeOffset EndUtc);

public static class ReportCalendar
{
    public static DateTimeOffset NextFireUtc(ReportKind kind, string timeZoneId,
        DateTimeOffset afterUtc, TimeOnly? localTime = null)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(afterUtc, zone).DateTime;
        var time = kind == ReportKind.Monthly ? new TimeOnly(0, 15) :
            localTime ?? new TimeOnly(0, 15);
        DateTime candidate = kind switch
        {
            ReportKind.Weekly => localNow.Date.AddDays(
                ((int)DayOfWeek.Monday - (int)localNow.DayOfWeek + 7) % 7).Add(time.ToTimeSpan()),
            ReportKind.Monthly => new DateTime(localNow.Year, localNow.Month, 1)
                .Add(time.ToTimeSpan()),
            ReportKind.Annual => new DateTime(localNow.Year, 1, 1)
                .Add(time.ToTimeSpan()),
            _ => throw new ArgumentOutOfRangeException(nameof(kind),
                "This report kind has no automatic calendar schedule.")
        };

        while (ResolveLocal(candidate, zone) <= afterUtc)
        {
            candidate = kind switch
            {
                ReportKind.Weekly => candidate.AddDays(7),
                ReportKind.Monthly => candidate.AddMonths(1),
                ReportKind.Annual => candidate.AddYears(1),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }
        return ResolveLocal(candidate, zone);
    }

    public static ReportPeriod ClosedPeriod(ReportKind kind, string timeZoneId,
        DateTimeOffset scheduledAtUtc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(scheduledAtUtc, zone).Date);
        DateOnly end = kind switch
        {
            ReportKind.Weekly => localDate.AddDays(
                -(((int)localDate.DayOfWeek + 6) % 7)),
            ReportKind.Monthly => new DateOnly(localDate.Year, localDate.Month, 1),
            ReportKind.Annual => new DateOnly(localDate.Year, 1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(kind),
                "This report kind has no automatic calendar period.")
        };
        var start = kind switch
        {
            ReportKind.Weekly => end.AddDays(-7),
            ReportKind.Monthly => end.AddMonths(-1),
            ReportKind.Annual => end.AddYears(-1),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return new ReportPeriod(ResolveLocal(start.ToDateTime(TimeOnly.MinValue), zone),
            ResolveLocal(end.ToDateTime(TimeOnly.MinValue), zone));
    }

    private static DateTimeOffset ResolveLocal(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddMinutes(1);
        if (zone.IsAmbiguousTime(unspecified))
            return new DateTimeOffset(unspecified,
                zone.GetAmbiguousTimeOffsets(unspecified).Max()).ToUniversalTime();
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone),
            TimeSpan.Zero);
    }
}
