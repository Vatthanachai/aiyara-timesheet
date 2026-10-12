using Aiyara.Timesheet.Component.Abstractions.Extensions;
using Aiyara.Timesheet.Component.Data.Context;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aiyara.Timesheet.Component.Abstractions.Health;

/// <summary>
/// Health check for validating the database context.
/// </summary>
internal class DbContextHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _service;
    private readonly Type[] _dbContextTypes;

    /// <summary>
    /// ต้องเปลี่ยน <see cref="service"/> ให้ตรงกับ DbContext ที่ต้องการ.
    /// </summary>
    /// <param name="dbContextTypes"></param>
    /// <param name="dbContextTypes"></param>
    public DbContextHealthCheck(IServiceProvider service, params Type[] dbContextTypes)
    {
        _service = service;
        _dbContextTypes = dbContextTypes;
    }

    /// <summary>
    /// Asynchronously checks the health of the database context.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var result = await ValidationCanConnect();
        var status = result.SelectMany(s => s.Value as Dictionary<string, bool>).Any(w => !w.Value)
            ? HealthStatus.Unhealthy
            : HealthStatus.Healthy;

        return new HealthCheckResult(status, "Connect database result.", null, result);
    }

    /// <summary>
    /// Validates the database context and returns a dictionary with the results.
    /// </summary>
    /// <returns></returns>
    private async Task<Dictionary<string, object>> ValidationCanConnect()
    {
        var values = new Dictionary<string, object>();
        foreach (var item in _dbContextTypes)
        {
            var dbContext = (IBaseDbContext)_service.GetTryService(item);
            bool canConnect = !dbContext.IsNullable() && await dbContext.Database.CanConnectAsync();
            values.Add(item.Name, new Dictionary<string, bool> { { "CanConnect", canConnect } });
        }

        return values;
    }
}