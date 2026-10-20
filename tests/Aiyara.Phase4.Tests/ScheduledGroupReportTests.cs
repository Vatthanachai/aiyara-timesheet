using System.Text.Json;
using Aiyara.Report.Api;
using Aiyara.Report.Databases;
using Aiyara.Report.Models;
using Aiyara.Report.Services;
using Aiyara.Report.Worker;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aiyara.Phase4.Tests;

/// <summary>Automatic reports for a member group: the schedule names a group in Identity, and each
/// time it fires the run covers the group's members at that moment.</summary>
public sealed class ScheduledGroupReportTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly Guid tenantId = Guid.NewGuid();
    private readonly Guid adminId = Guid.NewGuid();
    private readonly Guid outsourceId = Guid.NewGuid();
    private readonly Groups groups = new();
    private ServiceProvider provider = null!;
    private ReportDefinition monthly = null!;

    private ReportActor Admin => new(tenantId, adminId, "TenantAdmin", "Asia/Bangkok");

    [Fact]
    public async Task Admins_schedule_a_report_per_group_alongside_the_whole_organization()
    {
        var ada = Guid.NewGuid();
        groups.Set(outsourceId, "Outsource", ada);
        await using var scope = Scope();
        var operations = Operations(scope);

        var organization = await operations.CreateSchedule(Admin, Request(null), default);
        var group = await operations.CreateSchedule(Admin, Request(outsourceId), default);
        var duplicate = await operations.CreateSchedule(Admin, Request(outsourceId), default);
        var unknown = await operations.CreateSchedule(Admin, Request(Guid.NewGuid()), default);
        var noDirectory = await new ReportOperations(Db(scope), new ConfigurationBuilder().Build(), new NoStorage())
            .CreateSchedule(Admin, Request(Guid.NewGuid()), default);

        Assert.Equal((201, 201), (organization.StatusCode, group.StatusCode));
        Assert.Equal((409, "SCHEDULE_EXISTS"), (duplicate.StatusCode, ErrorCode(duplicate)));
        Assert.Equal((400, "GROUP_NOT_FOUND"), (unknown.StatusCode, ErrorCode(unknown)));
        Assert.Equal((503, "GROUPS_UNAVAILABLE"), (noDirectory.StatusCode, ErrorCode(noDirectory)));
        var listed = Result(await operations.Schedules(Admin, default)).EnumerateArray().ToArray();
        Assert.Equal(2, listed.Length);
        Assert.Contains(listed, x => x.GetProperty("subjectGroupName").GetString() == "Outsource" &&
            x.GetProperty("subjectGroupId").GetGuid() == outsourceId);
        Assert.Contains(listed, x => x.GetProperty("subjectGroupId").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task A_disabled_schedule_is_enabled_again_instead_of_failing_as_a_duplicate()
    {
        groups.Set(outsourceId, "Outsource", Guid.NewGuid());
        await using var scope = Scope();
        var operations = Operations(scope);
        var created = await operations.CreateSchedule(Admin, Request(outsourceId), default);
        var id = Result(created, 201).GetProperty("id").GetGuid();
        await operations.DeleteSchedule(Admin, id, default);
        await operations.DeleteSchedule(Admin, Result(await operations.CreateSchedule(Admin, Request(null), default), 201)
            .GetProperty("id").GetGuid(), default);

        var again = await operations.CreateSchedule(Admin, Request(outsourceId, "Asia/Tokyo"), default);
        var organizationAgain = await operations.CreateSchedule(Admin, Request(null), default);

        Assert.Equal((201, 201), (again.StatusCode, organizationAgain.StatusCode));
        Assert.Equal(id, Result(again, 201).GetProperty("id").GetGuid());
        var db = Db(scope);
        db.ChangeTracker.Clear();
        var schedule = await db.ReportSchedules.SingleAsync(x => x.Id == id);
        Assert.True(schedule.IsEnabled);
        Assert.Equal("Asia/Tokyo", schedule.TimeZoneId);
        Assert.Equal(2, await db.ReportSchedules.CountAsync());
    }

    [Fact]
    public async Task A_due_group_schedule_reports_on_the_groups_members_when_it_fires()
    {
        var (ada, ben) = (Guid.NewGuid(), Guid.NewGuid());
        var schedule = await AddDueScheduleAsync(outsourceId, "Old name");
        var organization = await AddDueScheduleAsync(null, null);
        // The group was renamed and gained a member after the schedule was created.
        groups.Set(outsourceId, "Outsource", ben, ada);

        await Job().DispatchDueAsync(default);
        await Job().DispatchDueAsync(default);

        await using var scope = Scope();
        var db = Db(scope);
        var run = await db.ReportRuns.SingleAsync(x => x.IdempotencyKey.StartsWith($"schedule:{schedule.Id:N}:"));
        Assert.Equal("Outsource", run.SubjectGroupName);
        Assert.Equal(new[] { ada, ben }.Order(), JsonSerializer.Deserialize<Guid[]>(run.SubjectGroupMemberIdsJson!));
        Assert.Null(run.SubjectUserId);
        Assert.Equal(new DateTime(2026, 8, 31, 17, 0, 0), run.PeriodStartUtc);
        var stored = await db.ReportSchedules.SingleAsync(x => x.Id == schedule.Id);
        Assert.Equal("Outsource", stored.SubjectGroupName);
        Assert.True(stored.NextFireAtUtc > DateTime.UtcNow);
        // The whole-organization schedule still reports on everyone.
        var everyone = await db.ReportRuns.SingleAsync(x => x.IdempotencyKey.StartsWith($"schedule:{organization.Id:N}:"));
        Assert.Null(everyone.SubjectGroupMemberIdsJson);
    }

    [Fact]
    public async Task Deleted_empty_or_oversized_groups_and_identity_outages_never_produce_a_wrong_report()
    {
        var deleted = await AddDueScheduleAsync(Guid.NewGuid(), "Deleted");
        var emptyId = Guid.NewGuid();
        var empty = await AddDueScheduleAsync(emptyId, "Empty");
        groups.Set(emptyId, "Empty");
        var hugeId = Guid.NewGuid();
        var huge = await AddDueScheduleAsync(hugeId, "Huge");
        groups.Set(hugeId, "Huge", Enumerable.Range(0, ReportGroupLimits.MaxMembers + 1).Select(_ => Guid.NewGuid()).ToArray());
        var unreachableId = Guid.NewGuid();
        var unreachable = await AddDueScheduleAsync(unreachableId, "Unreachable");
        groups.Fail(unreachableId);

        await Job().DispatchDueAsync(default);

        await using var scope = Scope();
        var db = Db(scope);
        Assert.Empty(await db.ReportRuns.ToListAsync());
        var schedules = await db.ReportSchedules.ToDictionaryAsync(x => x.Id);
        Assert.False(schedules[deleted.Id].IsEnabled);
        // Empty and oversized groups wait for their next period; an outage leaves the schedule due.
        Assert.True(schedules[empty.Id].IsEnabled && schedules[empty.Id].NextFireAtUtc > DateTime.UtcNow);
        Assert.True(schedules[huge.Id].NextFireAtUtc > DateTime.UtcNow);
        Assert.True(schedules[unreachable.Id].NextFireAtUtc <= DateTime.UtcNow);
        var actions = await db.Audits.Select(x => x.Action).ToListAsync();
        Assert.Contains("schedule.disabled.group_deleted", actions);
        Assert.Contains("schedule.skipped.empty_group", actions);
        Assert.Contains("schedule.skipped.group_too_large", actions);
    }

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddScoped<ReportingTenantScope>();
        services.AddDbContext<ReportingDbContext>(options => options.UseSqlite(connection));
        provider = services.BuildServiceProvider();
        await using var scope = Scope();
        var db = Db(scope);
        await db.Database.EnsureCreatedAsync();
        monthly = new ReportDefinition { Id = Guid.NewGuid(), TenantId = tenantId, Kind = ReportKind.Monthly,
            Format = ReportFormat.Pdf, Name = "Monthly", CreatedAtUtc = DateTime.UtcNow };
        db.ReportDefinitions.Add(monthly);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await provider.DisposeAsync();
        await connection.DisposeAsync();
    }

    private AsyncServiceScope Scope()
    {
        var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ReportingTenantScope>().TenantId = tenantId;
        return scope;
    }

    private static ReportingDbContext Db(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

    private ReportOperations Operations(AsyncServiceScope scope) =>
        new(Db(scope), new ConfigurationBuilder().Build(), new NoStorage(), groups);

    private ScheduleDispatchJob Job() => new(provider.GetRequiredService<IServiceScopeFactory>(),
        new ConfigurationBuilder().Build(), groups, NullLogger<ScheduleDispatchJob>.Instance);

    private ScheduleRequest Request(Guid? groupId, string zone = "Asia/Bangkok") =>
        new(monthly.Id, zone, new TimeOnly(0, 15), groupId);

    /// <summary>A schedule that fired at 00:15 on 1 October 2026 in Bangkok, for September.</summary>
    private async Task<ReportSchedule> AddDueScheduleAsync(Guid? groupId, string? groupName)
    {
        await using var scope = Scope();
        var db = Db(scope);
        var schedule = new ReportSchedule { Id = Guid.NewGuid(), TenantId = tenantId, ReportDefinitionId = monthly.Id,
            SubjectGroupId = groupId, SubjectGroupName = groupName, TimeZoneId = "Asia/Bangkok",
            NextFireAtUtc = new DateTime(2026, 9, 30, 17, 15, 0) };
        db.ReportSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return schedule;
    }

    private static JsonElement Result(ServiceResponse response, int status = 200)
    {
        Assert.Equal(status, response.StatusCode);
        var result = response.GetType().GetProperty("Result")!.GetValue(response);
        return JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static string? ErrorCode(ServiceResponse response) =>
        response.GetType().GetProperty("ErrorCode")?.GetValue(response) as string;

    private sealed class Groups : IReportGroupDirectory
    {
        private readonly Dictionary<Guid, ReportGroup> known = [];
        private readonly HashSet<Guid> failing = [];

        public void Set(Guid id, string name, params Guid[] members) => known[id] = new ReportGroup(name, members);
        public void Fail(Guid id) => failing.Add(id);

        public Task<ReportGroup?> FindAsync(Guid tenantId, Guid groupId, CancellationToken cancellationToken) =>
            failing.Contains(groupId) ? throw new HttpRequestException("Identity is unreachable.")
                : Task.FromResult(known.GetValueOrDefault(groupId));
    }

    private sealed class NoStorage : IReportObjectStorage
    {
        public Task EnsureBucketAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ReportObjectDownload> GetAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
