using Aiyara.Notifications.Databases;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTcpDependencyHealthCheck("RabbitMq", 5672);
builder.AddTcpDependencyHealthCheck("Postgres", 5432);
builder.Services.AddDbContext<NotificationDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("NotificationDb")
    ?? throw new InvalidOperationException("ConnectionStrings:NotificationDb is required.")));
builder.AddDatabaseHealthCheck<WebApplicationBuilder, NotificationDbContext>("notification-db");
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostedService<NotificationDispatchWorker>();
builder.Services.AddScoped<CredentialEmailDispatcher>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
}

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new { service = "notification", status = "ready" }));
app.MapPost("/internal/v1/credential-email", async (
    CredentialEmailRequest request, HttpContext context,
    CredentialEmailDispatcher dispatcher, IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!CredentialEmailDispatcher.ValidInternalKey(configuration["InternalApi:Key"],
        context.Request.Headers["X-Internal-Key"].ToString())) return Results.Unauthorized();
    return await dispatcher.SendAsync(request, cancellationToken)
        ? Results.Accepted() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
})
.ExcludeFromDescription();

app.Run();

internal sealed class NotificationDispatchWorker(ILogger<NotificationDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Notification dispatcher is ready for message-queue integration.");
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
