var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSingleton<IReportGenerationQueue, InMemoryReportGenerationQueue>();
builder.Services.AddHostedService<ReportGenerationWorker>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Ok(new { service = "report-worker", status = "ready" }));

app.Run();

internal sealed record ReportGenerationRequest(string TenantId, string ReportType, string IdempotencyKey);

/// <summary>
/// Boundary for the future RabbitMQ consumer. The worker remains transport-agnostic.
/// </summary>
internal interface IReportGenerationQueue
{
    ValueTask<ReportGenerationRequest> DequeueAsync(CancellationToken cancellationToken);
}

internal sealed class InMemoryReportGenerationQueue : IReportGenerationQueue
{
    public async ValueTask<ReportGenerationRequest> DequeueAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new OperationCanceledException(cancellationToken);
    }
}

internal sealed class ReportGenerationWorker(
    IReportGenerationQueue queue,
    ILogger<ReportGenerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Report worker is ready for Quartz scheduling and RabbitMQ commands.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var request = await queue.DequeueAsync(stoppingToken);
                logger.LogInformation(
                    "Received report generation request {ReportType} for tenant {TenantId} with key {IdempotencyKey}.",
                    request.ReportType,
                    request.TenantId,
                    request.IdempotencyKey);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
