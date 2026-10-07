using Aiyara.Report.Databases;
using Aiyara.Report.Services;
using Aiyara.Report.Worker;
using Aiyara.Timesheet.Contracts.Identity.V1;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Quartz;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddTcpDependencyHealthCheck("RabbitMq", 5672);
builder.AddTcpDependencyHealthCheck("RustFs", 9000);
builder.AddTcpDependencyHealthCheck("Postgres", 5432);
builder.Services.AddScoped<ReportingTenantScope>();
builder.Services.AddDbContext<ReportingDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("ReportingDb")
    ?? throw new InvalidOperationException("ConnectionStrings:ReportingDb is required.")));
builder.Services.AddSingleton(_ => GrpcChannel.ForAddress(
    builder.Configuration["IdentityGrpc:Url"] ?? "http://localhost:8082"));
builder.Services.AddSingleton(provider => new IdentityValidationService.IdentityValidationServiceClient(
    provider.GetRequiredService<GrpcChannel>()));
builder.Services.AddSingleton(ReportObjectStorage.CreateClient(builder.Configuration));
builder.Services.AddSingleton<ReportObjectStorage>();
builder.Services.AddSingleton<IReportObjectStorage>(provider => provider.GetRequiredService<ReportObjectStorage>());
builder.Services.AddHttpClient("timesheet", client =>
    client.BaseAddress = new Uri(builder.Configuration["Timesheet:BaseUrl"] ?? "http://localhost:5198"));
builder.Services.AddHttpClient("notifications", client =>
    client.BaseAddress = new Uri(builder.Configuration["Notifications:BaseUrl"] ?? "http://localhost:8080"));
builder.Services.AddHostedService<ReportGenerationConsumer>();
builder.Services.AddHostedService<TimesheetMonthEventConsumer>();
builder.Services.AddHostedService<ReportRunMetricsPublisher>();
builder.Services.AddHostedService<RabbitQueueMetricsPublisher>();
builder.Services.AddQuartz(options =>
{
    var key = new JobKey("report-schedule-dispatch");
    options.AddJob<ScheduleDispatchJob>(job => job.WithIdentity(key));
    options.AddTrigger(trigger => trigger.ForJob(key).WithIdentity("report-schedule-dispatch-minute")
        .StartNow().WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever()));
    var purgeKey = new JobKey("report-retention-purge");
    options.AddJob<RetentionPurgeJob>(job => job.WithIdentity(purgeKey));
    options.AddTrigger(trigger => trigger.ForJob(purgeKey).WithIdentity("report-retention-purge-daily")
        .WithCronSchedule("0 15 2 * * ?"));
});
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();
app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Ok(new { service = "report-worker", status = "ready" }));
app.Run();
