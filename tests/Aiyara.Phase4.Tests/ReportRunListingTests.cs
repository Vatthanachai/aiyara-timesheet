using System.Text.Json;
using System.Text.Json.Serialization;
using Aiyara.Report.Api;
using Aiyara.Report.Databases;
using Aiyara.Report.Models;
using Aiyara.Report.Services;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aiyara.Phase4.Tests;

public sealed class ReportRunListingTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly Guid tenantId = Guid.NewGuid();
    private readonly Guid adminId = Guid.NewGuid();
    private readonly Guid employeeId = Guid.NewGuid();
    private ReportingDbContext db = null!;
    private ReportOperations operations = null!;
    private ReportDefinition monthly = null!;
    private ReportDefinition weekly = null!;
    private ReportRun succeeded = null!;
    private ReportRun blocked = null!;
    private ReportRun failed = null!;

    private ReportActor Admin => new(tenantId, adminId, "TenantAdmin", "Asia/Bangkok");
    private ReportActor Employee => new(tenantId, employeeId, "Employee", "Asia/Bangkok");

    [Fact]
    public async Task Runs_report_reason_codes_completion_and_latest_signed_version()
    {
        var page = await RunsAsync(Admin, new RunQuery(null, null));

        Assert.Equal(4, page.GetProperty("totalCount").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetGuid());
        var ready = items[succeeded.Id];
        Assert.Equal(2, ready.GetProperty("signedVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, ready.GetProperty("statusReason").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, ready.GetProperty("completedAtUtc").ValueKind);
        Assert.Equal(monthly.Id, ready.GetProperty("definitionId").GetGuid());
        var waiting = items[blocked.Id];
        Assert.Equal("Queued", waiting.GetProperty("status").GetString());
        Assert.Equal(ReportRunReasons.SourceMonthNotLockedCode, waiting.GetProperty("statusReason").GetString());
        Assert.Equal("2026-09", waiting.GetProperty("blockedMonth").GetString());
        var broken = items[failed.Id];
        Assert.Equal(ReportRunReasons.GenerationFailedCode, broken.GetProperty("statusReason").GetString());
        // Raw failure text never reaches clients.
        Assert.DoesNotContain("connection string", page.GetRawText());
        Assert.False(broken.TryGetProperty("failureReason", out _));
    }

    [Fact]
    public async Task Runs_filter_by_status_and_definition_and_page_newest_first()
    {
        var queued = await RunsAsync(Admin, new RunQuery(ReportRunStatus.Queued, null));
        var weeklyOnly = await RunsAsync(Admin, new RunQuery(null, weekly.Id));
        var firstPage = await RunsAsync(Admin, new RunQuery(null, null, 1, 3));
        var secondPage = await RunsAsync(Admin, new RunQuery(null, null, 2, 3));
        var beyond = await RunsAsync(Admin, new RunQuery(null, null, 9, 3));

        Assert.Equal(2, queued.GetProperty("totalCount").GetInt32());
        Assert.All(queued.GetProperty("items").EnumerateArray(),
            x => Assert.Equal("Queued", x.GetProperty("status").GetString()));
        Assert.Equal(1, weeklyOnly.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, firstPage.GetProperty("items").GetArrayLength());
        Assert.Equal(failed.Id, firstPage.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(1, secondPage.GetProperty("items").GetArrayLength());
        Assert.Equal(succeeded.Id, secondPage.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(2, beyond.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Runs_filter_by_subject_members_and_leave_out_whole_organization_runs()
    {
        var group = await RunsAsync(Admin, new RunQuery(null, null, SubjectUserIds: [employeeId, Guid.NewGuid()]));
        var nobody = await RunsAsync(Admin, new RunQuery(null, null, SubjectUserIds: [Guid.NewGuid()]));
        var combined = await RunsAsync(Admin, new RunQuery(ReportRunStatus.Queued, null, SubjectUserIds: [employeeId]));
        var tooMany = await operations.Runs(Admin, new RunQuery(null, null,
            SubjectUserIds: Enumerable.Range(0, RunQuery.MaxSubjectUserIds + 1).Select(_ => Guid.NewGuid()).ToArray()), default);
        // An employee's own visibility still applies under a subject filter.
        var employee = await RunsAsync(Employee, new RunQuery(null, null, SubjectUserIds: [adminId]));

        Assert.Equal(1, group.GetProperty("totalCount").GetInt32());
        Assert.Equal(succeeded.Id, group.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(0, nobody.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, combined.GetProperty("totalCount").GetInt32());
        Assert.Equal((400, "VALIDATION_FAILED"), (tooMany.StatusCode, ErrorCode(tooMany)));
        Assert.Equal(0, employee.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Admins_request_a_member_group_report_once_per_group_and_period()
    {
        var (ada, ben) = (Guid.NewGuid(), Guid.NewGuid());
        RunRequest Request(string? name, params Guid[] members) => new(monthly.Id,
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            null, name, members);

        var created = await operations.CreateRun(Admin, Request(" Outsource ", ben, ada, ada), default);
        var again = await operations.CreateRun(Admin, Request("Outsource", ada, ben), default);
        var otherMembers = await operations.CreateRun(Admin, Request("Outsource", ada), default);

        Assert.Equal(202, created.StatusCode);
        db.ChangeTracker.Clear();
        var run = await db.ReportRuns.SingleAsync(x => x.SubjectGroupName != null && x.SubjectGroupMemberIdsJson!.Contains(ben.ToString()));
        Assert.Equal("Outsource", run.SubjectGroupName);
        Assert.Null(run.SubjectUserId);
        Assert.Equal(new[] { ada, ben }.Order(), JsonSerializer.Deserialize<Guid[]>(run.SubjectGroupMemberIdsJson!));
        // The same group and period is the same report; a different membership is a new one.
        Assert.Equal(run.Id, Result(again, 202).GetProperty("id").GetGuid());
        Assert.NotEqual(run.Id, Result(otherMembers, 202).GetProperty("id").GetGuid());
        var listed = (await RunsAsync(Admin, new RunQuery(null, null))).GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == run.Id);
        Assert.Equal("Outsource", listed.GetProperty("subjectGroupName").GetString());
        Assert.Contains("\"SubjectGroupName\":\"Outsource\"", (await db.Audits.FirstAsync(x => x.ReportRunId == run.Id)).DetailsJson);
    }

    [Fact]
    public async Task Group_reports_are_for_admins_with_a_named_group_of_one_to_five_hundred_members()
    {
        var period = (Start: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), End: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        async Task<(int, string?)> Create(ReportActor actor, Guid? subject, string? name, Guid[]? members)
        {
            var response = await operations.CreateRun(actor, new RunRequest(monthly.Id, period.Start, period.End,
                subject, name, members), default);
            return (response.StatusCode, ErrorCode(response));
        }

        Assert.Equal((403, "FORBIDDEN"), await Create(Employee, null, "Outsource", [employeeId]));
        Assert.Equal((400, "VALIDATION_FAILED"), await Create(Admin, employeeId, "Outsource", [employeeId]));
        Assert.Equal((400, "VALIDATION_FAILED"), await Create(Admin, null, "  ", [employeeId]));
        Assert.Equal((400, "VALIDATION_FAILED"), await Create(Admin, null, new string('x', 101), [employeeId]));
        Assert.Equal((400, "VALIDATION_FAILED"), await Create(Admin, null, "Outsource", []));
        Assert.Equal((400, "VALIDATION_FAILED"), await Create(Admin, null, "Outsource", null));
        Assert.Equal((400, "VALIDATION_FAILED"), await Create(Admin, null, "Outsource",
            Enumerable.Range(0, RunQuery.MaxSubjectUserIds + 1).Select(_ => Guid.NewGuid()).ToArray()));
        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.ReportRuns.CountAsync(x => x.SubjectGroupName != null));
    }

    [Fact]
    public async Task Regenerating_a_finished_run_queues_a_new_run_with_the_same_report_and_subject()
    {
        var first = await operations.Regenerate(Admin, succeeded.Id, default);
        var second = await operations.Regenerate(Admin, succeeded.Id, default);
        var retried = await operations.Regenerate(Admin, failed.Id, default);

        Assert.Equal((202, 202, 202), (first.StatusCode, second.StatusCode, retried.StatusCode));
        db.ChangeTracker.Clear();
        var copyId = Result(first, 202).GetProperty("id").GetGuid();
        var copy = await db.ReportRuns.SingleAsync(x => x.Id == copyId);
        Assert.Equal((succeeded.ReportDefinitionId, succeeded.SubjectUserId, succeeded.PeriodStartUtc, succeeded.PeriodEndUtc),
            (copy.ReportDefinitionId, copy.SubjectUserId, copy.PeriodStartUtc, copy.PeriodEndUtc));
        Assert.Equal((ReportRunStatus.Queued, adminId), (copy.Status, copy.RequestedBy));
        // Every regeneration is its own run; the original stays as it was.
        Assert.NotEqual(copyId, Result(second, 202).GetProperty("id").GetGuid());
        Assert.Equal(ReportRunStatus.Succeeded, (await db.ReportRuns.SingleAsync(x => x.Id == succeeded.Id)).Status);
        Assert.Equal(3, await db.Audits.CountAsync(x => x.Action == "run.regenerated"));
        Assert.Contains(succeeded.Id.ToString(), (await db.Audits.SingleAsync(x => x.ReportRunId == copyId)).DetailsJson);
    }

    [Fact]
    public async Task Regenerating_keeps_a_group_snapshot_and_refuses_unfinished_invisible_or_disabled_runs()
    {
        var group = Run(weekly, ReportRunStatus.Succeeded, null, DateTime.UtcNow, null);
        group.SubjectGroupName = "Outsource";
        group.SubjectGroupMemberIdsJson = JsonSerializer.Serialize(new[] { employeeId });
        db.ReportRuns.Add(group);
        await db.SaveChangesAsync();

        var regenerated = await operations.Regenerate(Admin, group.Id, default);
        var queued = await operations.Regenerate(Admin, blocked.Id, default);
        var unknown = await operations.Regenerate(Admin, Guid.NewGuid(), default);
        var notTheirs = await operations.Regenerate(Employee, failed.Id, default);
        var own = await operations.Regenerate(Employee, succeeded.Id, default);
        await db.ReportDefinitions.Where(x => x.Id == monthly.Id).ExecuteUpdateAsync(x => x.SetProperty(d => d.IsEnabled, false));
        var disabled = await operations.Regenerate(Admin, failed.Id, default);

        db.ChangeTracker.Clear();
        var copy = await db.ReportRuns.SingleAsync(x => x.Id == Result(regenerated, 202).GetProperty("id").GetGuid());
        Assert.Equal(("Outsource", group.SubjectGroupMemberIdsJson), (copy.SubjectGroupName, copy.SubjectGroupMemberIdsJson));
        Assert.Equal((409, "RUN_IN_PROGRESS"), (queued.StatusCode, ErrorCode(queued)));
        Assert.Equal((404, "NOT_FOUND"), (unknown.StatusCode, ErrorCode(unknown)));
        Assert.Equal((404, "NOT_FOUND"), (notTheirs.StatusCode, ErrorCode(notTheirs)));
        Assert.Equal(202, own.StatusCode);
        Assert.Equal((404, "NOT_FOUND"), (disabled.StatusCode, ErrorCode(disabled)));
    }

    [Fact]
    public async Task Employees_see_only_their_own_runs()
    {
        var page = await RunsAsync(Employee, new RunQuery(null, null));

        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(employeeId, page.GetProperty("items")[0].GetProperty("subjectUserId").GetGuid());
    }

    [Fact]
    public async Task Definitions_and_schedules_list_only_enabled_rows_and_dashboard_counts_blocked_runs()
    {
        var definitions = Result(await operations.Definitions(Admin, default));
        var schedules = Result(await operations.Schedules(Admin, default));
        var dashboard = Result(await operations.Dashboard(Admin, default));

        Assert.Equal(new HashSet<Guid> { weekly.Id, monthly.Id },
            definitions.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToHashSet());
        Assert.Single(schedules.EnumerateArray());
        Assert.Equal(1, dashboard.GetProperty("blocked").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("queued").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("ready").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task Creating_or_renaming_into_an_existing_kind_and_format_is_a_conflict_not_a_server_error()
    {
        var duplicate = await operations.CreateDefinition(Admin,
            new DefinitionRequest(ReportKind.Monthly, ReportFormat.Pdf, "Another monthly"), default);
        var collision = await operations.UpdateDefinition(Admin, weekly.Id,
            new DefinitionRequest(ReportKind.Monthly, ReportFormat.Pdf, "Weekly renamed"), default);

        Assert.Equal((409, "DEFINITION_EXISTS"), (duplicate.StatusCode, ErrorCode(duplicate)));
        Assert.Equal((409, "DEFINITION_EXISTS"), (collision.StatusCode, ErrorCode(collision)));
        db.ChangeTracker.Clear();
        Assert.Equal(3, await db.ReportDefinitions.CountAsync());
        Assert.Equal(ReportKind.Weekly, (await db.ReportDefinitions.SingleAsync(x => x.Id == weekly.Id)).Kind);
        Assert.Empty(await db.Audits.ToListAsync());
    }

    [Fact]
    public async Task Creating_a_disabled_kind_and_format_enables_it_again_under_the_new_name()
    {
        var restored = await operations.CreateDefinition(Admin,
            new DefinitionRequest(ReportKind.Annual, ReportFormat.Pdf, " Annual summary "), default);

        Assert.Equal(200, restored.StatusCode);
        db.ChangeTracker.Clear();
        var annual = await db.ReportDefinitions.SingleAsync(x => x.Kind == ReportKind.Annual);
        Assert.True(annual.IsEnabled);
        Assert.Equal("Annual summary", annual.Name);
        Assert.Equal(annual.Id, Result(restored).GetProperty("id").GetGuid());
        Assert.Equal("definition.reenabled", (await db.Audits.SingleAsync()).Action);
    }

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var scope = new ReportingTenantScope { TenantId = tenantId };
        db = new ReportingDbContext(new DbContextOptionsBuilder<ReportingDbContext>()
            .UseSqlite(connection).Options, scope);
        await db.Database.EnsureCreatedAsync();
        // Publishing a requested run fails fast against a host that never resolves; recovery would republish it.
        operations = new ReportOperations(db, new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["RabbitMQ:Host"] = "rabbitmq.invalid" }).Build(), new UnusedStorage());

        var now = DateTime.UtcNow;
        monthly = Definition(ReportKind.Monthly, "Monthly PDF");
        weekly = Definition(ReportKind.Weekly, "Weekly PDF");
        var retired = Definition(ReportKind.Annual, "Retired");
        retired.IsEnabled = false;
        db.ReportDefinitions.AddRange(monthly, weekly, retired);
        await db.SaveChangesAsync();

        succeeded = Run(monthly, ReportRunStatus.Succeeded, null, now.AddMinutes(-40), employeeId);
        succeeded.CompletedAtUtc = now.AddMinutes(-39);
        blocked = Run(monthly, ReportRunStatus.Queued, ReportRunReasons.SourceMonthNotLocked(new DateOnly(2026, 9, 1)),
            now.AddMinutes(-30), null);
        var queued = Run(weekly, ReportRunStatus.Queued, null, now.AddMinutes(-20), null);
        failed = Run(monthly, ReportRunStatus.Failed, "Npgsql connection string invalid", now.AddMinutes(-10), null);
        db.ReportRuns.AddRange(succeeded, blocked, queued, failed);
        await db.SaveChangesAsync();
        foreach (var version in new[] { 1, 2 })
            db.ReportObjects.Add(new ReportObject { TenantId = tenantId, ReportRunId = succeeded.Id,
                ObjectKey = $"signed/v{version}.pdf", Sha256 = "x", ContentType = "application/pdf",
                Version = version, IsExternallySigned = true, CreatedAtUtc = now, RetainUntilUtc = now.AddYears(7) });
        db.ReportSchedules.Add(new ReportSchedule { TenantId = tenantId, ReportDefinitionId = monthly.Id,
            TimeZoneId = "Asia/Bangkok" });
        db.ReportSchedules.Add(new ReportSchedule { TenantId = tenantId, ReportDefinitionId = weekly.Id,
            TimeZoneId = "Asia/Bangkok", IsEnabled = false });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    private ReportDefinition Definition(ReportKind kind, string name) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, Kind = kind, Format = ReportFormat.Pdf, Name = name,
        CreatedAtUtc = DateTime.UtcNow
    };

    private ReportRun Run(ReportDefinition definition, ReportRunStatus status, string? reason,
        DateTime createdAtUtc, Guid? subject) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, ReportDefinitionId = definition.Id, SubjectUserId = subject,
        RequestedBy = adminId, TimeZoneId = "Asia/Bangkok", IdempotencyKey = Guid.NewGuid().ToString(),
        Status = status, FailureReason = reason, PeriodStartUtc = new DateTime(2026, 9, 1),
        PeriodEndUtc = new DateTime(2026, 10, 1), CreatedAtUtc = createdAtUtc
    };

    private async Task<JsonElement> RunsAsync(ReportActor actor, RunQuery query) =>
        Result(await operations.Runs(actor, query, default));

    private static JsonElement Result(ServiceResponse response, int status = 200)
    {
        Assert.Equal(status, response.StatusCode);
        var result = response.GetType().GetProperty("Result")!.GetValue(response);
        return JsonSerializer.SerializeToElement(result, Json);
    }

    private static string? ErrorCode(ServiceResponse response) =>
        response.GetType().GetProperty("ErrorCode")?.GetValue(response) as string;

    private sealed class UnusedStorage : IReportObjectStorage
    {
        public Task EnsureBucketAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ReportObjectDownload> GetAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
