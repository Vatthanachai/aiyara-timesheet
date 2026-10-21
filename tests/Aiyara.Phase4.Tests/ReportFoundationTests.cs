using Aiyara.Report.Databases;
using Aiyara.Report.Models;
using Aiyara.Report.Services;
using Aiyara.Report.Worker;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aiyara.Phase4.Tests;

public sealed class ReportFoundationTests
{
    [Fact]
    public async Task Report_ready_notification_retries_transient_failure_and_sends_internal_key()
    {
        var attempts = 0;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            attempts++;
            Assert.Equal("test-internal-key", request.Headers.GetValues("X-Internal-Key").Single());
            return Task.FromResult(new HttpResponseMessage(attempts == 1
                ? System.Net.HttpStatusCode.ServiceUnavailable
                : System.Net.HttpStatusCode.Accepted));
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://notifications") };
        var sender = new ReportReadyNotificationSender(client, "test-internal-key", TimeSpan.Zero);

        var sent = await sender.SendAsync(new ReportReadyNotification("employee@example.test",
            "Test Employee", "2026-10", "https://reports.example.test", Guid.NewGuid()));

        Assert.True(sent);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Report_ready_notification_does_not_retry_permanent_failure()
    {
        var attempts = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest));
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://notifications") };
        var sender = new ReportReadyNotificationSender(client, "test-internal-key", TimeSpan.Zero);

        var sent = await sender.SendAsync(new ReportReadyNotification("employee@example.test",
            "Test Employee", "2026-10", "https://reports.example.test", Guid.NewGuid()));

        Assert.False(sent);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Report_ready_notification_stops_after_three_transient_failures()
    {
        var attempts = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://notifications") };
        var sender = new ReportReadyNotificationSender(client, "test-internal-key", TimeSpan.Zero);

        var sent = await sender.SendAsync(new ReportReadyNotification("employee@example.test",
            "Test Employee", "2026-10", "https://reports.example.test", Guid.NewGuid()));

        Assert.False(sent);
        Assert.Equal(3, attempts);
    }

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
    public void Days_off_are_the_months_holidays_and_weekdays_outside_the_work_week()
    {
        // A Sunday-to-Thursday organization: Friday and Saturday are off, and 13 October is a holiday.
        const string snapshot = """{"entries":[],"leaves":[],"holidays":[{"Date":"2026-10-13","Name":"Memorial day"},{"Date":"2026-11-02","Name":"Next month"}],"workingDays":[0,1,2,3,4]}""";
        // Closed before work weeks existed: Saturday and Sunday are off.
        const string older = """{"entries":[],"leaves":[],"holidays":[]}""";

        var days = ReportDocumentRenderer.ReadDaysOff(snapshot, 2026, 10);
        var defaults = ReportDocumentRenderer.ReadDaysOff(older, 2026, 10);

        Assert.Equal(new DateOnly(2026, 10, 2), days[0].Date);
        Assert.All(days, x => Assert.True(ReportDocumentRenderer.IsDayOff(x)));
        Assert.Equal(5 + 5 + 1, days.Count);
        var holiday = Assert.Single(days, x => x.Kind == "holiday");
        Assert.Equal((new DateOnly(2026, 10, 13), "Memorial day", "Holiday"), (holiday.Date, holiday.Task, holiday.Notes));
        Assert.All(days.Where(x => x.Kind == "dayoff"), x =>
            Assert.Contains(x.Date.DayOfWeek, new[] { DayOfWeek.Friday, DayOfWeek.Saturday }));
        Assert.Equal(9, defaults.Count);
        Assert.All(defaults, x => Assert.Contains(x.Date.DayOfWeek, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }));

        var work = new ReportLine(new DateOnly(2026, 10, 1), "09:00", "17:00", 480, "Review", "", "", "", "", "work");
        var data = new ReportDocumentData("Ada", "", "October", [work, .. days], 0);
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(ReportDocumentRenderer.RenderXlsx(data)));
        var sheet = string.Concat(archive.Entries.Select(entry =>
        {
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }));
        Assert.Contains(">Days off<", sheet);
        Assert.Contains("Memorial day", sheet);
        Assert.Contains("Weekly day off", sheet);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(ReportDocumentRenderer.RenderPdf(data), 0, 5));
    }

    [Fact]
    public void Xlsx_lists_rows_in_an_excel_table_with_each_date_on_its_days_first_row()
    {
        ReportLine Work(int day, string start, string end, int minutes, string task) =>
            new(new DateOnly(2026, 10, day), start, end, minutes, task, "", "", "", "", "work");
        // Out of order on purpose: the document sorts by date and time.
        var lines = new[]
        {
            Work(1, "13:00", "14:54", 114, "Overview"), Work(1, "08:30", "12:00", 210, "Onboarding"),
            Work(2, "08:30", "18:00", 570, "Study"),
            new ReportLine(new DateOnly(2026, 10, 3), "", "", 0, "Day off", "", "Weekly day off", "", "", "dayoff")
        };

        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(
            ReportDocumentRenderer.RenderXlsx(new ReportDocumentData("Ada", "", "October", lines, 0))));
        var sheet = workbook.Worksheet(1);
        var table = Assert.Single(sheet.Tables);

        Assert.Equal("A7:I11", table.RangeAddress.ToString());
        Assert.True(table.ShowAutoFilter);
        // The organization's default date format.
        Assert.Equal(["01/10/2026", "", "02/10/2026", "03/10/2026"],
            Enumerable.Range(8, 4).Select(row => sheet.Cell(row, 1).GetString()));
        Assert.Equal(["08:30", "13:00"], new[] { sheet.Cell(8, 2).GetString(), sheet.Cell(9, 2).GetString() });
        Assert.Equal("Overview", sheet.Cell(9, 5).GetString());

        // An empty period still gets a valid table: a header and one blank row.
        using var empty = new ClosedXML.Excel.XLWorkbook(new MemoryStream(
            ReportDocumentRenderer.RenderXlsx(new ReportDocumentData("Ada", "", "October", [], 0))));
        Assert.Equal("A7:I8", Assert.Single(empty.Worksheet(1).Tables).RangeAddress.ToString());
    }

    [Theory]
    [InlineData("dd/MM/yyyy", "HH:mm", "05/10/2026", "13:30", "05/10/2026 – 31/10/2026")]
    [InlineData("dd/MM/bbbb", "h:mm a", "05/10/2569", "1:30 PM", "05/10/2569 – 31/10/2569")]
    [InlineData("d MMM yyyy", "h:mm a", "5 Oct 2026", "1:30 PM", "5 Oct 2026 – 31 Oct 2026")]
    [InlineData("yyyy-MM-dd", "HH:mm", "2026-10-05", "13:30", "2026-10-05 – 2026-10-31")]
    [InlineData("MM/dd/yyyy", "HH:mm", "10/05/2026", "13:30", "10/05/2026 – 10/31/2026")]
    [InlineData("unknown", "unknown", "05/10/2026", "13:30", "05/10/2026 – 31/10/2026")]
    public void Documents_write_dates_and_times_in_the_organizations_formats(string dateFormat, string timeFormat,
        string date, string time, string period)
    {
        var formats = new ReportFormats(dateFormat, timeFormat);
        var day = new DateOnly(2026, 10, 5);
        Assert.Equal(date, formats.Date(day));
        Assert.Equal(time, formats.Time("13:30"));
        Assert.Equal("", formats.Time(""));
        Assert.Equal(period, formats.Period(day, new DateOnly(2026, 10, 31)));

        var data = new ReportDocumentData("Ada", "", formats.Period(day, new DateOnly(2026, 10, 31)),
            [new ReportLine(day, "13:30", "14:00", 30, "Review", "", "", "", "", "work")], 0, formats);
        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(ReportDocumentRenderer.RenderXlsx(data)));
        var sheet = workbook.Worksheet(1);
        Assert.Equal((date, time, period), (sheet.Cell(8, 1).GetString(), sheet.Cell(8, 2).GetString(), sheet.Cell(4, 2).GetString()));
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(ReportDocumentRenderer.RenderPdf(data), 0, 5));
    }

    [Fact]
    public void Documents_show_a_member_column_only_when_rows_name_members()
    {
        ReportLine Line(string member) => new(new DateOnly(2026, 10, 1), "09:00", "10:00", 60,
            "Review", "", "", "", "", "work", member);
        static string Sheet(byte[] xlsx)
        {
            using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(xlsx));
            return string.Concat(archive.Entries.Select(entry =>
            {
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            }));
        }

        var group = Sheet(ReportDocumentRenderer.RenderXlsx(new ReportDocumentData("Group summary (2 members)",
            "Outsource", "October", [Line("Ada Lovelace"), Line("สมชาย ใจดี")], 0)));
        var single = Sheet(ReportDocumentRenderer.RenderXlsx(new ReportDocumentData("Ada", "", "October", [Line("")], 0)));
        var pdf = ReportDocumentRenderer.RenderPdf(new ReportDocumentData("Group summary (2 members)",
            "Outsource", "October", [Line("Ada Lovelace"), Line("สมชาย ใจดี")], 0));

        Assert.Contains(">Member<", group);
        Assert.Contains("สมชาย ใจดี", group);
        Assert.DoesNotContain(">Member<", single);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
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

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private static ReportRun NewRun(Guid tenantId, Guid definitionId, string key) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, ReportDefinitionId = definitionId,
        TimeZoneId = "Asia/Bangkok",
        IdempotencyKey = key, PeriodStartUtc = DateTime.UtcNow.AddDays(-30),
        PeriodEndUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
    };
}
