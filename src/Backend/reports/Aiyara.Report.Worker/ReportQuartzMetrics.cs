using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Aiyara.Report.Worker;

internal static class ReportQuartzMetrics
{
    private static readonly Meter Meter = new("Aiyara.Report.Worker");
    private static readonly Counter<long> Executions = Meter.CreateCounter<long>(
        "aiyara.quartz.job.executions", description: "Report Quartz job outcomes.");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "aiyara.quartz.job.duration", unit: "s", description: "Report Quartz job duration.");

    public static async ValueTask MeasureAsync(string job, Func<ValueTask> execute)
    {
        var stopwatch = Stopwatch.StartNew();
        var outcome = "success";
        try
        {
            await execute();
        }
        catch
        {
            outcome = "failure";
            throw;
        }
        finally
        {
            Executions.Add(1, new KeyValuePair<string, object?>("job", job),
                new KeyValuePair<string, object?>("outcome", outcome));
            Duration.Record(stopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("job", job));
        }
    }
}
