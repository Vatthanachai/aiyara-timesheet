using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Aiyara.Report.Databases;
using Aiyara.Report.Models;
using Microsoft.EntityFrameworkCore;

namespace Aiyara.Report.Worker;

internal sealed class ReportRunMetricsPublisher(IServiceScopeFactory scopes, ILogger<ReportRunMetricsPublisher> logger)
    : BackgroundService
{
    private static readonly string[] Statuses = ["queued", "running", "succeeded", "failed"];
    private readonly ConcurrentDictionary<string, long> _counts = new(StringComparer.Ordinal);
    private readonly Meter _meter = new("Aiyara.Report.Worker");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _meter.CreateObservableGauge("aiyara.report.runs", Observe,
            description: "Current report runs grouped by status across all tenants.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
                var counts = await db.ReportRuns.IgnoreQueryFilters().AsNoTracking()
                    .GroupBy(run => run.Status)
                    .Select(group => new { Status = group.Key, Count = group.LongCount() })
                    .ToListAsync(stoppingToken);

                foreach (var status in Statuses) _counts[status] = 0;
                foreach (var item in counts) _counts[ToMetricStatus(item.Status)] = item.Count;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogWarning("Report run metrics refresh failed: {FailureType}", error.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private IEnumerable<Measurement<long>> Observe()
    {
        foreach (var status in Statuses)
            yield return new Measurement<long>(_counts.GetValueOrDefault(status),
                new KeyValuePair<string, object?>("status", status));
    }

    private static string ToMetricStatus(ReportRunStatus status) => status switch
    {
        ReportRunStatus.Queued => "queued",
        ReportRunStatus.Running => "running",
        ReportRunStatus.Succeeded => "succeeded",
        ReportRunStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown report run status.")
    };

    public override void Dispose()
    {
        _meter.Dispose();
        base.Dispose();
    }
}
