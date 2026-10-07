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
app.MapGet("/internal/v1/report-snapshots/{subjectId:guid}/{year:int}/{month:int}",
    async (Guid subjectId, int year, int month, HttpContext context, TimesheetDbContext db,
        TimesheetTenantScope tenantScope, IConfiguration configuration) =>
    {
        var configuredKey = configuration["Reporting:InternalKey"];
        var suppliedKey = context.Request.Headers["X-Internal-Service-Key"].ToString();
        if (string.IsNullOrEmpty(configuredKey) || string.IsNullOrEmpty(suppliedKey) ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(configuredKey)),
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(suppliedKey))))
            return Results.Unauthorized();
        if (!Guid.TryParse(context.Request.Headers["X-Tenant-Id"], out var tenantId) ||
            tenantId == Guid.Empty || year is < 2000 or > 2100 || month is < 1 or > 12)
            return Results.BadRequest();
        tenantScope.TenantId = tenantId;
        var snapshot = await db.MonthSnapshots.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OwnerId == subjectId && x.Year == year && x.Month == month);
        return snapshot is null ? Results.NotFound() :
            Results.Content(snapshot.PayloadJson, "application/json");
    }).WithName("GetInternalReportSnapshotV1");
app.MapGet("/internal/v1/report-snapshots/{year:int}/{month:int}",
    async (int year, int month, HttpContext context, TimesheetDbContext db,
        TimesheetTenantScope tenantScope, IConfiguration configuration) =>
    {
        var configuredKey = configuration["Reporting:InternalKey"];
        var suppliedKey = context.Request.Headers["X-Internal-Service-Key"].ToString();
        if (string.IsNullOrEmpty(configuredKey) || string.IsNullOrEmpty(suppliedKey) ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(configuredKey)),
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(suppliedKey))))
            return Results.Unauthorized();
        if (!Guid.TryParse(context.Request.Headers["X-Tenant-Id"], out var tenantId) ||
            tenantId == Guid.Empty || year is < 2000 or > 2100 || month is < 1 or > 12)
            return Results.BadRequest();
        tenantScope.TenantId = tenantId;
        var snapshots = await db.MonthSnapshots.AsNoTracking().Where(x =>
            x.Year == year && x.Month == month && x.OwnerId != Guid.Empty)
            .Select(x => new { x.OwnerId, x.PayloadJson, x.CreatedAtUtc }).ToListAsync();
        return Results.Ok(snapshots);
    }).WithName("ListInternalReportSnapshotsV1");

app.Run();
