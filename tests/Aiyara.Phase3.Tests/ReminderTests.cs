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

/// <summary>Daily and month-end reminders about time not logged. See the Missing Entry Reminders Plan.
/// October 2026: the 18th is a Sunday, so the 30th (a Friday) is the last working day.</summary>
public sealed class ReminderTests : IAsyncLifetime
{
    private static readonly TimeZoneInfo Bangkok = TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly Guid tenantA = Guid.NewGuid();
    private readonly Guid tenantB = Guid.NewGuid();
    private readonly Guid admin = Guid.NewGuid();
    private readonly FakeDirectory directory = new();
    private readonly FakeSender sender = new();
    private WebApplication app = null!;
    private HttpClient client = null!;

    [Fact]
    public async Task Admins_turn_reminders_on_and_choose_the_time()
    {
        var settings = await Result(await Send(HttpMethod.Get, "/api/v1/reminders", admin, null, "TenantAdmin"));
        Assert.False(settings.GetProperty("dailyEnabled").GetBoolean());
        Assert.Equal("09:00", settings.GetProperty("sendAt").GetString());

        var saved = await Result(await Send(HttpMethod.Put, "/api/v1/reminders", admin,
            new { dailyEnabled = true, monthEndEnabled = false, sendAt = "08:30" }, "TenantAdmin"));
        Assert.True(saved.GetProperty("dailyEnabled").GetBoolean());
        Assert.Equal("08:30", saved.GetProperty("sendAt").GetString());
        Assert.Equal("08:30", (await Result(await Send(HttpMethod.Get, "/api/v1/reminders", admin, null, "TenantAdmin")))
            .GetProperty("sendAt").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "/api/v1/reminders", admin,
            new { dailyEnabled = true, monthEndEnabled = true, sendAt = "08:30:15" }, "TenantAdmin")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/v1/reminders", Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Put, "/api/v1/reminders", Guid.NewGuid(),
            new { dailyEnabled = false, monthEndEnabled = false, sendAt = "09:00" })).StatusCode);
        Assert.False((await Result(await Send(HttpMethod.Get, "/api/v1/reminders", admin, null, "TenantAdmin", tenantB)))
            .GetProperty("dailyEnabled").GetBoolean());
    }

    [Fact]
    public async Task The_daily_reminder_names_the_previous_working_day_once()
    {
        var (alice, bob, carol, dave, erin) = (Member("Alice"), Member("Bob"), Member("Carol"), Member("Dave", Day(20)), Member("Erin"));
        directory.Members[tenantA] = [alice, bob, carol, dave, erin];
        directory.Members[tenantB] = [Member("Frank")];
        await Settings(tenantA, daily: true, monthEnd: false);
        await using (var scope = Scope(tenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
            db.TimeEntries.Add(Entry(alice.Id, Day(19), 60));
            db.LeaveEntries.Add(new LeaveEntry { TenantId = tenantA, OwnerId = bob.Id, Date = Day(19), Kind = "Sick", Minutes = 120 });
            db.MonthApprovals.Add(new MonthApproval { TenantId = tenantA, OwnerId = erin.Id, Year = 2026, Month = 10,
                Status = MonthApproval.Submitted, SubmittedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await Run(Day(20), new TimeOnly(8, 59));
        Assert.Empty(sender.Sent);
        await Run(Day(20), new TimeOnly(9, 5));
        var email = Assert.Single(sender.Sent);
        Assert.Equal(("carol@example.test", "daily", "Monday 19 October 2026"), (email.Email, email.Kind, email.Period));

        // Later the same day, and again after the day's run is forgotten, nothing more is sent.
        await Run(Day(20), new TimeOnly(9, 6));
        await using (var scope = Scope(tenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
            (await db.ReminderSettings.SingleAsync()).DailyRanOn = null;
            await db.SaveChangesAsync();
        }
        await Run(Day(20), new TimeOnly(9, 7));
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task The_daily_reminder_skips_days_off_holidays_and_the_previous_month()
    {
        var carol = Member("Carol");
        directory.Members[tenantA] = [carol];
        await Settings(tenantA, daily: true, monthEnd: false);
        await using (var scope = Scope(tenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
            db.Holidays.Add(new Holiday { TenantId = tenantA, Date = Day(23), Name = "Chulalongkorn Day" });
            await db.SaveChangesAsync();
        }

        await Run(Day(24), new TimeOnly(10, 0)); // Saturday
        await Run(Day(23), new TimeOnly(10, 0)); // a holiday
        Assert.Empty(sender.Sent);
        // Monday after the holiday: the previous working day is Thursday.
        await Run(Day(26), new TimeOnly(10, 0));
        Assert.Equal("Thursday 22 October 2026", Assert.Single(sender.Sent).Period);
        // 2 November: the previous working day, 30 October, is in a month that can no longer be changed.
        await Run(new DateOnly(2026, 11, 2), new TimeOnly(10, 0));
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task The_month_end_reminder_reports_the_shortfall_on_the_last_working_day()
    {
        var (alice, carol, dave) = (Member("Alice"), Member("Carol"), Member("Dave", Day(26)));
        directory.Members[tenantA] = [alice, carol, dave];
        await Settings(tenantA, daily: false, monthEnd: true);
        await using (var scope = Scope(tenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
            db.Holidays.Add(new Holiday { TenantId = tenantA, Date = Day(23), Name = "Chulalongkorn Day" });
            // 20 working days before the 30th: 1–29 October less weekends and the holiday.
            foreach (var day in WorkingDaysBefore(30).Where(day => day != Day(23)))
            {
                db.TimeEntries.Add(Entry(alice.Id, day, 480));
                if (day == Day(29)) continue;
                if (day == Day(28)) db.LeaveEntries.Add(new LeaveEntry { TenantId = tenantA, OwnerId = carol.Id, Date = day, Kind = "Annual" });
                else db.TimeEntries.Add(Entry(carol.Id, day, 480));
            }
            await db.SaveChangesAsync();
        }

        await Run(Day(29), new TimeOnly(10, 0));
        Assert.Empty(sender.Sent);
        await Run(Day(30), new TimeOnly(10, 0));
        var emails = sender.Sent.ToDictionary(x => x.Email);
        Assert.Equal(2, emails.Count);
        var short1 = emails["carol@example.test"];
        Assert.Equal(("month-end", "October 2026", 152m, 160m), (short1.Kind, short1.Period, short1.LoggedHours, short1.ExpectedHours));
        Assert.Equal(["Thu 29 Oct"], short1.MissingDays);
        // Dave joined on the 26th, so only four days are expected of him.
        var joined = emails["dave@example.test"];
        Assert.Equal((0m, 32m), (joined.LoggedHours, joined.ExpectedHours));
        Assert.Equal(["Mon 26 Oct", "Tue 27 Oct", "Wed 28 Oct", "Thu 29 Oct"], joined.MissingDays);

        await Run(Day(30), new TimeOnly(11, 0));
        Assert.Equal(2, sender.Sent.Count);
    }

    [Fact]
    public async Task A_locked_month_or_an_organization_without_settings_gets_nothing()
    {
        directory.Members[tenantA] = [Member("Carol")];
        directory.Members[tenantB] = [Member("Frank")];
        await Settings(tenantA, daily: true, monthEnd: true);
        await using (var scope = Scope(tenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
            db.MonthLocks.Add(new MonthLock { TenantId = tenantA, Year = 2026, Month = 10, LockedBy = admin, LockedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await Run(Day(30), new TimeOnly(10, 0));
        Assert.Empty(sender.Sent);
    }

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<TimesheetTenantScope>();
        builder.Services.AddScoped<TimesheetOperations>();
        builder.Services.AddSingleton<IReminderDirectory>(directory);
        builder.Services.AddSingleton<IReminderSender>(sender);
        builder.Services.AddScoped<MissingEntryReminders>();
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

    private static DateOnly Day(int day) => new(2026, 10, day);

    private static IEnumerable<DateOnly> WorkingDaysBefore(int day) => Enumerable.Range(1, day - 1).Select(Day)
        .Where(x => x.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));

    /// <param name="joined">The day the member joined; by default before October.</param>
    private static ReminderMember Member(string name, DateOnly? joined = null) =>
        new(Guid.NewGuid(), $"{name.ToLowerInvariant()}@example.test", name,
            TimeZoneInfo.ConvertTimeToUtc((joined ?? new DateOnly(2026, 9, 1)).ToDateTime(new TimeOnly(10, 0)), Bangkok));

    private TimeEntry Entry(Guid owner, DateOnly date, int minutes) => new()
    {
        TenantId = tenantA, OwnerId = owner, Date = date, StartTime = new TimeOnly(9, 0),
        EndTime = new TimeOnly(9, 0).AddMinutes(minutes), DurationMinutes = minutes, TaskName = "Work", UpdatedAtUtc = DateTime.UtcNow
    };

    private async Task Settings(Guid tenant, bool daily, bool monthEnd)
    {
        await using var scope = Scope(tenant);
        var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
        db.ReminderSettings.Add(new ReminderSettings { TenantId = tenant, DailyEnabled = daily, MonthEndEnabled = monthEnd });
        await db.SaveChangesAsync();
    }

    /// <summary>Runs the reminders at a local time in Bangkok.</summary>
    private async Task Run(DateOnly day, TimeOnly time)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MissingEntryReminders>().RunAsync(
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(time), Bangkok), TimeSpan.Zero), default);
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

    private static async Task<JsonElement> Result(HttpResponseMessage response)
    {
        using (response)
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").Clone();
    }

    private sealed class FakeDirectory : IReminderDirectory
    {
        public Dictionary<Guid, IReadOnlyList<ReminderMember>> Members { get; } = [];
        public Task<string?> TimeZoneAsync(Guid tenantId, CancellationToken ct) => Task.FromResult<string?>("Asia/Bangkok");
        public Task<IReadOnlyList<ReminderMember>> MembersAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult(Members.GetValueOrDefault(tenantId) ?? []);
    }

    private sealed class FakeSender : IReminderSender
    {
        public List<ReminderEmail> Sent { get; } = [];
        public Task SendAsync(ReminderEmail email, CancellationToken ct)
        {
            Sent.Add(email);
            return Task.CompletedTask;
        }
    }
}
