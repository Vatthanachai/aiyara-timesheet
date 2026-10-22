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

/// <summary>Project billing details, hours used against budget, and usage by project and client. See the
/// Billable Work And Budgets Plan.</summary>
public sealed class ProjectBillingTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
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
    public async Task Admins_set_billing_details_and_refusals_are_named()
    {
        var created = await Result(await Send(HttpMethod.Post, "/api/v1/projects", admin, new
        {
            name = "Rollout", client = "  Acme  ", isBillable = true, hourlyRate = 1200.5, currencyCode = "thb", budgetHours = 100
        }, "TenantAdmin"));
        Assert.Equal("Acme", created.GetProperty("client").GetString());
        Assert.Equal("THB", created.GetProperty("currencyCode").GetString());
        Assert.True(created.GetProperty("isBillable").GetBoolean());

        // A project made with a name alone is not billable and has no rate or budget.
        var plain = await Result(await Send(HttpMethod.Post, "/api/v1/projects", admin, new { name = "Internal" }, "TenantAdmin"));
        Assert.False(plain.GetProperty("isBillable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("hourlyRate").ValueKind);
        var id = plain.GetProperty("id").GetGuid();

        static string Path(Guid project) => $"/api/v1/projects/{project}/billing";
        var saved = await Result(await Send(HttpMethod.Put, Path(id), admin, new
        {
            client = "Globex", isBillable = true, hourlyRate = 80, currencyCode = "usd", budgetHours = 12.5
        }, "TenantAdmin"));
        Assert.Equal((80m, "USD", 12.5m), (saved.GetProperty("hourlyRate").GetDecimal(),
            saved.GetProperty("currencyCode").GetString(), saved.GetProperty("budgetHours").GetDecimal()));
        // Renaming leaves the billing details alone; null clears them.
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, $"/api/v1/projects/{id}", admin, new { name = "Internal 2" }, "TenantAdmin")).StatusCode);
        var kept = (await Result(await Send(HttpMethod.Get, "/api/v1/catalog", admin, null, "TenantAdmin"))).GetProperty("projects")
            .EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal("Globex", kept.GetProperty("client").GetString());
        var cleared = await Result(await Send(HttpMethod.Put, Path(id), admin, new { client = "", isBillable = false, hourlyRate = (decimal?)null, currencyCode = (string?)null, budgetHours = (decimal?)null }, "TenantAdmin"));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("client").ValueKind);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("budgetHours").ValueKind);

        foreach (var bad in new object[]
        {
            new { client = (string?)null, isBillable = true, hourlyRate = -1, currencyCode = "THB", budgetHours = (decimal?)null },
            new { client = (string?)null, isBillable = true, hourlyRate = 10.123, currencyCode = "THB", budgetHours = (decimal?)null },
            new { client = (string?)null, isBillable = true, hourlyRate = 1000001, currencyCode = "THB", budgetHours = (decimal?)null },
            new { client = (string?)null, isBillable = true, hourlyRate = 10, currencyCode = (string?)null, budgetHours = (decimal?)null },
            new { client = (string?)null, isBillable = true, hourlyRate = 10, currencyCode = "BAHT", budgetHours = (decimal?)null },
            new { client = (string?)null, isBillable = true, hourlyRate = 10, currencyCode = "T1B", budgetHours = (decimal?)null },
            new { client = (string?)null, isBillable = false, hourlyRate = (decimal?)null, currencyCode = (string?)null, budgetHours = -5 },
            new { client = (string?)null, isBillable = false, hourlyRate = (decimal?)null, currencyCode = (string?)null, budgetHours = 0 },
            new { client = new string('x', 201), isBillable = false, hourlyRate = (decimal?)null, currencyCode = (string?)null, budgetHours = (decimal?)null }
        })
            Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Send(HttpMethod.Put, Path(id), admin, bad, "TenantAdmin")));
        Assert.Equal("VALIDATION_FAILED", await ErrorCode(await Send(HttpMethod.Post, "/api/v1/projects", admin,
            new { name = "Bad", hourlyRate = -3, currencyCode = "THB" }, "TenantAdmin")));

        var body = new { client = "X", isBillable = true, hourlyRate = 1, currencyCode = "THB", budgetHours = 1 };
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Put, Path(id), alice, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/v1/projects", alice, new { name = "Mine", hourlyRate = 1, currencyCode = "THB" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put, Path(id), admin, body, "TenantAdmin", tenantB)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put, Path(Guid.NewGuid()), admin, body, "TenantAdmin")).StatusCode);
    }

    [Fact]
    public async Task Members_see_projects_without_rates_and_other_tenants_see_nothing()
    {
        var project = await Result(await Send(HttpMethod.Post, "/api/v1/projects", admin, new
        {
            name = "Rollout", client = "Acme", isBillable = true, hourlyRate = 500, currencyCode = "THB", budgetHours = 10
        }, "TenantAdmin"));
        var forAdmin = (await Result(await Send(HttpMethod.Get, "/api/v1/catalog", admin, null, "TenantAdmin"))).GetProperty("projects")[0];
        Assert.Equal(500m, forAdmin.GetProperty("hourlyRate").GetDecimal());
        var forMember = (await Result(await Send(HttpMethod.Get, "/api/v1/catalog", alice))).GetProperty("projects")[0];
        Assert.Equal(JsonValueKind.Null, forMember.GetProperty("hourlyRate").ValueKind);
        Assert.Equal(JsonValueKind.Null, forMember.GetProperty("currencyCode").ValueKind);
        Assert.Equal("Acme", forMember.GetProperty("client").GetString());
        Assert.True(forMember.GetProperty("isBillable").GetBoolean());
        // The stored rate is untouched by a member reading the catalog.
        Assert.Equal(500m, (await Result(await Send(HttpMethod.Get, "/api/v1/catalog", admin, null, "TenantAdmin")))
            .GetProperty("projects")[0].GetProperty("hourlyRate").GetDecimal());

        Assert.Empty((await Result(await Send(HttpMethod.Get, "/api/v1/catalog", admin, null, "TenantAdmin", tenantB))).GetProperty("projects").EnumerateArray());
        var id = project.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"/api/v1/projects/{id}/usage", admin, null, "TenantAdmin", tenantB)).StatusCode);
        Assert.Empty((await Result(await Send(HttpMethod.Get, "/api/v1/projects/usage", admin, null, "TenantAdmin", tenantB))).GetProperty("projects").EnumerateArray());
        Assert.Empty((await Result(await Send(HttpMethod.Get, "/api/v1/clients/usage", admin, null, "TenantAdmin", tenantB))).GetProperty("clients").EnumerateArray());
    }

    [Fact]
    public async Task Usage_counts_worked_minutes_without_break_or_deleted_entries()
    {
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, "/api/v1/work-week", admin,
            new { workingDays = new[] { 0, 1, 2, 3, 4, 5, 6 }, startTime = "09:00", endTime = "17:00", breakStart = "12:00", breakEnd = "13:00" }, "TenantAdmin")).StatusCode);
        var rollout = await Project("Rollout", "Acme", true, 100m, "THB", 8m);

        // 09:00-14:00 holds the 1 h break: 4 h. 14:00-15:30: 1.5 h. Worked through the break, 10:00-13:00: 3 h. Deleted: nothing.
        await Entry(alice, rollout, "09:00", "14:00");
        await Entry(bob, rollout, "14:00", "15:30");
        var worked = await Entry(bob, rollout, "10:00", "13:00", includesBreak: true);
        var removed = await Entry(alice, rollout, "15:30", "17:00");
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/v1/entries/{removed}", alice)).StatusCode);
        await Entry(alice, null, "15:00", "16:00");

        var usage = await Result(await Send(HttpMethod.Get, $"/api/v1/projects/{rollout}/usage", admin, null, "TenantAdmin"));
        var row = usage.GetProperty("project");
        // 240 + 90 + 180 = 510 minutes = 8.5 h against a budget of 8 h.
        Assert.Equal(510, row.GetProperty("usedMinutes").GetInt32());
        Assert.Equal(8.5m, row.GetProperty("usedHours").GetDecimal());
        Assert.Equal(-0.5m, row.GetProperty("remainingHours").GetDecimal());
        Assert.True(row.GetProperty("overBudget").GetBoolean());
        Assert.Equal(510, row.GetProperty("billableMinutes").GetInt32());
        Assert.Equal(850m, row.GetProperty("billableAmount").GetDecimal());
        var members = usage.GetProperty("members").EnumerateArray()
            .Select(x => (x.GetProperty("ownerId").GetGuid(), x.GetProperty("minutes").GetInt32())).ToArray();
        Assert.Equal([(bob, 270), (alice, 240)], members);

        var all = await Result(await Send(HttpMethod.Get, "/api/v1/projects/usage", admin, null, "TenantAdmin"));
        Assert.Equal(60, all.GetProperty("unassignedMinutes").GetInt32());
        Assert.Equal(510, all.GetProperty("totals").GetProperty("periodMinutes").GetInt32());

        // Editing the entry worked through the break to 09:00-10:00 counts what it now covers: 60 minutes.
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, $"/api/v1/entries/{worked}", bob, new
        { date = today, startTime = "09:00", endTime = "10:00", taskName = "Work", projectId = rollout, includesBreak = false })).StatusCode);
        var after = (await Result(await Send(HttpMethod.Get, $"/api/v1/projects/{rollout}/usage", admin, null, "TenantAdmin"))).GetProperty("project");
        Assert.Equal(390, after.GetProperty("usedMinutes").GetInt32());
        Assert.False(after.GetProperty("overBudget").GetBoolean());
        Assert.Equal(1.5m, after.GetProperty("remainingHours").GetDecimal());
    }

    [Fact]
    public async Task Usage_by_period_rate_and_billable_flag()
    {
        var billable = await Project("Billable", "Acme", true, 150m, "THB", 40m);
        var free = await Project("Free", "Acme", false, 150m, "THB", null);
        var norate = await Project("No rate", "acme", true, null, null, null);
        var deleted = await Project("Gone", "Gone Co", true, 10m, "USD", null);
        await Seed(billable, alice, new DateOnly(2026, 3, 31), 60);
        await Seed(billable, alice, new DateOnly(2026, 4, 1), 100);
        await Seed(billable, bob, new DateOnly(2026, 4, 30), 20);
        await Seed(billable, bob, new DateOnly(2026, 5, 1), 600);
        await Seed(free, alice, new DateOnly(2026, 4, 2), 90);
        await Seed(norate, alice, new DateOnly(2026, 4, 3), 30);
        await Seed(deleted, alice, new DateOnly(2026, 4, 4), 60);
        await Seed(deleted, alice, new DateOnly(2026, 6, 4), 60, isDeleted: true);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, $"/api/v1/projects/{deleted}", admin, null, "TenantAdmin")).StatusCode);

        var april = await Result(await Send(HttpMethod.Get, "/api/v1/projects/usage?from=2026-04-01&to=2026-04-30", admin, null, "TenantAdmin"));
        var rows = april.GetProperty("projects").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
        Assert.Equal(["Billable", "Free", "Gone", "No rate"], april.GetProperty("projects").EnumerateArray().Select(x => x.GetProperty("name").GetString()));
        Assert.True(rows["Gone"].GetProperty("isDeleted").GetBoolean());
        // 120 minutes in April at 150 an hour; all time 780 minutes = 13 h of a 40 h budget.
        Assert.Equal(120, rows["Billable"].GetProperty("periodMinutes").GetInt32());
        Assert.Equal(300m, rows["Billable"].GetProperty("billableAmount").GetDecimal());
        Assert.Equal(780, rows["Billable"].GetProperty("usedMinutes").GetInt32());
        Assert.Equal(27m, rows["Billable"].GetProperty("remainingHours").GetDecimal());
        Assert.Equal(90, rows["Free"].GetProperty("periodMinutes").GetInt32());
        Assert.Equal(0, rows["Free"].GetProperty("billableMinutes").GetInt32());
        Assert.Equal(JsonValueKind.Null, rows["Free"].GetProperty("billableAmount").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows["Free"].GetProperty("remainingHours").ValueKind);
        Assert.False(rows["Free"].GetProperty("overBudget").GetBoolean());
        Assert.Equal(30, rows["No rate"].GetProperty("billableMinutes").GetInt32());
        Assert.Equal(JsonValueKind.Null, rows["No rate"].GetProperty("billableAmount").ValueKind);
        var amounts = april.GetProperty("totals").GetProperty("amounts").EnumerateArray()
            .Select(x => (x.GetProperty("currencyCode").GetString(), x.GetProperty("amount").GetDecimal())).ToArray();
        Assert.Equal([("THB", 300m), ("USD", 10m)], amounts);
        Assert.Equal(120 + 90 + 30 + 60, april.GetProperty("totals").GetProperty("periodMinutes").GetInt32());

        // A deleted project with nothing in the period is left out; an open-ended period reaches back.
        var june = await Result(await Send(HttpMethod.Get, "/api/v1/projects/usage?from=2026-06-01", admin, null, "TenantAdmin"));
        Assert.DoesNotContain(june.GetProperty("projects").EnumerateArray(), x => x.GetProperty("name").GetString() == "Gone");
        var upTo = await Result(await Send(HttpMethod.Get, "/api/v1/projects/usage?to=2026-03-31", admin, null, "TenantAdmin"));
        Assert.Equal(60, upTo.GetProperty("totals").GetProperty("periodMinutes").GetInt32());

        // Rounded to the cent: 20 minutes at 1.99 an hour is 0.663..., so 0.66.
        await Result(await Send(HttpMethod.Put, $"/api/v1/projects/{billable}/billing", admin,
            new { client = "Acme", isBillable = true, hourlyRate = 1.99, currencyCode = "THB", budgetHours = (decimal?)null }, "TenantAdmin"));
        var cents = (await Result(await Send(HttpMethod.Get, $"/api/v1/projects/{billable}/usage?from=2026-04-30&to=2026-04-30", admin, null, "TenantAdmin"))).GetProperty("project");
        Assert.Equal(0.66m, cents.GetProperty("billableAmount").GetDecimal());

        foreach (var query in new[] { "from=2026-05-01&to=2026-04-01", "from=1999-01-01", "to=2101-01-01" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, $"/api/v1/projects/usage?{query}", admin, null, "TenantAdmin")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, "/api/v1/projects/usage?from=nonsense", admin, null, "TenantAdmin")).StatusCode);
    }

    [Fact]
    public async Task Usage_by_client_groups_names_ignoring_case_and_keeps_currencies_apart()
    {
        var a = await Project("A", "Acme", true, 100m, "THB", null);
        var b = await Project("B", "ACME", true, 10m, "USD", null);
        var c = await Project("C", "Acme", false, 100m, "THB", null);
        var d = await Project("D", "Zed", true, 60m, "THB", null);
        var e = await Project("E", null, true, 60m, "THB", null);
        await Seed(a, alice, today, 120);
        await Seed(b, alice, today, 60);
        await Seed(c, bob, today, 30);
        await Seed(d, bob, today, 60);
        await Seed(e, bob, today, 60);

        var result = await Result(await Send(HttpMethod.Get, "/api/v1/clients/usage", admin, null, "TenantAdmin"));
        var clients = result.GetProperty("clients").EnumerateArray().ToArray();
        Assert.Equal(["Acme", "Zed", null], clients.Select(x => x.GetProperty("client").GetString()));
        var acme = clients[0];
        Assert.Equal(3, acme.GetProperty("projectCount").GetInt32());
        Assert.Equal(210, acme.GetProperty("periodMinutes").GetInt32());
        Assert.Equal(180, acme.GetProperty("billableMinutes").GetInt32());
        Assert.Equal(30, acme.GetProperty("nonBillableMinutes").GetInt32());
        Assert.Equal([("THB", 200m), ("USD", 10m)], acme.GetProperty("amounts").EnumerateArray()
            .Select(x => (x.GetProperty("currencyCode").GetString(), x.GetProperty("amount").GetDecimal())));
        Assert.Equal(60m, clients[2].GetProperty("amounts")[0].GetProperty("amount").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/v1/clients/usage", alice)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/v1/projects/usage", alice)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, $"/api/v1/projects/{a}/usage", alice)).StatusCode);
    }

    private async Task<Guid> Project(string name, string? client, bool billable, decimal? rate, string? currency, decimal? budget) =>
        (await Result(await Send(HttpMethod.Post, "/api/v1/projects", admin, new
        {
            name, client, isBillable = billable, hourlyRate = rate, currencyCode = currency, budgetHours = budget
        }, "TenantAdmin"))).GetProperty("id").GetGuid();

    private async Task<Guid> Entry(Guid user, Guid? project, string start, string end, bool includesBreak = false) =>
        (await Result(await Send(HttpMethod.Post, "/api/v1/entries", user, new
        {
            date = today, startTime = start, endTime = end, taskName = "Work", projectId = project, includesBreak
        }))).GetProperty("id").GetGuid();

    /// <summary>An entry written straight to the database, for dates a member could not log today.</summary>
    private async Task Seed(Guid project, Guid owner, DateOnly date, int minutes, bool isDeleted = false)
    {
        await using var scope = Scope(tenantA);
        var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
        db.TimeEntries.Add(new TimeEntry { TenantId = tenantA, OwnerId = owner, Date = date, StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(9, 0).AddMinutes(minutes), DurationMinutes = minutes, TaskName = "Seeded", ProjectId = project,
            IsDeleted = isDeleted, UpdatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
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
