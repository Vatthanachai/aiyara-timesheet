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

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        var scope = new ReportingTenantScope { TenantId = tenantId };
        db = new ReportingDbContext(new DbContextOptionsBuilder<ReportingDbContext>()
            .UseSqlite(connection).Options, scope);
        await db.Database.EnsureCreatedAsync();
        operations = new ReportOperations(db, new ConfigurationBuilder().Build(), new UnusedStorage());

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

    private static JsonElement Result(ServiceResponse response)
    {
        Assert.Equal(200, response.StatusCode);
        var result = response.GetType().GetProperty("Result")!.GetValue(response);
        return JsonSerializer.SerializeToElement(result, Json);
    }

    private sealed class UnusedStorage : IReportObjectStorage
    {
        public Task EnsureBucketAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ReportObjectDownload> GetAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
