using Aiyara.Report.Databases;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTcpDependencyHealthCheck("Postgres", 5432);
builder.Services.AddScoped<ReportingTenantScope>();
builder.Services.AddDbContext<ReportingDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("ReportingDb")
    ?? throw new InvalidOperationException("ConnectionStrings:ReportingDb is required.")));
builder.AddDatabaseHealthCheck<WebApplicationBuilder, ReportingDbContext>("reporting-db");

builder.Services.AddControllers();
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

app.UseAuthorization();

app.MapControllers();

app.Run();
