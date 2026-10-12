using Aiyara.Timesheet.Component.Abstractions.Health;
using Aiyara.Timesheet.Component.Abstractions.Health.HealthChecks;
using Aiyara.Timesheet.Component.Data.Context;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aiyara.Timesheet.Component.Abstractions.Extensions;

public static class ApiExtension
{
    public static IHealthChecksBuilder HealthCheckRegister(this IHealthChecksBuilder builder)
    {
        // "self" is already registered by ServiceDefault's AddDefaultHealthChecks() (called via
        // builder.AddServiceDefaults() in Program.cs) - registering it again throws at startup
        // ("Duplicate health checks were registered with the name(s): self").
        builder.AddCheck<VersionHealthCheck>("version", tags: ["version"]);
        builder.AddDbContextHealthCheck([typeof(IBaseDbContext)]);


        return builder;
    }

    public static IHealthChecksBuilder AddDbContextHealthCheck(this IHealthChecksBuilder builder,
        params Type[] dbContextTypes)
        => builder.AddTypeActivatedCheck<DbContextHealthCheck>("dbcontexts", HealthStatus.Healthy
            , tags: ["dbcontexts"]
            , args: [dbContextTypes]);
}