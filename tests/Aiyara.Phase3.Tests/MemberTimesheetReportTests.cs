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

/// <summary>The Tenant Admin member timesheet report: a monthly summary per member and one
/// member's month, read live and limited to the admin's own tenant.</summary>
public sealed class MemberTimesheetReportTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly string postgresDatabase = $"timesheet_members_test_{Guid.NewGuid():N}";
    private bool usesPostgres;
    private readonly Guid tenantA = Guid.NewGuid();
    private readonly Guid tenantB = Guid.NewGuid();
    private readonly Guid admin = Guid.NewGuid();
    private readonly Guid alice = Guid.NewGuid();
    private readonly Guid bob = Guid.NewGuid();
    private readonly DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok")).DateTime);
    private WebApplication app = null!;
    private HttpClient client = null!;

    [Fact]
    public async Task Admin_sees_each_members_hours_entries_and_leave_for_the_month()
    {
        await AddEntryAsync(alice, "Design review", 9, 12);
        await AddEntryAsync(alice, "Pairing", 13, 15);
        await AddEntryAsync(bob, "Support", 9, 10);
        await AddLeaveAsync(bob);
        await AddEntryAsync(alice, "Last month", 9, 17, today.AddMonths(-1));
        await AddEntryAsync(alice, "Deleted", 9, 17, deleted: true);

        using var response = await Get($"/api/v1/members/summary?year={today.Year}&month={today.Month}", "TenantAdmin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Result(response);
        Assert.False(result.GetProperty("locked").GetBoolean());
        var members = result.GetProperty("members").EnumerateArray().ToList();
        Assert.Equal([alice, bob], members.Select(x => x.GetProperty("ownerId").GetGuid()));
        Assert.Equal((2, 300, 0), Summary(members[0]));
        Assert.Equal((1, 60, 1), Summary(members[1]));
    }

    [Fact]
    public async Task Admin_reads_one_members_month_with_project_names_leave_and_holidays()
    {
        Guid projectId;
        await using (var scope = Scope(tenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
            var project = new Project { TenantId = tenantA, Name = "Apollo" };
            db.Projects.Add(project);
            db.Holidays.Add(new Holiday { TenantId = tenantA, Date = today, Name = "Founders day" });
            await db.SaveChangesAsync();
            projectId = project.Id;
        }
        await AddEntryAsync(alice, "Design review", 9, 12, projectId: projectId);
        await AddEntryAsync(bob, "Not Alice's", 9, 10);
        await AddLeaveAsync(alice);

        using var response = await Get($"/api/v1/members/{alice}/timesheet?year={today.Year}&month={today.Month}", "TenantAdmin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Result(response);
        var entry = Assert.Single(result.GetProperty("entries").EnumerateArray().ToList());
        Assert.Equal("Design review", entry.GetProperty("taskName").GetString());
        Assert.Equal("Apollo", entry.GetProperty("projectName").GetString());
        Assert.Equal(180, entry.GetProperty("durationMinutes").GetInt32());
        Assert.Single(result.GetProperty("leaves").EnumerateArray());
        Assert.Equal("Founders day", Assert.Single(result.GetProperty("holidays").EnumerateArray().ToList())
            .GetProperty("name").GetString());
    }

    [Fact]
    public async Task Employees_cannot_read_and_another_organization_sees_nothing()
    {
        await AddEntryAsync(alice, "Design review", 9, 12);
        var month = $"year={today.Year}&month={today.Month}";

        using var employeeSummary = await Get($"/api/v1/members/summary?{month}", "Employee");
        using var employeeDetail = await Get($"/api/v1/members/{alice}/timesheet?{month}", "Employee");
        using var anonymous = await Get($"/api/v1/members/summary?{month}", null);
        using var badMonth = await Get($"/api/v1/members/summary?year={today.Year}&month=13", "TenantAdmin");
        using var otherSummary = await Get($"/api/v1/members/summary?{month}", "TenantAdmin", tenantB);
        using var otherDetail = await Get($"/api/v1/members/{alice}/timesheet?{month}", "TenantAdmin", tenantB);

        Assert.Equal(HttpStatusCode.Forbidden, employeeSummary.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeDetail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badMonth.StatusCode);
        Assert.Empty((await Result(otherSummary)).GetProperty("members").EnumerateArray());
        Assert.Empty((await Result(otherDetail)).GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task Any_member_reads_only_their_own_open_month()
    {
        // The test actor's user is `admin`, whatever role the request claims.
        await AddEntryAsync(admin, "My work", 9, 11);
        await AddEntryAsync(alice, "Alice's work", 9, 17);
        var month = $"year={today.Year}&month={today.Month}";

        using var employee = await Get($"/api/v1/me/timesheet?{month}", "Employee");
        using var anonymous = await Get($"/api/v1/me/timesheet?{month}", null);
        using var badMonth = await Get($"/api/v1/me/timesheet?year={today.Year}&month=0", "Employee");
        using var otherTenant = await Get($"/api/v1/me/timesheet?{month}", "Employee", tenantB);

        Assert.Equal(HttpStatusCode.OK, employee.StatusCode);
        var result = await Result(employee);
        Assert.Equal(admin, result.GetProperty("ownerId").GetGuid());
        Assert.False(result.GetProperty("locked").GetBoolean());
        var entry = Assert.Single(result.GetProperty("entries").EnumerateArray().ToList());
        Assert.Equal("My work", entry.GetProperty("taskName").GetString());
        Assert.Equal(5, result.GetProperty("workingDays").GetArrayLength());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badMonth.StatusCode);
        Assert.Empty((await Result(otherTenant)).GetProperty("entries").EnumerateArray());
    }

    /// <summary>Uses in-memory SQLite, or a throwaway PostgreSQL database built from the real
    /// migrations when AIYARA_TEST_POSTGRES holds a server connection string.</summary>
    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("AIYARA_TEST_POSTGRES");
        if (string.IsNullOrEmpty(server)) await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<TimesheetTenantScope>();
        builder.Services.AddScoped<TimesheetOperations>();
        builder.Services.AddControllers().AddApplicationPart(typeof(TimesheetController).Assembly);
        builder.Services.AddDbContext<TimesheetDbContext>(options =>
        {
            if (string.IsNullOrEmpty(server)) options.UseSqlite(connection);
            else options.UseNpgsql(new Npgsql.NpgsqlConnectionStringBuilder(server)
                { Database = postgresDatabase }.ConnectionString);
        });
        usesPostgres = !string.IsNullOrEmpty(server);
        app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (Guid.TryParse(context.Request.Headers["X-Test-Tenant"], out var tenant))
            {
                context.RequestServices.GetRequiredService<TimesheetTenantScope>().TenantId = tenant;
                context.Items[Actor.ContextKey] = new Actor(tenant, admin,
                    context.Request.Headers["X-Test-Role"].ToString(), "Asia/Bangkok");
            }
            await next();
        });
        app.MapControllers();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>().Database;
            if (usesPostgres) await database.MigrateAsync(); else await database.EnsureCreatedAsync();
        }
        await app.StartAsync();
        client = app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        if (usesPostgres)
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TimesheetDbContext>().Database.EnsureDeletedAsync();
        }
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

    private async Task AddEntryAsync(Guid owner, string task, int fromHour, int toHour, DateOnly? date = null,
        bool deleted = false, Guid? projectId = null)
    {
        await using var scope = Scope(tenantA);
        var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
        db.TimeEntries.Add(new TimeEntry
        {
            TenantId = tenantA, OwnerId = owner, Date = date ?? today, TaskName = task,
            StartTime = new TimeOnly(fromHour, 0), EndTime = new TimeOnly(toHour, 0),
            DurationMinutes = (toHour - fromHour) * 60, IsDeleted = deleted, ProjectId = projectId,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task AddLeaveAsync(Guid owner)
    {
        await using var scope = Scope(tenantA);
        var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
        db.LeaveEntries.Add(new LeaveEntry { TenantId = tenantA, OwnerId = owner, Date = today, Kind = "Vacation" });
        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> Get(string path, string? role, Guid? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (role is not null)
        {
            request.Headers.Add("X-Test-Tenant", (tenant ?? tenantA).ToString());
            request.Headers.Add("X-Test-Role", role);
        }
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Result(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");

    private static (int Entries, int Minutes, int LeaveDays) Summary(JsonElement member) =>
        (member.GetProperty("entryCount").GetInt32(), member.GetProperty("totalMinutes").GetInt32(),
            member.GetProperty("leaveDays").GetInt32());
}
