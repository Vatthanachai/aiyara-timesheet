using System.Net;
using System.Net.Http.Json;
using Aiyara.Timesheet.Api;
using Aiyara.Timesheet.Databases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aiyara.Phase3.Tests;

public sealed class TimesheetHttpContractTests
{
    [Fact]
    public async Task Entry_routes_enforce_owner_tenant_and_closed_month_contract()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<TimesheetTenantScope>();
        builder.Services.AddDbContext<TimesheetDbContext>(options => options.UseSqlite(connection));
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (Guid.TryParse(context.Request.Headers["X-Test-Tenant"], out var tenant) &&
                Guid.TryParse(context.Request.Headers["X-Test-User"], out var user))
            {
                context.RequestServices.GetRequiredService<TimesheetTenantScope>().TenantId = tenant;
                context.Items[Actor.ContextKey] = new Actor(tenant, user, "Employee", "Asia/Bangkok");
            }
            await next();
        });
        app.MapTimesheetEndpoints();
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TimesheetDbContext>().Database.EnsureCreatedAsync();
        await app.StartAsync();
        using var client = app.GetTestClient();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var month = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok"));
        var today = DateOnly.FromDateTime(month.DateTime);
        var body = new EntryRequest(today, new TimeOnly(22, 0), new TimeOnly(2, 0),
            "Overnight support", null, null, null, null, null, null);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/v1/entries?year={today.Year}&month={today.Month}")).StatusCode);
        SetActor(client, tenantA, userA);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/v1/projects", new NameRequest("Denied"))).StatusCode);
        var created = await client.PostAsJsonAsync("/api/v1/entries", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var entry = await created.Content.ReadFromJsonAsync<TimeEntry>();
        Assert.NotNull(entry);
        Assert.Equal(240, entry.DurationMinutes);
        Assert.Equal(tenantA, entry.TenantId);
        var own = await client.GetFromJsonAsync<List<TimeEntry>>(
            $"/api/v1/entries?year={today.Year}&month={today.Month}");
        Assert.Single(own!);

        SetActor(client, tenantA, userB);
        Assert.Empty((await client.GetFromJsonAsync<List<TimeEntry>>(
            $"/api/v1/entries?year={today.Year}&month={today.Month}"))!);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/v1/entries/{entry.Id}")).StatusCode);
        SetActor(client, tenantB, userA);
        Assert.Empty((await client.GetFromJsonAsync<List<TimeEntry>>(
            $"/api/v1/entries?year={today.Year}&month={today.Month}"))!);

        var previous = today.AddMonths(-1);
        var closed = await client.PostAsJsonAsync("/api/v1/entries", body with { Date = previous });
        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        await app.StopAsync();
    }

    private static void SetActor(HttpClient client, Guid tenant, Guid user)
    {
        client.DefaultRequestHeaders.Remove("X-Test-Tenant");
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant.ToString());
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
    }
}
