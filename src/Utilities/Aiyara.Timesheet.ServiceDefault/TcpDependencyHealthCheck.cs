using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.Hosting;

internal sealed class TcpDependencyHealthCheck(string host, int port) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"Cannot connect to {context.Registration.Name}.");
        }
    }
}
