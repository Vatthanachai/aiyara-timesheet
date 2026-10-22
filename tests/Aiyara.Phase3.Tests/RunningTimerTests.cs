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

/// <summary>The running timer. See the Running Timer Plan.</summary>
public sealed class RunningTimerTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly Guid tenantA = Guid.NewGuid();
    private readonly Guid tenantB = Guid.NewGuid();
    private readonly Guid alice = Guid.NewGuid();
    private readonly Guid bob = Guid.NewGuid();
    private readonly DateOnly first;
    private readonly TestClock clock = new();
    private WebApplication app = null!;
    private HttpClient client = null!;

    public RunningTimerTests()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok")).DateTime);
        first = new DateOnly(today.Year, today.Month, 1);
    }

    /// <summary>Day <paramref name="day"/> of the current month.</summary>
    private DateOnly Day(int day) => first.AddDays(day - 1);

    /// <summary>A local (Asia/Bangkok, UTC+7) time on <paramref name="day"/>.</summary>
    private static DateTimeOffset At(DateOnly day, int hour, int minute = 0) =>
        new(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(7));

    private Task<HttpResponseMessage> Start(Guid user, object? body = null, Guid? tenant = null) =>
        Send(HttpMethod.Post, "/api/v1/timer/start", user, body ?? new { }, tenant: tenant);

    private Task<HttpResponseMessage> Stop(Guid user, object? body = null) =>
        Send(HttpMethod.Post, "/api/v1/timer/stop", user, body ?? new { });

    private async Task<JsonElement> Timer(Guid user, Guid? tenant = null) =>
        (await Result(await Send(HttpMethod.Get, "/api/v1/timer", user, tenant: tenant))).GetProperty("timer");

    [Fact]
    public async Task Stop_creates_the_entry_with_the_break_left_out()
    {
        Assert.Equal(JsonValueKind.Null, (await Timer(alice)).ValueKind);
        clock.Now = At(Day(3), 9);
        var started = await Result(await Start(alice, new { taskName = " Support ", includesBreak = false }));
        Assert.Equal("Support", started.GetProperty("timer").GetProperty("taskName").GetString());
        // The timer is on the server, so another device sees it.
        var seen = await Timer(alice);
        Assert.Equal(started.GetProperty("timer").GetProperty("id").GetGuid(), seen.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, (await Timer(bob)).ValueKind);

        clock.Now = At(Day(3), 17, 30);
        using var stopped = await Stop(alice);
        Assert.Equal(HttpStatusCode.Created, stopped.StatusCode);
        var entry = (await stopped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        Assert.Equal(Day(3).ToString("yyyy-MM-dd"), entry.GetProperty("date").GetString());
        Assert.Equal("09:00:00", entry.GetProperty("startTime").GetString());
        Assert.Equal("17:30:00", entry.GetProperty("endTime").GetString());
        // 09:00-17:30 less the 12:00-13:00 break.
        Assert.Equal(450, entry.GetProperty("durationMinutes").GetInt32());
        Assert.Equal(JsonValueKind.Null, (await Timer(alice)).ValueKind);
        Assert.Equal(1, (await Result(await Send(HttpMethod.Get, $"/api/v1/entries?year={first.Year}&month={first.Month}", alice))).GetArrayLength());
        await using (var scope = Scope(tenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
            Assert.Equal(1, await db.OutboxEvents.CountAsync());
            Assert.Equal(1, await db.Audits.CountAsync(x => x.EntityType == "entry" && x.Action == "created"));
            Assert.Equal(0, await db.RunningTimers.CountAsync());
        }

        // Working through the break counts it, and the stop request can fill in what start left out.
        clock.Now = At(Day(4), 9);
        Assert.Equal(HttpStatusCode.Created, (await Start(alice)).StatusCode);
        clock.Now = At(Day(4), 17);
        var worked = await Result(await Stop(alice, new { taskName = "Review", includesBreak = true }));
        Assert.Equal(480, worked.GetProperty("durationMinutes").GetInt32());
        Assert.Equal("Review", worked.GetProperty("taskName").GetString());
    }

    [Fact]
    public async Task One_timer_per_member_and_tenant()
    {
        Assert.Equal(HttpStatusCode.Created, (await Start(alice)).StatusCode);
        Assert.Equal("TIMER_RUNNING", await ErrorCode(await Start(alice)));
        // Another member, and the same user id in another tenant, each have their own.
        Assert.Equal(HttpStatusCode.Created, (await Start(bob)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Start(alice, tenant: tenantB)).StatusCode);

        // The unique index is what decides a race between two starts.
        await using (var scope = Scope(tenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
            db.RunningTimers.Add(new RunningTimer { TenantId = tenantA, OwnerId = alice, StartedAtUtc = DateTime.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Start(bob, new { taskName = new string('x', 201) })));
        Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Start(alice, new { projectId = Guid.NewGuid() }, tenantB)));

        // A timer is private to its member and tenant: discarding acts on your own only.
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, "/api/v1/timer", alice, tenant: tenantB)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Delete, "/api/v1/timer", alice, tenant: tenantB)).StatusCode);
        Assert.Equal(JsonValueKind.Object, (await Timer(alice)).ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, "/api/v1/timer", alice)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Timer(alice)).ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await Stop(alice)).StatusCode);
        Assert.Equal(JsonValueKind.Object, (await Timer(bob)).ValueKind);
        // Discarding makes no entry, and the next start works.
        Assert.Equal(0, (await Result(await Send(HttpMethod.Get, $"/api/v1/entries?year={first.Year}&month={first.Month}", alice))).GetArrayLength());
        Assert.Equal(HttpStatusCode.Created, (await Start(alice)).StatusCode);
    }

    [Fact]
    public async Task A_timer_across_midnight_is_an_overnight_entry_on_its_start_day()
    {
        clock.Now = At(Day(3), 22);
        await Start(alice, new { taskName = "Deploy" });
        clock.Now = At(Day(4), 2);
        var entry = await Result(await Stop(alice));
        Assert.Equal(Day(3).ToString("yyyy-MM-dd"), entry.GetProperty("date").GetString());
        Assert.Equal("22:00:00", entry.GetProperty("startTime").GetString());
        Assert.Equal("02:00:00", entry.GetProperty("endTime").GetString());
        Assert.Equal(240, entry.GetProperty("durationMinutes").GetInt32());
    }

    [Fact]
    public async Task A_refused_stop_keeps_the_timer()
    {
        // A timer that began last month: the month is closed, the day is named, nothing is lost.
        var lastMonth = first.AddDays(-1);
        clock.Now = At(lastMonth, 20);
        await Start(alice, new { taskName = "Late" });
        clock.Now = At(first, 1);
        using (var refused = await Stop(alice))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var error = await refused.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("MONTH_CLOSED", error.GetProperty("errorCode").GetString());
            Assert.Equal([lastMonth.ToString("yyyy-MM-dd")],
                error.GetProperty("errors").GetProperty("dates").EnumerateArray().Select(x => x.GetString()));
        }
        Assert.Equal(JsonValueKind.Object, (await Timer(alice)).ValueKind);
        // It can be placed on an open day instead.
        var placed = await Result(await Stop(alice, new { date = first, startTime = "09:00:00", endTime = "10:00:00" }));
        Assert.Equal(first.ToString("yyyy-MM-dd"), placed.GetProperty("date").GetString());
        Assert.Equal(60, placed.GetProperty("durationMinutes").GetInt32());

        // Entry checks: no task name, a stretch inside the break, and under a minute.
        clock.Now = At(Day(3), 9);
        await Start(alice);
        Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Stop(alice)));
        Assert.Equal("ENTRY_IN_BREAK", await ErrorCode(await Stop(alice, new { taskName = "Lunch", startTime = "12:10:00", endTime = "12:50:00" })));
        clock.Now = At(Day(3), 9).AddSeconds(30);
        Assert.Equal("INVALID_TIME_RANGE", await ErrorCode(await Stop(alice, new { taskName = "Blink" })));
        Assert.Equal(JsonValueKind.Object, (await Timer(alice)).ValueKind);

        // Past 24 hours the times must be given.
        clock.Now = At(Day(4), 10);
        Assert.Equal("TIMER_TOO_LONG", await ErrorCode(await Stop(alice, new { taskName = "Forgot" })));
        Assert.Equal(JsonValueKind.Object, (await Timer(alice)).ValueKind);
        var fixedUp = await Result(await Stop(alice, new { taskName = "Forgot", startTime = "09:00:00", endTime = "11:00:00" }));
        Assert.Equal(120, fixedUp.GetProperty("durationMinutes").GetInt32());

        // A submitted month refuses the stop as well.
        clock.Now = At(Day(5), 9);
        await Start(alice, new { taskName = "Once more" });
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/v1/me/months/{first.Year}/{first.Month}/submit", alice)).StatusCode);
        clock.Now = At(Day(5), 10);
        Assert.Equal("MONTH_CLOSED", await ErrorCode(await Stop(alice)));
        Assert.Equal(JsonValueKind.Object, (await Timer(alice)).ValueKind);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(clock);
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
        // Every day is a working day with a 12:00–13:00 break, so the tests do not depend on today's weekday.
        await using (var scope = Scope(tenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
            db.WorkWeeks.Add(new WorkWeek { TenantId = tenantA, WorkingDaysMask = 0b1111111,
                BreakStart = new TimeOnly(12, 0), BreakEnd = new TimeOnly(13, 0) });
            await db.SaveChangesAsync();
        }
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
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
        return await client.SendAsync(request);
    }

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
