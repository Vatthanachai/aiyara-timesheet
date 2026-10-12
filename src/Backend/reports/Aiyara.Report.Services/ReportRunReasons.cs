using System.Globalization;
using System.Text.RegularExpressions;
using Aiyara.Report.Models;

namespace Aiyara.Report.Services;

/// <summary>Turns a run's stored <c>FailureReason</c> into a stable code for clients, so the
/// API never returns raw exception text. The Worker writes the source-month message through
/// <see cref="SourceMonthNotLocked(DateOnly)"/> and the API parses it back here.</summary>
public static partial class ReportRunReasons
{
    public const string SourceMonthNotLockedCode = "SOURCE_MONTH_NOT_LOCKED";
    public const string GenerationFailedCode = "GENERATION_FAILED";

    public static string SourceMonthNotLocked(DateOnly month) =>
        string.Create(CultureInfo.InvariantCulture, $"Source month {month:yyyy-MM} has not been locked.");

    public static (string? Code, string? BlockedMonth) Describe(ReportRunStatus status, string? failureReason)
    {
        if (status == ReportRunStatus.Failed) return (GenerationFailedCode, null);
        if (status is ReportRunStatus.Queued or ReportRunStatus.Running && failureReason is not null &&
            SourceMonthPattern().Match(failureReason) is { Success: true } match)
            return (SourceMonthNotLockedCode, match.Groups[1].Value);
        return (null, null);
    }

    [GeneratedRegex(@"^Source month (\d{4}-\d{2}) has not been locked\.$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceMonthPattern();
}
