using Aiyara.Timesheet.Databases;
using Aiyara.Timesheet.Api;
using Aiyara.Timesheet.Contracts.Identity.V1;
using Grpc.Net.Client;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTcpDependencyHealthCheck("Postgres", 5432);
builder.AddTcpDependencyHealthCheck("Redis", 6379);
builder.AddTcpDependencyHealthCheck("RabbitMQ", 5672);
builder.Services.AddScoped<TimesheetTenantScope>();
builder.Services.AddSingleton(_ => GrpcChannel.ForAddress(
    builder.Configuration["IdentityGrpc:Url"] ?? "http://localhost:8082"));
builder.Services.AddSingleton(provider => new IdentityValidationService.IdentityValidationServiceClient(
    provider.GetRequiredService<GrpcChannel>()));
builder.Services.AddHostedService<TimesheetOutboxPublisher>();
builder.Services.AddDbContext<TimesheetDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("TimesheetDb")
    ?? throw new InvalidOperationException("ConnectionStrings:TimesheetDb is required.")));
builder.AddDatabaseHealthCheck<WebApplicationBuilder, TimesheetDbContext>("timesheet-db");

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<TimesheetDbContext>().Database.MigrateAsync();
}

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api/v1"))
    {
        await next();
        return;
    }
    var authorization = context.Request.Headers.Authorization.ToString();
    if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    ValidateAccessTokenResponse validation;
    try
    {
        validation = await context.RequestServices
            .GetRequiredService<IdentityValidationService.IdentityValidationServiceClient>()
            .ValidateAccessTokenAsync(new ValidateAccessTokenRequest
            {
                AccessToken = authorization[7..].Trim(), CorrelationId = context.TraceIdentifier
            }, cancellationToken: context.RequestAborted);
    }
    catch (RpcException)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return;
    }
    if (!validation.IsValid || !Guid.TryParse(validation.TenantId, out var tenantId) ||
        !Guid.TryParse(validation.SubjectId, out var userId) ||
        string.IsNullOrWhiteSpace(validation.TimeZoneId))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    try
    {
        TimeZoneInfo.FindSystemTimeZoneById(validation.TimeZoneId);
    }
    catch (TimeZoneNotFoundException)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    var actor = new Actor(tenantId, userId, validation.Roles.FirstOrDefault() ?? "",
        validation.TimeZoneId);
    context.Items[Actor.ContextKey] = actor;
    context.RequestServices.GetRequiredService<TimesheetTenantScope>().TenantId = tenantId;
    await next();
});
app.UseAuthorization();

app.MapControllers();
app.MapTimesheetEndpoints();

app.Run();
