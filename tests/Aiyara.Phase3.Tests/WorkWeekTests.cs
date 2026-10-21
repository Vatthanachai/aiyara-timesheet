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

/// <summary>The organization's working weekdays: Monday to Friday until a Tenant Admin changes
/// them, shown to every member through the catalog and frozen into closed months.</summary>
public sealed class WorkWeekTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly string postgresDatabase = $"timesheet_work_week_test_{Guid.NewGuid():N}";
    private bool usesPostgres;
    private readonly Guid tenantA = Guid.NewGuid();
    private readonly Guid tenantB = Guid.NewGuid();
    private readonly Guid admin = Guid.NewGuid();
    private WebApplication app = null!;
    private HttpClient client = null!;

    [Fact]
    public async Task An_organization_works_monday_to_friday_until_an_admin_changes_it()
    {
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, await CatalogDays("Employee"));

        using var saved = await Send(HttpMethod.Put, "/api/v1/work-week", "TenantAdmin", [6, 1, 2, 3, 4]);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(new[] { 1, 2, 3, 4, 6 }, Days(await Result(saved)));
        Assert.Equal(new[] { 1, 2, 3, 4, 6 }, await CatalogDays("Employee"));

        using var updated = await Send(HttpMethod.Put, "/api/v1/work-week", "TenantAdmin", [0, 1, 2, 3, 4]);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, await CatalogDays("Employee"));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, await CatalogDays("Employee", tenantB));

        await using var scope = Scope(tenantA);
        var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
        Assert.Single(await db.WorkWeeks.ToListAsync());
        Assert.Equal(["created", "updated"], await db.Audits.Where(x => x.EntityType == "work_week")
            .OrderBy(x => x.OccurredAtUtc).Select(x => x.Action).ToListAsync());
    }

    [Fact]
    public async Task Only_admins_change_it_and_every_weekday_must_be_valid()
    {
        using var employee = await Send(HttpMethod.Put, "/api/v1/work-week", "Employee", [1, 2, 3]);
        using var anonymous = await Send(HttpMethod.Put, "/api/v1/work-week", null, [1, 2, 3]);
        using var empty = await Send(HttpMethod.Put, "/api/v1/work-week", "TenantAdmin", []);
        using var outOfRange = await Send(HttpMethod.Put, "/api/v1/work-week", "TenantAdmin", [1, 7]);
        using var negative = await Send(HttpMethod.Put, "/api/v1/work-week", "TenantAdmin", [-1, 2]);
        using var duplicate = await Send(HttpMethod.Put, "/api/v1/work-week", "TenantAdmin", [1, 1]);

        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, await CatalogDays("Employee"));
    }

    [Fact]
    public async Task A_closed_month_keeps_the_working_days_it_was_closed_with()
    {
        var previous = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);
        using (var saved = await Send(HttpMethod.Put, "/api/v1/work-week", "TenantAdmin", [0, 1, 2, 3, 4]))
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using (var locked = await Send(HttpMethod.Post,
            $"/api/v1/months/{previous.Year}/{previous.Month}/lock", "TenantAdmin"))
            Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        using (var changed = await Send(HttpMethod.Put, "/api/v1/work-week", "TenantAdmin", [1, 2, 3, 4, 5]))
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        using var snapshot = await Send(HttpMethod.Get,
            $"/api/v1/months/{previous.Year}/{previous.Month}/snapshot?ownerId={Guid.Empty}", "TenantAdmin");
        using var member = await Send(HttpMethod.Get,
            $"/api/v1/members/{admin}/timesheet?year={previous.Year}&month={previous.Month}", "TenantAdmin");

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, Days(await Result(snapshot)));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Days(await Result(member)));
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

    private async Task<int[]> CatalogDays(string role, Guid? tenant = null)
    {
        using var response = await Send(HttpMethod.Get, "/api/v1/catalog", role, tenant: tenant);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Days(await Result(response));
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? role,
        int[]? workingDays = null, Guid? tenant = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (workingDays is not null) request.Content = JsonContent.Create(new WorkWeekRequest(workingDays));
        if (role is not null)
        {
            request.Headers.Add("X-Test-Tenant", (tenant ?? tenantA).ToString());
            request.Headers.Add("X-Test-Role", role);
        }
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Result(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");

    private static int[] Days(JsonElement result) =>
        result.GetProperty("workingDays").EnumerateArray().Select(x => x.GetInt32()).ToArray();
}
