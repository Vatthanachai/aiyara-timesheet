using System.Text;
using Aiyara.Report.Api;
using Aiyara.Timesheet.Component.Abstractions.ServiceResponses;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Aiyara.Phase4.Tests;

/// <summary>Reporting's Export PDF: a member's month as the page shows it, rendered by Report with the
/// monthly report layout. Nothing is read or stored, so the operation needs no database or storage.</summary>
public sealed class TimesheetPdfExportTests
{
    private static readonly ReportActor Employee = new(Guid.NewGuid(), Guid.NewGuid(), "Employee", "Asia/Bangkok");
    private static readonly ReportOperations Operations = new(null!, new ConfigurationBuilder().Build(), null!);

    private static TimesheetPdfLine Work(int day, string start, string end, int minutes, string task) =>
        new(new DateOnly(2026, 10, day), start, end, minutes, task, Project: "Apollo", Kind: "work");

    private static TimesheetPdfRequest Month(params TimesheetPdfLine[] lines) =>
        new("สมชาย ใจดี", "October 2026", lines, Department: "Outsource", StandardDayMinutes: 420,
            DateFormat: "dd/MM/bbbb", TimeFormat: "h:mm a");

    [Fact]
    public void Any_member_gets_a_pdf_of_the_month_they_send_with_work_leave_and_days_off()
    {
        var response = Operations.TimesheetPdf(Employee, Month(
            Work(5, "09:00", "12:00", 180, "ออกแบบหน้าจอ"),
            Work(5, "13:00", "18:30", 330, "Pairing"),
            new TimesheetPdfLine(new DateOnly(2026, 10, 9), "", "", 0, "Annual", Kind: "leave"),
            new TimesheetPdfLine(new DateOnly(2026, 10, 13), null, null, 0, "Memorial day", Notes: "Holiday", Kind: "holiday")));

        var file = Assert.IsType<ServiceFileResponse>(response);
        Assert.Equal(200, file.StatusCode);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(file.Content, 0, 5));
        Assert.Contains("Timesheet October 2026", Encoding.Latin1.GetString(file.Content));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("en")]
    [InlineData("th")]
    public void The_pdf_is_made_in_the_language_the_member_chose(string? locale)
    {
        var english = Assert.IsType<ServiceFileResponse>(Operations.TimesheetPdf(Employee, Month(Work(5, "09:00", "12:00", 180, "Review"))));
        var chosen = Assert.IsType<ServiceFileResponse>(Operations.TimesheetPdf(Employee,
            Month(Work(5, "09:00", "12:00", 180, "Review")) with { Locale = locale }));
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(chosen.Content, 0, 5));
        // Thai words need Thai glyphs, so a Thai document embeds a different face subset than an English one.
        if (locale == "th") Assert.NotEqual(english.Content.Length, chosen.Content.Length);
    }

    [Fact]
    public void An_empty_month_still_renders()
    {
        var file = Assert.IsType<ServiceFileResponse>(Operations.TimesheetPdf(Employee, Month()));
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(file.Content, 0, 5));
    }

    public static TheoryData<TimesheetPdfRequest> InvalidRequests() => new()
    {
        Month() with { MemberName = " " },
        Month() with { Period = new string('x', 101) },
        Month() with { StandardDayMinutes = 0 },
        Month() with { Locale = "fr" },
        Month(Work(5, "9:00", "12:00", 180, "Single-digit hour")),
        Month(Work(5, "09:00", "", 180, "No end")),
        Month(Work(5, "09:00", "12:00", 1441, "Longer than a day")),
        Month(Work(5, "09:00", "12:00", 180, new string('x', 201))),
        Month(new TimesheetPdfLine(new DateOnly(2026, 10, 5), "", "", 0, "Party", Kind: "celebration")),
        Month(Enumerable.Range(0, TimesheetPdfRequest.MaxLines + 1)
            .Select(_ => Work(5, "09:00", "10:00", 60, "Many")).ToArray())
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void Invalid_requests_are_refused(TimesheetPdfRequest request)
    {
        var response = Operations.TimesheetPdf(Employee, request);
        var error = Assert.IsType<ServiceErrorResponse>(response);
        Assert.Equal((400, "VALIDATION_FAILED"), (error.StatusCode, error.ErrorCode));
    }
}
