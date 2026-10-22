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

/// <summary>A member submits a month; a Tenant Admin approves it or returns it with a reason.
/// See the Timesheet Approval Plan.</summary>
public sealed class MonthApprovalTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly Guid tenantA = Guid.NewGuid();
    private readonly Guid tenantB = Guid.NewGuid();
    private readonly Guid admin = Guid.NewGuid();
    private readonly Guid alice = Guid.NewGuid();
    private readonly Guid bob = Guid.NewGuid();
    private readonly DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok")).DateTime);
    private readonly RecordingNotifier notifier = new();
    private WebApplication app = null!;
    private HttpClient client = null!;

    private string ThisMonth => $"{today.Year}/{today.Month}";
    private DateOnly LastMonthDay => new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
    private string LastMonth => $"{LastMonthDay.Year}/{LastMonthDay.Month}";

    [Fact]
    public async Task Submit_return_resubmit_and_approve_a_month()
    {
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/v1/entries", alice, Entry(today))).StatusCode);
        Assert.Equal((true, "open"), await MyMonth(alice, ThisMonth));

        using (var submitted = await Send(HttpMethod.Post, $"/api/v1/me/months/{ThisMonth}/submit", alice))
            Assert.Equal("submitted", (await Result(submitted)).GetProperty("status").GetString());
        Assert.Equal((false, "submitted"), await MyMonth(alice, ThisMonth));
        using (var blocked = await Send(HttpMethod.Post, "/api/v1/entries", alice, Entry(today)))
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        using (var returned = await Send(HttpMethod.Post, $"/api/v1/members/{alice}/months/{ThisMonth}/return", admin,
            new { reason = "  Add the client meeting on Monday.  " }, "TenantAdmin"))
            Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        using (var month = await Send(HttpMethod.Get, $"/api/v1/me/months/{ThisMonth}", alice))
        {
            var result = await Result(month);
            Assert.Equal("returned", result.GetProperty("status").GetString());
            Assert.Equal("Add the client meeting on Monday.", result.GetProperty("returnReason").GetString());
            Assert.True(result.GetProperty("editable").GetBoolean());
        }
        Assert.Equal((tenantA, alice, today.Year, today.Month, "Add the client meeting on Monday."), Assert.Single(notifier.Sent));
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/v1/entries", alice, Entry(today))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/v1/me/months/{ThisMonth}/submit", alice)).StatusCode);
        Assert.Equal((false, "submitted"), await MyMonth(alice, ThisMonth));
        Assert.Equal(HttpStatusCode.OK,
            (await Send(HttpMethod.Post, $"/api/v1/members/{alice}/months/{ThisMonth}/approve", admin, null, "TenantAdmin")).StatusCode);
        Assert.Equal((false, "approved"), await MyMonth(alice, ThisMonth));

        await using var scope = Scope(tenantA);
        var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
        var snapshot = await db.MonthSnapshots.SingleAsync(x => x.OwnerId == alice);
        Assert.Equal(2, JsonDocument.Parse(snapshot.PayloadJson).RootElement.GetProperty("entries").GetArrayLength());
        Assert.Equal(["submitted", "returned", "submitted", "approved"], await db.Audits
            .Where(x => x.EntityType == "month_approval").OrderBy(x => x.OccurredAtUtc).Select(x => x.Action).ToListAsync());
        Assert.All(await db.Audits.Where(x => x.EntityType == "month_approval").ToListAsync(), x => Assert.Equal(alice, x.SubjectId));

        using var summary = await Send(HttpMethod.Get, $"/api/v1/members/summary?year={today.Year}&month={today.Month}", admin, null, "TenantAdmin");
        Assert.Equal("approved", Assert.Single((await Result(summary)).GetProperty("members").EnumerateArray()).GetProperty("status").GetString());
        using var detail = await Send(HttpMethod.Get, $"/api/v1/members/{alice}/timesheet?year={today.Year}&month={today.Month}", admin, null, "TenantAdmin");
        Assert.Equal("approved", (await Result(detail)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_returned_past_month_can_be_edited_until_it_is_locked()
    {
        using (var closed = await Send(HttpMethod.Post, "/api/v1/entries", alice, Entry(LastMonthDay)))
            Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        Assert.Equal((false, "open"), await MyMonth(alice, LastMonth));

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/v1/me/months/{LastMonth}/submit", alice)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/v1/members/{alice}/months/{LastMonth}/return", admin,
            new { reason = "The last week is missing." }, "TenantAdmin")).StatusCode);
        Assert.Equal((true, "returned"), await MyMonth(alice, LastMonth));
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/v1/entries", alice, Entry(LastMonthDay))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/v1/leave", alice,
            new { date = LastMonthDay, kind = "sick" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/v1/months/{LastMonth}/lock", admin, null, "TenantAdmin")).StatusCode);
        Assert.Equal((false, "returned"), await MyMonth(alice, LastMonth));
        using (var locked = await Send(HttpMethod.Post, "/api/v1/entries", alice, Entry(LastMonthDay)))
            Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        using (var submitLocked = await Send(HttpMethod.Post, $"/api/v1/me/months/{LastMonth}/submit", alice))
            Assert.Equal("MONTH_CLOSED", await ErrorCode(submitLocked));
    }

    [Fact]
    public async Task Locking_keeps_the_snapshot_taken_at_approval()
    {
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/v1/entries", alice, Entry(today))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/v1/me/months/{ThisMonth}/submit", alice)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Send(HttpMethod.Post, $"/api/v1/members/{alice}/months/{ThisMonth}/approve", admin, null, "TenantAdmin")).StatusCode);
        DateTime approvedAt;
        await using (var scope = Scope(tenantA))
            approvedAt = (await scope.ServiceProvider.GetRequiredService<TimesheetDbContext>().MonthSnapshots.SingleAsync()).CreatedAtUtc;
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/v1/entries", bob, Entry(today))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/v1/months/{ThisMonth}/lock", admin, null, "TenantAdmin")).StatusCode);

        await using var after = Scope(tenantA);
        var snapshots = await after.ServiceProvider.GetRequiredService<TimesheetDbContext>().MonthSnapshots.ToListAsync();
        Assert.Equal(approvedAt, snapshots.Single(x => x.OwnerId == alice).CreatedAtUtc);
        Assert.Single(snapshots, x => x.OwnerId == bob);
        Assert.Equal(2, JsonDocument.Parse(snapshots.Single(x => x.OwnerId == Guid.Empty).PayloadJson)
            .RootElement.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task Transitions_that_do_not_apply_are_refused()
    {
        var future = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        using var employeeApproves = await Send(HttpMethod.Post, $"/api/v1/members/{alice}/months/{ThisMonth}/approve", bob, null, "Employee");
        using var approveOpen = await Send(HttpMethod.Post, $"/api/v1/members/{alice}/months/{ThisMonth}/approve", admin, null, "TenantAdmin");
        using var submitFuture = await Send(HttpMethod.Post, $"/api/v1/me/months/{future.Year}/{future.Month}/submit", alice);
        using var submitBadMonth = await Send(HttpMethod.Post, $"/api/v1/me/months/{today.Year}/13/submit", alice);
        using var submit = await Send(HttpMethod.Post, $"/api/v1/me/months/{ThisMonth}/submit", alice);
        using var submitAgain = await Send(HttpMethod.Post, $"/api/v1/me/months/{ThisMonth}/submit", alice);
        using var noReason = await Send(HttpMethod.Post, $"/api/v1/members/{alice}/months/{ThisMonth}/return", admin,
            new { reason = "   " }, "TenantAdmin");
        using var longReason = await Send(HttpMethod.Post, $"/api/v1/members/{alice}/months/{ThisMonth}/return", admin,
            new { reason = new string('x', 1001) }, "TenantAdmin");
        using var employeeReturns = await Send(HttpMethod.Post, $"/api/v1/members/{alice}/months/{ThisMonth}/return", bob,
            new { reason = "No" }, "Employee");
        using var otherTenant = await Send(HttpMethod.Post, $"/api/v1/members/{alice}/months/{ThisMonth}/approve", admin,
            null, "TenantAdmin", tenantB);
        using var anonymous = await Send(HttpMethod.Post, $"/api/v1/me/months/{ThisMonth}/submit", null);

        Assert.Equal(HttpStatusCode.Forbidden, employeeApproves.StatusCode);
        Assert.Equal("INVALID_TRANSITION", await ErrorCode(approveOpen));
        Assert.Equal("INVALID_DATE", await ErrorCode(submitFuture));
        Assert.Equal("INVALID_DATE", await ErrorCode(submitBadMonth));
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        Assert.Equal("INVALID_TRANSITION", await ErrorCode(submitAgain));
        Assert.Equal("VALIDATION_FAILED", await ErrorCode(noReason));
        Assert.Equal("VALIDATION_FAILED", await ErrorCode(longReason));
        Assert.Equal(HttpStatusCode.Forbidden, employeeReturns.StatusCode);
        Assert.Equal("INVALID_TRANSITION", await ErrorCode(otherTenant));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal((false, "submitted"), await MyMonth(alice, ThisMonth));
        Assert.Empty(notifier.Sent);
    }

    [Fact]
    public void Future_months_follow_the_tenant_calendar()
    {
        var now = new DateTimeOffset(2026, 9, 30, 18, 0, 0, TimeSpan.Zero); // 1 October in Bangkok
        Assert.False(TimesheetRules.IsFutureMonth(2026, 10, "Asia/Bangkok", now));
        Assert.True(TimesheetRules.IsFutureMonth(2026, 11, "Asia/Bangkok", now));
        Assert.True(TimesheetRules.IsFutureMonth(2026, 10, "UTC", now));
        Assert.False(TimesheetRules.IsFutureMonth(2025, 12, "UTC", now));
    }

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<TimesheetTenantScope>();
        builder.Services.AddScoped<TimesheetOperations>();
        builder.Services.AddSingleton<IMonthReturnNotifier>(notifier);
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

    private static object Entry(DateOnly date) => new
    {
        date, startTime = "09:00:00", endTime = "10:00:00", taskName = "Design review"
    };

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, Guid? user, object? body = null,
        string role = "Employee", Guid? tenant = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (user is not null)
        {
            request.Headers.Add("X-Test-Tenant", (tenant ?? tenantA).ToString());
            request.Headers.Add("X-Test-User", user.ToString());
            request.Headers.Add("X-Test-Role", role);
        }
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private async Task<(bool Editable, string Status)> MyMonth(Guid user, string month)
    {
        using var response = await Send(HttpMethod.Get, $"/api/v1/me/months/{month}", user);
        var result = await Result(response);
        return (result.GetProperty("editable").GetBoolean(), result.GetProperty("status").GetString()!);
    }

    private static async Task<JsonElement> Result(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString();

    private sealed class RecordingNotifier : IMonthReturnNotifier
    {
        public List<(Guid Tenant, Guid Owner, int Year, int Month, string Reason)> Sent { get; } = [];

        public Task MonthReturnedAsync(Guid tenantId, Guid ownerId, int year, int month, string reason,
            CancellationToken ct)
        {
            Sent.Add((tenantId, ownerId, year, month, reason));
            return Task.CompletedTask;
        }
    }
}
