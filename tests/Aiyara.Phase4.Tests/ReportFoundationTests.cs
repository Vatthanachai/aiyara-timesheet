using Aiyara.Report.Databases;
using Aiyara.Report.Models;
using Aiyara.Report.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aiyara.Phase4.Tests;

public sealed class ReportFoundationTests
{
    [Fact]
    public void Monthly_schedule_uses_tenant_timezone_and_closes_previous_month()
    {
        var next = ReportCalendar.NextFireUtc(ReportKind.Monthly, "Asia/Bangkok",
            new DateTimeOffset(2026, 12, 31, 16, 0, 0, TimeSpan.Zero),
            new TimeOnly(5, 0));
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 17, 15, 0, TimeSpan.Zero), next);
        var period = ReportCalendar.ClosedPeriod(ReportKind.Monthly, "Asia/Bangkok", next);
        Assert.Equal(new DateTimeOffset(2026, 11, 30, 17, 0, 0, TimeSpan.Zero),
            period.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 17, 0, 0, TimeSpan.Zero),
            period.EndUtc);
    }

    [Fact]
    public void Weekly_and_annual_schedules_follow_local_calendar()
    {
        var after = new DateTimeOffset(2026, 10, 4, 16, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 17, 15, 0, TimeSpan.Zero),
            ReportCalendar.NextFireUtc(ReportKind.Weekly, "Asia/Bangkok", after));
        var annual = ReportCalendar.NextFireUtc(ReportKind.Annual, "Asia/Bangkok",
            new DateTimeOffset(2026, 12, 31, 16, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 17, 15, 0, TimeSpan.Zero), annual);
        var period = ReportCalendar.ClosedPeriod(ReportKind.Annual, "Asia/Bangkok", annual);
        Assert.Equal(new DateTimeOffset(2025, 12, 31, 17, 0, 0, TimeSpan.Zero),
            period.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 17, 0, 0, TimeSpan.Zero),
            period.EndUtc);
    }

    [Fact]
    public void Weekly_schedule_tracks_daylight_saving_offset()
    {
        var next = ReportCalendar.NextFireUtc(ReportKind.Weekly, "America/New_York",
            new DateTimeOffset(2026, 3, 8, 6, 0, 0, TimeSpan.Zero), new TimeOnly(2, 30));
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 6, 30, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Snapshot_reader_maps_work_and_leave_rows_and_calculates_document_totals()
    {
        const string snapshot = """{"entries":[{"Date":"2026-10-01","StartTime":"09:00:00","EndTime":"18:00:00","DurationMinutes":540,"TaskName":"ออกแบบ","Detail":"หน้ารายงาน","Notes":"ทดสอบ","ProjectId":"project-1","CategoryId":"category-1"}],"leaves":[{"Date":"2026-10-02","Kind":"Vacation","Notes":"พักร้อน"}]}""";

        var lines = ReportDocumentRenderer.ReadSnapshot(snapshot);
        Assert.Equal(2, lines.Count);
        Assert.Equal("work", lines[0].Kind);
        Assert.Equal("leave", lines[1].Kind);
        Assert.Equal(1, ReportDocumentRenderer.CountLeaveDays(snapshot));
        var xlsx = ReportDocumentRenderer.RenderXlsx(new ReportDocumentData("ทดสอบ", "ทีม", "ตุลาคม",
            lines, ReportDocumentRenderer.CountLeaveDays(snapshot)));
        Assert.Equal((byte)'P', xlsx[0]);
        Assert.Equal((byte)'K', xlsx[1]);
    }

    [Fact]
    public void Pdf_renderer_embeds_thai_document_content()
    {
        var pdf = ReportDocumentRenderer.RenderPdf(new ReportDocumentData("พนักงาน", "ทีม", "ตุลาคม",
            [new ReportLine(new DateOnly(2026, 10, 1), "09:00", "18:00", 480,
                "งานทดสอบ", "รายละเอียด", "หมายเหตุ", "โครงการ", "หมวดหมู่", "work")], 0));
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    public async Task Tenant_filter_fails_closed_and_composite_foreign_key_blocks_cross_tenant_run()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var scope = new ReportingTenantScope();
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new ReportingDbContext(options, scope);
        await db.Database.EnsureCreatedAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var firstDefinition = NewDefinition(first);
        var secondDefinition = NewDefinition(second);
        db.ReportDefinitions.Add(firstDefinition);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        scope.TenantId = first;
        await db.SaveChangesAsync();
        scope.TenantId = second;
        db.ReportDefinitions.Add(secondDefinition);
        await db.SaveChangesAsync();

        scope.TenantId = null;
        Assert.Empty(await db.ReportDefinitions.ToListAsync());
        scope.TenantId = first;
        Assert.Equal(firstDefinition.Id, Assert.Single(await db.ReportDefinitions.ToListAsync()).Id);
        scope.TenantId = second;
        Assert.Equal(secondDefinition.Id, Assert.Single(await db.ReportDefinitions.ToListAsync()).Id);

        scope.TenantId = first;
        db.ReportRuns.Add(new ReportRun
        {
            Id = Guid.NewGuid(), TenantId = first,
            ReportDefinitionId = secondDefinition.Id,
            TimeZoneId = "Asia/Bangkok",
            IdempotencyKey = "cross-tenant-test",
            PeriodStartUtc = DateTime.UtcNow.AddDays(-7),
            PeriodEndUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Report_run_idempotency_key_is_unique_within_a_tenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var tenant = Guid.NewGuid();
        var scope = new ReportingTenantScope { TenantId = tenant };
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new ReportingDbContext(options, scope);
        await db.Database.EnsureCreatedAsync();
        var definition = NewDefinition(tenant);
        db.ReportDefinitions.Add(definition);
        await db.SaveChangesAsync();

        db.ReportRuns.Add(NewRun(tenant, definition.Id, "same-period"));
        await db.SaveChangesAsync();
        db.ReportRuns.Add(NewRun(tenant, definition.Id, "same-period"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Definition_with_a_report_run_cannot_be_deleted()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var tenant = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new ReportingDbContext(options,
            new ReportingTenantScope { TenantId = tenant });
        await db.Database.EnsureCreatedAsync();
        var definition = NewDefinition(tenant);
        db.ReportDefinitions.Add(definition);
        db.ReportRuns.Add(NewRun(tenant, definition.Id, "retained-run"));
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        db.ReportDefinitions.Remove(await db.ReportDefinitions.SingleAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Mismatched_tenant_write_and_snapshot_update_are_rejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var scope = new ReportingTenantScope { TenantId = first };
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new ReportingDbContext(options, scope);
        await db.Database.EnsureCreatedAsync();
        var definition = NewDefinition(first);
        db.ReportDefinitions.Add(definition);
        await db.SaveChangesAsync();
        var run = NewRun(first, definition.Id, "immutable-snapshot");
        var employee = Guid.NewGuid();
        run.SubjectUserId = employee;
        var snapshot = new ReportSnapshot
        {
            Id = Guid.NewGuid(), TenantId = first, ReportRunId = run.Id,
            PayloadJson = "{}", Sha256 = new string('a', 64), CreatedAtUtc = DateTime.UtcNow
        };
        db.ReportRuns.Add(run);
        db.ReportSnapshots.Add(snapshot);
        await db.SaveChangesAsync();
        Assert.Equal(run.Id, (await db.ReportRuns.SingleAsync(x =>
            x.SubjectUserId == employee)).Id);

        snapshot.PayloadJson = "{\"changed\":true}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(snapshot).Reload();
        scope.TenantId = second;
        definition.Name = "Cross-tenant edit";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static ReportDefinition NewDefinition(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId,
        Kind = ReportKind.Monthly, Format = ReportFormat.Pdf,
        Name = "Monthly timesheet", CreatedAtUtc = DateTime.UtcNow
    };

    private static ReportRun NewRun(Guid tenantId, Guid definitionId, string key) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, ReportDefinitionId = definitionId,
        TimeZoneId = "Asia/Bangkok",
        IdempotencyKey = key, PeriodStartUtc = DateTime.UtcNow.AddDays(-30),
        PeriodEndUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
    };
}
