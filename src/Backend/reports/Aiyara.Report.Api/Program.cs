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
builder.Services.AddScoped<IReportAccessTokenValidator, GrpcReportAccessTokenValidator>();
builder.Services.AddSingleton(ReportObjectStorage.CreateClient(builder.Configuration));
builder.Services.AddSingleton<ReportObjectStorage>();
builder.Services.AddSingleton<IReportObjectStorage>(provider => provider.GetRequiredService<ReportObjectStorage>());
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

app.UseReportAuthentication();

app.UseAuthorization();

app.MapControllers();
app.MapReportEndpoints();

app.Run();
