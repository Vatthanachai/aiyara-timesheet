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
    public void Part_day_leave_counts_against_the_snapshots_working_day()
    {
        // 08:30–17:30 less an hour's break is an 8-hour day: 240 minutes is half of it.
        const string snapshot = """{"entries":[],"leaves":[{"Date":"2026-10-02","Kind":"Annual","Minutes":240},{"Date":"2026-10-05","Kind":"Sick","Minutes":null}],"workHours":{"startTime":"08:30","endTime":"17:30","breakStart":"12:00","breakEnd":"13:00"}}""";

        Assert.Equal(1.5m, ReportDocumentRenderer.CountLeaveDays(snapshot));
        Assert.Equal([240, 0], ReportDocumentRenderer.ReadSnapshot(snapshot).Select(x => x.DurationMinutes));
        Assert.Equal(1m, ReportDocumentRenderer.LeaveDays(null, 480));
        Assert.Equal(0.33m, ReportDocumentRenderer.LeaveDays(160, 480));
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

    [Theory]
    [InlineData("""{"workHours":{"startTime":"08:30","endTime":"17:30","breakStart":"11:30","breakEnd":"12:30"}}""", 480)]
    [InlineData("""{"workHours":{"startTime":"08:00","endTime":"17:00","breakStart":null,"breakEnd":null}}""", 540)]
    [InlineData("""{"workHours":{"startTime":"09:00","endTime":"17:00"}}""", 480)]
    [InlineData("""{"entries":[]}""", null)]
    public void A_normal_working_day_is_the_hours_less_the_break(string snapshot, int? expected) =>
        Assert.Equal(expected, ReportDocumentRenderer.ReadStandardDayMinutes(snapshot));

    [Fact]
    public void Overtime_days_are_days_with_more_work_than_a_normal_working_day()
    {
        ReportLine Work(int day, int minutes) => new(new DateOnly(2026, 10, day), "08:00", "18:00", minutes, "Work", "", "", "", "", "work");
        var lines = new[] { Work(1, 450), Work(2, 480), Work(3, 500) };
        int Overtime(ReportDocumentData data)
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(ReportDocumentRenderer.RenderXlsx(data)));
            return workbook.Worksheet(1).Cell(6, 6).GetValue<int>();
        }

        // A 7.5-hour day (09:00-17:00 less a 30-minute break) makes 2 and 3 October overtime.
        Assert.Equal(2, Overtime(new ReportDocumentData("Ada", "", "October", lines, 0, StandardDayMinutes: 450)));
        Assert.Equal(1, Overtime(new ReportDocumentData("Ada", "", "October", lines, 0)));
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
    public void Pdf_renderer_wraps_long_unspaced_text_over_several_pages()
    {
        var lines = Enumerable.Range(1, 60).Select(day => new ReportLine(new DateOnly(2026, 10, 1 + day % 28), "08:30", "12:00", 210,
            string.Concat(Enumerable.Repeat("ศึกษาโครงสร้างและรูปแบบการเขียนโค้ด", 6)), new string('x', 400), string.Concat(Enumerable.Repeat("หมายเหตุยาวมาก", 20)),
            "โครงการ", "หมวดหมู่", "work")).ToArray();
        var pdf = ReportDocumentRenderer.RenderPdf(new ReportDocumentData("พนักงาน", "ทีม", "ตุลาคม", lines, 0));
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Theory]
    [InlineData("Sarabun-Regular.ttf")]
    [InlineData("Sarabun-Bold.ttf")]
    public void Pdf_fonts_cover_the_thai_and_latin_text_a_report_draws(string file)
    {
        // The PDF library draws a character the font lacks as a blank, so the shipped faces must hold every one.
        var covered = CmapCodePoints(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", file)));
        var needed = Enumerable.Range('A', 26).Concat(Enumerable.Range('a', 26)).Concat(Enumerable.Range('0', 10))
            .Concat(" .,:;-/()&'\"·%+".Select(x => (int)x))
            .Concat(Enumerable.Range(0x0E01, 0x0E3A - 0x0E01 + 1)).Concat(Enumerable.Range(0x0E3F, 0x0E5B - 0x0E3F + 1));
        Assert.Empty(needed.Where(x => !covered.Contains(x)).Select(x => $"U+{x:X4}"));
    }

    private static HashSet<int> CmapCodePoints(byte[] font)
    {
        ushort U16(int at) => (ushort)(font[at] << 8 | font[at + 1]);
        uint U32(int at) => (uint)(U16(at) << 16 | U16(at + 2));
        var table = Enumerable.Range(0, U16(4)).Select(i => 12 + 16 * i)
            .Single(at => System.Text.Encoding.ASCII.GetString(font, at, 4) == "cmap");
        var cmap = (int)U32(table + 8);
        var points = new HashSet<int>();
        for (var i = 0; i < U16(cmap + 2); i++)
        {
            var sub = cmap + (int)U32(cmap + 4 + 8 * i + 4);
            switch (U16(sub))
            {
                case 4:
                    var segments = U16(sub + 6) / 2;
                    for (var s = 0; s < segments; s++)
                    {
                        var end = U16(sub + 14 + 2 * s);
                        var start = U16(sub + 16 + 2 * segments + 2 * s);
                        for (var c = start; c <= end && c < 0xFFFF; c++) points.Add(c);
                    }
                    break;
                case 12:
                    for (var g = 0; g < U32(sub + 12); g++)
                        for (var c = U32(sub + 16 + 12 * g); c <= U32(sub + 20 + 12 * g); c++) points.Add((int)c);
                    break;
            }
        }
        return points;
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
