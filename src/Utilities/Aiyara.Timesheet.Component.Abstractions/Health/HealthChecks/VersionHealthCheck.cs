using Aiyara.Timesheet.Component.Abstractions.Extensions;
using Aiyara.Timesheet.Component.Abstractions.Health.HealthChecks.Settings;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aiyara.Timesheet.Component.Abstractions.Health.HealthChecks;

/// <summary>
/// Health check for validating the release version settings.
/// </summary>
/// <param name="configuration"></param>
public class VersionHealthCheck(IConfiguration configuration) : IHealthCheck
{
    private readonly ReleaseSettings _settings = configuration.GetOptions<ReleaseSettings>(nameof(ReleaseSettings));

    /// <summary>
    /// Constructor for the VersionHealthCheck class.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        bool isMissingConfig = _settings.IsNullable() || _settings.Version.IsEmpty();
        var description = "Release version health result.";
        var status = !isMissingConfig ? HealthStatus.Healthy : HealthStatus.Unhealthy;
        var values = new Dictionary<string, object> { { nameof(_settings.Version), _settings.Version } };
        return Task.FromResult(new HealthCheckResult(status, description, null, values));
    }
}