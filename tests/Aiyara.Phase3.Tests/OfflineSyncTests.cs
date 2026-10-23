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

/// <summary>The server half of offline entry: a create that names its own id is idempotent, so an outbox
/// replaying after a lost answer never writes twice. See the Mobile And Offline Plan.</summary>
public sealed class OfflineSyncTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly Guid tenantA = Guid.NewGuid();
    private readonly Guid tenantB = Guid.NewGuid();
    private readonly Guid alice = Guid.NewGuid();
    private readonly Guid bob = Guid.NewGuid();
    private readonly DateOnly today;
    private WebApplication app = null!;
    private HttpClient client = null!;

    public OfflineSyncTests() => today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok")).DateTime);

    private object Body(Guid? id, string task = "Offline work", string start = "09:00:00", string end = "11:00:00") =>
        new { id, date = today.ToString("yyyy-MM-dd"), startTime = start, endTime = end, taskName = task };

    private Task<HttpResponseMessage> Create(Guid user, object body, Guid? tenant = null) =>
        Send(HttpMethod.Post, "/api/v1/entries", user, body, tenant);

    private async Task<int> Count<T>(Func<TimesheetDbContext, IQueryable<T>> query, Guid? tenant = null) where T : class
    {
        await using var scope = Scope(tenant ?? tenantA);
        return await query(scope.ServiceProvider.GetRequiredService<TimesheetDbContext>()).CountAsync();
    }

    [Fact]
    public async Task A_named_create_writes_once_and_a_replay_returns_the_same_entry()
    {
        var id = Guid.NewGuid();
        using var first = await Create(alice, Body(id));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(id, (await Result(first)).GetProperty("id").GetGuid());

        // The answer was lost, so the outbox sends the same create again.
        using var replay = await Create(alice, Body(id));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var entry = await Result(replay);
        Assert.Equal(id, entry.GetProperty("id").GetGuid());
        Assert.Equal(120, entry.GetProperty("durationMinutes").GetInt32());

        Assert.Equal(1, await Count(db => db.TimeEntries));
        Assert.Equal(1, await Count(db => db.Audits.Where(x => x.EntityType == "entry" && x.Action == "created")));
        Assert.Equal(1, await Count(db => db.OutboxEvents));
    }

    [Fact]
    public async Task A_replay_changes_nothing_even_if_the_queued_body_differs()
    {
        var id = Guid.NewGuid();
        (await Create(alice, Body(id, "First"))).Dispose();
        using var replay = await Create(alice, Body(id, "Second", "13:00:00", "15:00:00"));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("First", (await Result(replay)).GetProperty("taskName").GetString());
        Assert.Equal(1, await Count(db => db.TimeEntries));
    }

    [Fact]
    public async Task A_replay_after_the_month_closed_or_the_entry_was_deleted_is_still_a_success()
    {
        var id = Guid.NewGuid();
        (await Create(alice, Body(id))).Dispose();
        await using (var scope = Scope(tenantA))
        {
            var db = scope.ServiceProvider.GetRequiredService<TimesheetDbContext>();
            db.MonthLocks.Add(new MonthLock { TenantId = tenantA, Year = today.Year, Month = today.Month,
                LockedBy = bob, LockedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        using var closed = await Create(alice, Body(id));
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        // A new entry in the closed month is still refused.
        using var fresh = await Create(alice, Body(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, fresh.StatusCode);
        Assert.Equal("MONTH_CLOSED", await ErrorCode(fresh));
    }

    [Fact]
    public async Task A_replay_after_the_entry_was_deleted_does_not_bring_it_back()
    {
        var id = Guid.NewGuid();
        (await Create(alice, Body(id))).Dispose();
        using (var deleted = await Send(HttpMethod.Delete, $"/api/v1/entries/{id}", alice))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var replay = await Create(alice, Body(id));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(0, (await Result(await Send(HttpMethod.Get,
            $"/api/v1/entries?year={today.Year}&month={today.Month}", alice))).GetArrayLength());
    }

    [Fact]
    public async Task Another_members_or_tenants_id_is_refused_without_a_write()
    {
        var id = Guid.NewGuid();
        (await Create(alice, Body(id))).Dispose();

        using var sameTenant = await Create(bob, Body(id));
        Assert.Equal(HttpStatusCode.Conflict, sameTenant.StatusCode);
        Assert.Equal("ID_IN_USE", await ErrorCode(sameTenant));

        using var otherTenant = await Create(bob, Body(id), tenantB);
        Assert.Equal(HttpStatusCode.Conflict, otherTenant.StatusCode);
        Assert.Equal("ID_IN_USE", await ErrorCode(otherTenant));

        Assert.Equal(1, await Count(db => db.TimeEntries));
        Assert.Equal(0, await Count(db => db.TimeEntries, tenantB));
    }

    [Fact]
    public async Task An_empty_id_is_invalid_and_no_id_still_lets_the_server_choose()
    {
        using var empty = await Create(alice, Body(Guid.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using var unnamed = await Create(alice, Body(null));
        Assert.Equal(HttpStatusCode.Created, unnamed.StatusCode);
        Assert.NotEqual(Guid.Empty, (await Result(unnamed)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Update_ignores_a_body_id_and_context_names_the_account()
    {
        var id = Guid.NewGuid();
        (await Create(alice, Body(id))).Dispose();
        using var updated = await Send(HttpMethod.Put, $"/api/v1/entries/{id}", alice,
            Body(Guid.NewGuid(), "Changed", "09:00:00", "10:00:00"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var entry = await Result(updated);
        Assert.Equal(id, entry.GetProperty("id").GetGuid());
        Assert.Equal("Changed", entry.GetProperty("taskName").GetString());

        var context = await Result(await Send(HttpMethod.Get, "/api/v1/context", alice));
        Assert.Equal(alice, context.GetProperty("userId").GetGuid());
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
                context.Items[Actor.ContextKey] = new Actor(tenant, user, "Employee", "Asia/Bangkok");
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
        Guid? tenant = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Tenant", (tenant ?? tenantA).ToString());
        request.Headers.Add("X-Test-User", user.ToString());
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Result(HttpResponseMessage response)
    {
        using (response)
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").Clone();
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString();
}
