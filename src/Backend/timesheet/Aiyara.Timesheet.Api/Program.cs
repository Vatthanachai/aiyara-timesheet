using Aiyara.Timesheet.Databases;
using Aiyara.Timesheet.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTcpDependencyHealthCheck("Postgres", 5432);
builder.AddTcpDependencyHealthCheck("Redis", 6379);
builder.Services.AddScoped<TimesheetTenantScope>();
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
    var scope = context.RequestServices.GetRequiredService<TimesheetTenantScope>();
    if (Guid.TryParse(context.Request.Headers["X-Tenant-Id"], out var tenantId))
        scope.TenantId = tenantId;
    await next();
});
app.UseAuthorization();

app.MapControllers();
app.MapTimesheetEndpoints();

app.Run();
