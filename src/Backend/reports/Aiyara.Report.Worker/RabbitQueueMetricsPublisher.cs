using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using RabbitMQ.Client;

namespace Aiyara.Report.Worker;

internal sealed class RabbitQueueMetricsPublisher(IConfiguration configuration, ILogger<RabbitQueueMetricsPublisher> logger)
    : BackgroundService
{
    private const string Queue = ReportRabbit.GenerateQueue;
    private readonly ConcurrentDictionary<string, long> _values = new(StringComparer.Ordinal);
    private readonly Meter _meter = new("Aiyara.Report.Worker");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _meter.CreateObservableGauge("aiyara.rabbitmq.queue.messages", ObserveMessages,
            description: "Messages ready in the report generation queue.");
        _meter.CreateObservableGauge("aiyara.rabbitmq.queue.consumers", ObserveConsumers,
            description: "Consumers subscribed to the report generation queue.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await using var connection = await ReportRabbit.Factory(configuration)
                    .CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                var queue = await channel.QueueDeclarePassiveAsync(Queue, stoppingToken);
                _values["messages"] = queue.MessageCount;
                _values["consumers"] = queue.ConsumerCount;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogWarning("RabbitMQ queue metrics refresh failed: {FailureType}", error.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private IEnumerable<Measurement<long>> ObserveMessages() => Observe("messages");
    private IEnumerable<Measurement<long>> ObserveConsumers() => Observe("consumers");

    private IEnumerable<Measurement<long>> Observe(string value) =>
        [new Measurement<long>(_values.GetValueOrDefault(value),
            new KeyValuePair<string, object?>("queue", Queue))];

    public override void Dispose()
    {
        _meter.Dispose();
        base.Dispose();
    }
}
