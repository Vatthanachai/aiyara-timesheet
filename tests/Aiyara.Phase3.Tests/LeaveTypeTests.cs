using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aiyara.Timesheet.Api;
using Aiyara.Timesheet.Api.Controllers;
using Aiyara.Timesheet.Databases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aiyara.Phase3.Tests;

/// <summary>Organization leave types, part-day leave, and yearly balances. See the Leave Types Plan.</summary>
public sealed class LeaveTypeTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly Guid tenantA = Guid.NewGuid();
    private readonly Guid tenantB = Guid.NewGuid();
    private readonly Guid admin = Guid.NewGuid();
    private readonly Guid alice = Guid.NewGuid();
    private readonly DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok")).DateTime);
    private WebApplication app = null!;
    private HttpClient client = null!;

    [Fact]
    public async Task Admins_define_leave_types_with_yearly_days()
    {
        var annual = await Result(await Send(HttpMethod.Post, "/api/v1/leave-types", admin, new { name = " Annual ", annualDays = 10.5 }, "TenantAdmin"));
        Assert.Equal("Annual", annual.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/v1/leave-types", admin, new { name = "Sick", annualDays = (decimal?)null }, "TenantAdmin")).StatusCode);

        Assert.Equal("LEAVE_TYPE_EXISTS", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/leave-types", admin, new { name = "annual", annualDays = 5 }, "TenantAdmin")));
        foreach (var days in new[] { 0.3m, -1m, 367m })
            Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/leave-types", admin, new { name = "Odd", annualDays = days }, "TenantAdmin")));
        Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/leave-types", admin, new { name = new string('x', 61), annualDays = 1 }, "TenantAdmin")));
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/v1/leave-types", alice, new { name = "Mine", annualDays = 99 })).StatusCode);

        var catalog = await Result(await Send(HttpMethod.Get, "/api/v1/catalog", alice));
        Assert.Equal(["Annual", "Sick"], catalog.GetProperty("leaveTypes").EnumerateArray().Select(x => x.GetProperty("name").GetString()));
        Assert.Empty((await Result(await Send(HttpMethod.Get, "/api/v1/catalog", alice, tenant: tenantB))).GetProperty("leaveTypes").EnumerateArray());

        var id = annual.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, $"/api/v1/leave-types/{id}", admin, new { name = "Vacation", annualDays = 12 }, "TenantAdmin")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put, $"/api/v1/leave-types/{id}", admin, new { name = "Vacation", annualDays = 12 }, "TenantAdmin", tenantB)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/v1/leave-types/{id}", admin, null, "TenantAdmin")).StatusCode);
        catalog = await Result(await Send(HttpMethod.Get, "/api/v1/catalog", alice));
        Assert.Equal(["Sick"], catalog.GetProperty("leaveTypes").EnumerateArray().Select(x => x.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Leave_names_a_type_and_balances_count_part_days()
    {
        var annual = (await Result(await Send(HttpMethod.Post, "/api/v1/leave-types", admin, new { name = "Annual", annualDays = 10 }, "TenantAdmin"))).GetProperty("id").GetGuid();
        var sick = (await Result(await Send(HttpMethod.Post, "/api/v1/leave-types", admin, new { name = "Sick", annualDays = (decimal?)null }, "TenantAdmin"))).GetProperty("id").GetGuid();

        // A normal day is 09:00–17:00, 480 minutes, without a work week.
        var full = await Result(await Send(HttpMethod.Post, "/api/v1/leave", alice, new { date = today, kind = "ignored", leaveTypeId = annual }));
        Assert.Equal("Annual", full.GetProperty("kind").GetString());
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/v1/leave", alice, new { date = today, leaveTypeId = annual, minutes = 240 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/v1/leave", alice, new { date = today, leaveTypeId = sick, minutes = 120 })).StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/leave", alice, new { date = today, kind = "annual" })));
        Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/leave", alice, new { date = today, leaveTypeId = Guid.NewGuid() })));
        Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/leave", alice, new { date = today, leaveTypeId = annual, minutes = 481 })));
        Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/leave", alice, new { date = today, leaveTypeId = annual, minutes = 0 })));
        await using (var scope = Scope(tenantA))
        {
            // Leave last year does not count against this year.
            var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
            db.LeaveEntries.Add(new LeaveEntry { TenantId = tenantA, OwnerId = alice, Date = today.AddYears(-1), Kind = "Annual", LeaveTypeId = annual });
            await db.SaveChangesAsync();
        }

        var balances = (await Result(await Send(HttpMethod.Get, $"/api/v1/me/leave-balance?year={today.Year}", alice)))
            .GetProperty("balances").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
        Assert.Equal((10m, 1.5m, 8.5m), Balance(balances["Annual"]));
        Assert.Equal(0.25m, balances["Sick"].GetProperty("usedDays").GetDecimal());
        Assert.Equal(JsonValueKind.Null, balances["Sick"].GetProperty("remainingDays").ValueKind);

        var member = await Result(await Send(HttpMethod.Get, $"/api/v1/members/{alice}/leave-balance?year={today.Year}", admin, null, "TenantAdmin"));
        Assert.Equal(2, member.GetProperty("balances").GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, $"/api/v1/members/{alice}/leave-balance?year={today.Year}", alice)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, "/api/v1/me/leave-balance?year=1999", alice)).StatusCode);

        var summary = await Result(await Send(HttpMethod.Get, $"/api/v1/members/summary?year={today.Year}&month={today.Month}", admin, null, "TenantAdmin"));
        Assert.Equal(1.75m, Assert.Single(summary.GetProperty("members").EnumerateArray()).GetProperty("leaveDays").GetDecimal());

        // Renaming the type keeps the name the leave was taken under.
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, $"/api/v1/leave-types/{annual}", admin, new { name = "Vacation", annualDays = 10 }, "TenantAdmin")).StatusCode);
        var leaves = await Result(await Send(HttpMethod.Get, $"/api/v1/leave?year={today.Year}&month={today.Month}", alice));
        Assert.Equal(2, leaves.EnumerateArray().Count(x => x.GetProperty("kind").GetString() == "Annual"));
    }

    [Fact]
    public async Task Without_leave_types_leave_keeps_its_free_kind()
    {
        var leave = await Result(await Send(HttpMethod.Post, "/api/v1/leave", alice, new { date = today, kind = "annual", minutes = 60 }));
        Assert.Equal("annual", leave.GetProperty("kind").GetString());
        Assert.Equal(60, leave.GetProperty("minutes").GetInt32());
        Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/leave", alice, new { date = today, kind = "annual", leaveTypeId = Guid.NewGuid() })));
        Assert.Empty((await Result(await Send(HttpMethod.Get, $"/api/v1/me/leave-balance?year={today.Year}", alice))).GetProperty("balances").EnumerateArray());
    }

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<TimesheetTenantScope>();
        builder.Services.AddScoped<TimesheetOperations>();
        builder.Services.AddControllers().AddApplicationPart(typeof(TimesheetController).Assembly);
        builder.Services.AddDbContext<TimesheetDbContext>(options => options.UseSqlite(connection));
        app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (Guid.TryParse(context.Request.Headers["X-Test-Tenant"], out var tenant) &&
                Guid.TryParse(context.Request.Headers["X-Test-User"], out var user))
            {
                context.RequestServices.GetRequiredService<TimesheetTenantScope>().TenantId = tenant;
                context.Items[Actor.ContextKey] = new Actor(tenant, user,
                    context.Request.Headers["X-Test-Role"].ToString(), "Asia/Bangkok");
            }
            await next();
        });
        app.MapControllers();
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TimesheetDbContext>().Database.EnsureCreatedAsync();
        await app.StartAsync();
        client = app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
        await connection.DisposeAsync();
    }

    private AsyncServiceScope Scope(Guid tenant)
    {
        var scope = app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TimesheetTenantScope>().TenantId = tenant;
        return scope;
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, Guid user, object? body = null,
        string role = "Employee", Guid? tenant = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Tenant", (tenant ?? tenantA).ToString());
        request.Headers.Add("X-Test-User", user.ToString());
        request.Headers.Add("X-Test-Role", role);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static (decimal? Annual, decimal Used, decimal? Remaining) Balance(JsonElement row) =>
        (row.GetProperty("annualDays").GetDecimal(), row.GetProperty("usedDays").GetDecimal(),
            row.GetProperty("remainingDays").GetDecimal());

    private static async Task<JsonElement> Result(HttpResponseMessage response)
    {
        using (response)
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").Clone();
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using (response)
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString();
    }
}
