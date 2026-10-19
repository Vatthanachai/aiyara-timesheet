using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Aiyara.Phase1.Tests;

/// <summary>Gateway's readiness probe for Identity gRPC is registered from the channel URL,
/// so it must follow that URL's host and port rather than the Dependencies settings.</summary>
public sealed class DependencyHealthCheckTests
{
    [Fact]
    public async Task Url_dependency_is_ready_only_while_its_port_accepts_connections()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var endpoint = new Uri($"http://127.0.0.1:{port}");

        try
        {
            var reachable = await CheckAsync(endpoint);
            Assert.Equal(HealthStatus.Healthy, reachable.Status);
            Assert.Contains("ready", reachable.Entries["identitygrpc"].Tags);
        }
        finally
        {
            listener.Stop();
        }

        var unreachable = await CheckAsync(endpoint);
        Assert.Equal(HealthStatus.Unhealthy, unreachable.Status);
        Assert.Equal("Cannot connect to identitygrpc.", unreachable.Entries["identitygrpc"].Description);
    }

    private static async Task<HealthReport> CheckAsync(Uri endpoint)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddTcpDependencyHealthCheck("IdentityGrpc", endpoint);
        using var host = builder.Build();
        return await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
    }
}
