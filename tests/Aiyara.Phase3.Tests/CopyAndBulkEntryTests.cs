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

/// <summary>Bulk entry, copying a day or a week, and entry templates. See the Copy And Bulk Entry Plan.</summary>
public sealed class CopyAndBulkEntryTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly Guid tenantA = Guid.NewGuid();
    private readonly Guid tenantB = Guid.NewGuid();
    private readonly Guid alice = Guid.NewGuid();
    private readonly Guid bob = Guid.NewGuid();
    private readonly DateOnly first;
    private WebApplication app = null!;
    private HttpClient client = null!;

    public CopyAndBulkEntryTests()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok")).DateTime);
        first = new DateOnly(today.Year, today.Month, 1);
    }

    /// <summary>Day <paramref name="day"/> of the current month.</summary>
    private DateOnly Day(int day) => first.AddDays(day - 1);

    [Fact]
    public async Task Bulk_writes_the_entry_on_every_day_or_none()
    {
        var created = await Result(await Send(HttpMethod.Post, "/api/v1/entries/bulk", alice,
            Bulk([Day(4), Day(3)], "09:00", "17:00")));
        var entries = created.GetProperty("created").EnumerateArray().ToArray();
        Assert.Equal([Day(3), Day(4)], entries.Select(x => DateOnly.Parse(x.GetProperty("date").GetString()!)));
        // 09:00–17:00 less the 12:00–13:00 break.
        Assert.All(entries, x => Assert.Equal(420, x.GetProperty("durationMinutes").GetInt32()));
        Assert.Equal("Support", entries[0].GetProperty("taskName").GetString());
        await using (var scope = Scope(tenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
            Assert.Equal(1, await db.OutboxEvents.CountAsync());
            Assert.Equal(2, await db.Audits.CountAsync(x => x.EntityType == "entry" && x.Action == "created"));
        }

        foreach (var dates in new[] { Array.Empty<DateOnly>(), [Day(5), Day(5)],
                     Enumerable.Range(0, 32).Select(x => Day(1).AddDays(-x)).ToArray() })
            Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/entries/bulk", alice,
                Bulk(dates, "09:00", "10:00"))));

        // One day in last month refuses the whole request and is named.
        var lastMonth = first.AddDays(-1);
        using (var refused = await Send(HttpMethod.Post, "/api/v1/entries/bulk", alice, Bulk([Day(5), lastMonth], "09:00", "10:00")))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var error = await refused.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("MONTH_CLOSED", error.GetProperty("errorCode").GetString());
            Assert.Equal([lastMonth.ToString("yyyy-MM-dd")],
                error.GetProperty("errors").GetProperty("dates").EnumerateArray().Select(x => x.GetString()));
        }

        Assert.Equal("ENTRY_IN_BREAK", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/entries/bulk", alice, Bulk([Day(5)], "12:00", "13:00"))));
        Assert.Equal("INVALID_TIME_RANGE", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/entries/bulk", alice, Bulk([Day(5)], "09:00", "09:00"))));
        Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/entries/bulk", alice,
            Bulk([Day(5)], "09:00", "10:00", projectId: Guid.NewGuid()))));
        var bobsTask = (await Result(await Send(HttpMethod.Post, "/api/v1/tasks", bob,
            new { name = "Bob's", status = "todo", projectId = (Guid?)null, categoryId = (Guid?)null }))).GetProperty("id").GetGuid();
        Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/entries/bulk", alice,
            Bulk([Day(5)], "09:00", "10:00", personalTaskId: bobsTask))));
        Assert.Equal(2, (await Result(await Send(HttpMethod.Get, $"/api/v1/entries?year={first.Year}&month={first.Month}", alice))).GetArrayLength());

        // A submitted month is closed to bulk entry too; another tenant sees none of it.
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/v1/me/months/{first.Year}/{first.Month}/submit", alice)).StatusCode);
        Assert.Equal("MONTH_CLOSED", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/entries/bulk", alice, Bulk([Day(5)], "09:00", "10:00"))));
        Assert.Equal(0, (await Result(await Send(HttpMethod.Get, $"/api/v1/entries?year={first.Year}&month={first.Month}", alice, tenant: tenantB))).GetArrayLength());
    }

    [Fact]
    public async Task Copying_a_day_counts_the_break_again_and_drops_deleted_references()
    {
        var lastMonth = first.AddDays(-10);
        var project = (await Result(await Send(HttpMethod.Post, "/api/v1/projects", alice, new { name = "Gone" }, "TenantAdmin"))).GetProperty("id").GetGuid();
        await using (var scope = Scope(tenantA))
        {
            // Written before the organization had a break: 3 hours then, 2 now.
            var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
            db.TimeEntries.AddRange(
                Entry(lastMonth, "11:00", "14:00", 180, "Workshop", project),
                Entry(lastMonth, "12:00", "13:00", 60, "Lunch call"));
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/v1/projects/{project}", alice, null, "TenantAdmin")).StatusCode);

        var copy = await Result(await Send(HttpMethod.Post, "/api/v1/entries/copy", alice, new { from = lastMonth, to = Day(5), days = 1 }));
        var created = Assert.Single(copy.GetProperty("created").EnumerateArray());
        Assert.Equal(Day(5).ToString("yyyy-MM-dd"), created.GetProperty("date").GetString());
        Assert.Equal(120, created.GetProperty("durationMinutes").GetInt32());
        Assert.Equal("Workshop", created.GetProperty("taskName").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("projectId").ValueKind);
        Assert.Empty(copy.GetProperty("skipped").EnumerateArray());
        await using (var scope = Scope(tenantA))
            Assert.Equal($"copied from {lastMonth:yyyy-MM-dd}", (await scope.ServiceProvider.GetRequiredService<TimesheetDbContext>()
                .Audits.SingleAsync(x => x.EntityType == "entry")).Reason);

        // Copying again leaves the day alone instead of doubling it.
        var again = await Result(await Send(HttpMethod.Post, "/api/v1/entries/copy", alice, new { from = lastMonth, to = Day(5), days = 1 }));
        Assert.Empty(again.GetProperty("created").EnumerateArray());
        Assert.Equal("has-entries", Assert.Single(again.GetProperty("skipped").EnumerateArray()).GetProperty("reason").GetString());

        // Another member copying the same day finds nothing of Alice's.
        var bobs = await Result(await Send(HttpMethod.Post, "/api/v1/entries/copy", bob, new { from = lastMonth, to = Day(6), days = 1 }));
        Assert.Empty(bobs.GetProperty("created").EnumerateArray());
        Assert.Empty(bobs.GetProperty("skipped").EnumerateArray());
    }

    [Fact]
    public async Task Copying_a_week_skips_days_that_should_stay_empty()
    {
        // Sources on days 1–5; their targets are days 8–12.
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, "/api/v1/entries/bulk", alice,
            Bulk([Day(1), Day(2), Day(3), Day(4), Day(5), Day(12)], "09:00", "12:00"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/v1/holidays", alice, new { date = Day(8), name = "Holiday" }, "TenantAdmin")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/v1/leave", alice, new { date = Day(9), kind = "annual" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/v1/leave", alice, new { date = Day(10), kind = "annual", minutes = 60 })).StatusCode);
        await using (var scope = Scope(tenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
            (await db.WorkWeeks.SingleAsync()).WorkingDaysMask = 0b1111111 & ~(1 << (int)Day(11).DayOfWeek);
            await db.SaveChangesAsync();
        }

        var copy = await Result(await Send(HttpMethod.Post, "/api/v1/entries/copy", alice, new { from = Day(1), to = Day(8), days = 7 }));
        Assert.Equal([Day(10).ToString("yyyy-MM-dd")], copy.GetProperty("created").EnumerateArray().Select(x => x.GetProperty("date").GetString()));
        Assert.Equal([(Day(8), "holiday"), (Day(9), "leave"), (Day(11), "day-off"), (Day(12), "has-entries")],
            copy.GetProperty("skipped").EnumerateArray().Select(x =>
                (DateOnly.Parse(x.GetProperty("date").GetString()!), x.GetProperty("reason").GetString())));

        foreach (var body in new object[] { new { from = Day(1), to = Day(4), days = 7 }, new { from = Day(1), to = Day(1), days = 1 },
                     new { from = Day(1), to = Day(15), days = 0 }, new { from = Day(1), to = Day(15), days = 8 } })
            Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/entries/copy", alice, body)));

        // A week reaching into last month is refused whole and names those days.
        using var refused = await Send(HttpMethod.Post, "/api/v1/entries/copy", alice, new { from = Day(15), to = first.AddDays(-3), days = 7 });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var error = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("MONTH_CLOSED", error.GetProperty("errorCode").GetString());
        Assert.Equal(3, error.GetProperty("errors").GetProperty("dates").GetArrayLength());
        Assert.Equal(7, (await Result(await Send(HttpMethod.Get, $"/api/v1/entries?year={first.Year}&month={first.Month}", alice))).GetArrayLength());
    }

    [Fact]
    public async Task Templates_belong_to_their_member()
    {
        var project = (await Result(await Send(HttpMethod.Post, "/api/v1/projects", alice, new { name = "Client" }, "TenantAdmin"))).GetProperty("id").GetGuid();
        var standup = await Result(await Send(HttpMethod.Post, "/api/v1/entry-templates", alice,
            Template(" Standup ", "Daily standup", "09:00", "09:15", project)));
        Assert.Equal("Standup", standup.GetProperty("name").GetString());
        Assert.Equal("09:15:00", standup.GetProperty("endTime").GetString());
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/v1/entry-templates", alice, Template("Review", "Code review", "16:00", "17:00"))).StatusCode);

        Assert.Equal("TEMPLATE_EXISTS", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/entry-templates", alice, Template("standup", "Other", "10:00", "11:00"))));
        Assert.Equal("INVALID_TIME_RANGE", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/entry-templates", alice, Template("Empty", "Nothing", "10:00", "10:00"))));
        foreach (var body in new[] { Template("", "Task", "10:00", "11:00"), Template(new string('x', 101), "Task", "10:00", "11:00"),
                     Template("No task", " ", "10:00", "11:00"), Template("Unknown", "Task", "10:00", "11:00", Guid.NewGuid()) })
            Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/entry-templates", alice, body)));
        // Another member may use the same name; the break is not checked until the template is used.
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/v1/entry-templates", bob, Template("Standup", "Lunch", "12:00", "13:00"))).StatusCode);

        var id = standup.GetProperty("id").GetGuid();
        var updated = await Result(await Send(HttpMethod.Put, $"/api/v1/entry-templates/{id}", alice,
            Template("standup", "Daily standup", "09:00", "09:30", project) with { includesBreak = true }));
        Assert.Equal("standup", updated.GetProperty("name").GetString());
        Assert.True(updated.GetProperty("includesBreak").GetBoolean());
        Assert.Equal("TEMPLATE_EXISTS", await ErrorCode(await Send(HttpMethod.Put, $"/api/v1/entry-templates/{id}", alice, Template("Review", "x", "09:00", "10:00"))));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put, $"/api/v1/entry-templates/{id}", bob, Template("Mine", "x", "09:00", "10:00"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Delete, $"/api/v1/entry-templates/{id}", bob)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put, $"/api/v1/entry-templates/{id}", alice, Template("Mine", "x", "09:00", "10:00"), tenant: tenantB)).StatusCode);
        Assert.Empty((await Result(await Send(HttpMethod.Get, "/api/v1/entry-templates", alice, tenant: tenantB))).EnumerateArray());

        // A project deleted since is left out of the list.
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/v1/projects/{project}", alice, null, "TenantAdmin")).StatusCode);
        var list = (await Result(await Send(HttpMethod.Get, "/api/v1/entry-templates", alice))).EnumerateArray().ToArray();
        Assert.Equal(["Review", "standup"], list.Select(x => x.GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Null, list[1].GetProperty("projectId").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/v1/entry-templates/{id}", alice)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Delete, $"/api/v1/entry-templates/{id}", alice)).StatusCode);
        Assert.Equal(["Review"], (await Result(await Send(HttpMethod.Get, "/api/v1/entry-templates", alice))).EnumerateArray().Select(x => x.GetProperty("name").GetString()));
        Assert.Single((await Result(await Send(HttpMethod.Get, "/api/v1/entry-templates", bob))).EnumerateArray());
        await using var scope = Scope(tenantA);
        Assert.Equal(["created", "created", "created", "updated", "deleted"], await scope.ServiceProvider.GetRequiredService<TimesheetDbContext>()
            .Audits.Where(x => x.EntityType == "entry_template").OrderBy(x => x.OccurredAtUtc).Select(x => x.Action).ToListAsync());
    }

    private static object Bulk(DateOnly[] dates, string start, string end, Guid? projectId = null, Guid? personalTaskId = null) => new
    {
        dates, startTime = $"{start}:00", endTime = $"{end}:00", taskName = " Support ", detail = (string?)null,
        notes = (string?)null, projectId, categoryId = (Guid?)null, personalTaskId, reason = (string?)null, includesBreak = false
    };

    private static TemplateBody Template(string name, string taskName, string start, string end, Guid? projectId = null) =>
        new(name, taskName, $"{start}:00", $"{end}:00", projectId, null, "Detail", null, false);

    private sealed record TemplateBody(string name, string taskName, string startTime, string endTime, Guid? projectId,
        Guid? categoryId, string? detail, string? notes, bool includesBreak);

    private TimeEntry Entry(DateOnly date, string start, string end, int minutes, string task, Guid? projectId = null) => new()
    {
        TenantId = tenantA, OwnerId = alice, Date = date, StartTime = TimeOnly.Parse(start), EndTime = TimeOnly.Parse(end),
        DurationMinutes = minutes, TaskName = task, ProjectId = projectId, UpdatedAtUtc = DateTime.UtcNow
    };

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
