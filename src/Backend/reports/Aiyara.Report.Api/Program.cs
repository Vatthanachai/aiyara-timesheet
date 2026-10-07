using Aiyara.Report.Databases;
using Aiyara.Report.Api;
using Aiyara.Report.Services;
using Aiyara.Timesheet.Contracts.Identity.V1;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTcpDependencyHealthCheck("Postgres", 5432);
builder.AddTcpDependencyHealthCheck("RabbitMQ", 5672);
builder.AddTcpDependencyHealthCheck("RustFs", 9000);
builder.Services.AddScoped<ReportingTenantScope>();
builder.Services.AddSingleton(_ => GrpcChannel.ForAddress(
    builder.Configuration["IdentityGrpc:Url"] ?? "http://localhost:8082"));
builder.Services.AddSingleton(provider => new IdentityValidationService.IdentityValidationServiceClient(
    provider.GetRequiredService<GrpcChannel>()));
builder.Services.AddSingleton(ReportObjectStorage.CreateClient(builder.Configuration));
builder.Services.AddSingleton<ReportObjectStorage>();
builder.Services.AddDbContext<ReportingDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("ReportingDb")
    ?? throw new InvalidOperationException("ConnectionStrings:ReportingDb is required.")));
builder.AddDatabaseHealthCheck<WebApplicationBuilder, ReportingDbContext>("reporting-db");

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();
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
        !Guid.TryParse(validation.SubjectId, out var userId))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    var scope = context.RequestServices.GetRequiredService<ReportingTenantScope>();
    scope.TenantId = tenantId;
    context.Items[ReportActor.ContextKey] = new ReportActor(tenantId, userId,
        validation.Roles.FirstOrDefault(role => role is "TenantAdmin" or "PlatformAdmin") ??
        validation.Roles.FirstOrDefault() ?? "", string.IsNullOrWhiteSpace(validation.TimeZoneId)
            ? "Asia/Bangkok" : validation.TimeZoneId);
    await next();
});

app.UseAuthorization();

app.MapControllers();
app.MapReportEndpoints();

app.Run();
