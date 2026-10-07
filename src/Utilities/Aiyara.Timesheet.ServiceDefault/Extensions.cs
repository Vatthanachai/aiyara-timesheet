using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureStructuredLogging();

        builder.Services.AddRedaction(redaction =>
        {
            redaction.SetRedactor<ErasingRedactor>(TimesheetDataClassification.Sensitive);
            redaction.SetFallbackRedactor<ErasingRedactor>();
        });

        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    private static TBuilder ConfigureStructuredLogging<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "O";
            options.JsonWriterOptions = new() { Indented = false };
        });
        builder.Logging.EnableRedaction();

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var openTelemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            });

        var metricsEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(metricsEndpoint))
        {
            if (!Uri.TryCreate(metricsEndpoint, UriKind.Absolute, out var endpoint))
                throw new InvalidOperationException(
                    "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT must be an absolute URI.");

            openTelemetry.WithMetrics(metrics => metrics.AddOtlpExporter(options =>
            {
                options.Endpoint = endpoint;
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
            }));
        }

        openTelemetry.WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static TBuilder AddTcpDependencyHealthCheck<TBuilder>(
        this TBuilder builder, string dependency, int defaultPort) where TBuilder : IHostApplicationBuilder
    {
        var host = builder.Configuration[$"Dependencies:{dependency}:Host"] ?? "localhost";

        var configuredPort = builder.Configuration[$"Dependencies:{dependency}:Port"];
        var port = string.IsNullOrWhiteSpace(configuredPort) ? defaultPort : int.Parse(configuredPort);
        builder.Services.AddHealthChecks().Add(new HealthCheckRegistration(
            dependency.ToLowerInvariant(),
            new TcpDependencyHealthCheck(host, port),
            HealthStatus.Unhealthy,
            ["ready"],
            TimeSpan.FromSeconds(3)));

        return builder;
    }

    public static TBuilder AddDatabaseHealthCheck<TBuilder, TContext>(
        this TBuilder builder, string name)
        where TBuilder : IHostApplicationBuilder
        where TContext : DbContext
    {
        builder.Services.AddHealthChecks().AddCheck<DatabaseConnectivityHealthCheck<TContext>>(
            name, failureStatus: HealthStatus.Unhealthy, tags: ["ready"]);
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks(HealthEndpointPath);

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}

/// <summary>
/// Classification applied to secrets and credentials before they are passed to logging.
/// Registered redaction erases the corresponding value from every enabled logger.
/// </summary>
public static class TimesheetDataClassification
{
    public static DataClassification Sensitive { get; } = new("Aiyara.Timesheet", "Sensitive");
}
