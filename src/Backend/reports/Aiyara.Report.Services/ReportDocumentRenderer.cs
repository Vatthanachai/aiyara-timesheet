using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using PdfSharp.Fonts;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Aiyara.Report.Services;

/// <summary>One work, leave, or day-off row (<see cref="ReportDocumentRenderer.IsDayOff"/>). Member names
/// whose row it is in a report about several people (a member group); documents show a Member column
/// only when some row has one. Day-off rows belong to the organization and name no member.</summary>
public sealed record ReportLine(DateOnly Date, string Start, string End, int DurationMinutes,
    string Task, string Detail, string Notes, string Project, string Category, string Kind, string Member = "");

public sealed record ReportDocumentData(string EmployeeName, string Department, string Period,
    IReadOnlyList<ReportLine> Lines, int LeaveDays);

public static class ReportDocumentRenderer
{
    /// <summary>The weekdays an organization works when its snapshot predates work weeks.</summary>
    private static readonly int[] DefaultWorkingDays = [1, 2, 3, 4, 5];

    public static bool IsDayOff(ReportLine line) => line.Kind is "holiday" or "dayoff";

    private static int DaysOff(ReportDocumentData data) => data.Lines.Where(IsDayOff).Select(x => x.Date).Distinct().Count();

    public static byte[] RenderXlsx(ReportDocumentData data)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Timesheet");
        sheet.Cell(1, 1).Value = "Timesheet report";
        sheet.Range(1, 1, 1, 9).Merge().Style.Font.Bold = true;
        sheet.Cell(2, 1).Value = "Employee"; sheet.Cell(2, 2).Value = data.EmployeeName;
        sheet.Cell(3, 1).Value = "Department"; sheet.Cell(3, 2).Value = data.Department;
        sheet.Cell(4, 1).Value = "Period"; sheet.Cell(4, 2).Value = data.Period;
        sheet.Cell(5, 1).Value = "Leave days"; sheet.Cell(5, 2).Value = data.LeaveDays;
        sheet.Cell(5, 3).Value = "Days off"; sheet.Cell(5, 4).Value = DaysOff(data);
        sheet.Cell(6, 1).Value = "Worked days"; sheet.Cell(6, 2).Value = data.Lines.Where(x => x.Kind == "work").Select(x => x.Date).Distinct().Count();
        sheet.Cell(6, 3).Value = "Work hours"; sheet.Cell(6, 4).Value = Math.Round(data.Lines.Where(x => x.Kind == "work").Sum(x => x.DurationMinutes) / 60m, 2);
        sheet.Cell(6, 5).Value = "Overtime days"; sheet.Cell(6, 6).Value = data.Lines.Where(x => x.Kind == "work")
            .GroupBy(x => x.Date).Count(group => group.Sum(x => x.DurationMinutes) > 480);
        var showMember = data.Lines.Any(x => x.Member.Length > 0);
        string[] headers = showMember
            ? ["Date", "Member", "Start", "End", "Hours", "Task", "Detail", "Notes", "Project", "Category"]
            : ["Date", "Start", "End", "Hours", "Task", "Detail", "Notes", "Project", "Category"];
        for (var column = 0; column < headers.Length; column++) sheet.Cell(7, column + 1).Value = headers[column];
        var header = sheet.Range(7, 1, 7, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DBEAFE");
        var row = 8;
        foreach (var line in data.Lines)
        {
            var column = 1;
            sheet.Cell(row, column++).Value = line.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (showMember) sheet.Cell(row, column++).Value = line.Member;
            sheet.Cell(row, column++).Value = line.Start;
            sheet.Cell(row, column++).Value = line.End;
            if (IsDayOff(line)) column++;
            else sheet.Cell(row, column).Value = Math.Round(line.DurationMinutes / 60m, 2);
            column++;
            sheet.Cell(row, column++).Value = line.Task;
            sheet.Cell(row, column++).Value = line.Detail;
            sheet.Cell(row, column++).Value = line.Notes;
            sheet.Cell(row, column++).Value = line.Project;
            sheet.Cell(row, column).Value = line.Category;
            if (IsDayOff(line)) sheet.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF0F4");
            row++;
        }
        sheet.Columns().AdjustToContents(8, 48);
        sheet.SheetView.FreezeRows(7);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] RenderPdf(ReportDocumentData data)
    {
        ConfigureFont();
        var document = new PdfDocument();
        document.Info.Title = $"Timesheet {data.Period}";
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        var gfx = XGraphics.FromPdfPage(page);
        var title = new XFont("Noto Sans Thai", 15, XFontStyleEx.Bold);
        var body = new XFont("Noto Sans Thai", 8, XFontStyleEx.Regular);
        var bold = new XFont("Noto Sans Thai", 8, XFontStyleEx.Bold);
        var blue = new XSolidBrush(XColor.FromArgb(57, 111, 190));
        var pale = new XSolidBrush(XColor.FromArgb(224, 234, 250));
        var dayOff = new XSolidBrush(XColor.FromArgb(232, 234, 238));
        const double left = 35, right = 560;
        gfx.DrawString($"Timesheet report · {data.Period}", title, XBrushes.Black,
            new XRect(left, 30, right - left, 24), XStringFormats.TopCenter);
        gfx.DrawString($"Employee: {data.EmployeeName}    Department: {data.Department}", body,
            XBrushes.Black, left, 76);
        var workLines = data.Lines.Where(x => x.Kind == "work").ToArray();
        var workedDays = workLines.Select(x => x.Date).Distinct().Count();
        var hours = workLines.Sum(x => x.DurationMinutes) / 60d;
        var overtimeDays = workLines.GroupBy(x => x.Date).Count(group => group.Sum(x => x.DurationMinutes) > 480);
        gfx.DrawString($"Leave days: {data.LeaveDays}    Days off: {DaysOff(data)}    Worked days: {workedDays}    Work hours: {hours:0.##}    Overtime days: {overtimeDays}",
            body, XBrushes.Black, left, 91);
        var y = 112d;
        var showMember = data.Lines.Any(x => x.Member.Length > 0);
        // A Member column narrows Task so the table keeps the same width.
        var widths = showMember ? new[] { 42d, 76d, 76d, 214d, 82d } : new[] { 42d, 76d, 290d, 82d };
        var headers = showMember
            ? new[] { "Date", "Member", "Time", "Task / activity", "Notes" }
            : new[] { "Date", "Time", "Task / activity", "Notes" };
        var descriptionChars = showMember ? 40d : 54d;
        double x = left;
        for (var i = 0; i < headers.Length; i++)
        {
            gfx.DrawRectangle(blue, x, y, widths[i], 20);
            gfx.DrawString(headers[i], bold, XBrushes.White, x + 3, y + 14);
            x += widths[i];
        }
        y += 20;
        foreach (var line in data.Lines.OrderBy(x => x.Date).ThenBy(x => x.Member).ThenBy(x => x.Start))
        {
            var description = string.Join(" · ", new[] { line.Task, line.Detail, line.Project, line.Category }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            var lines = Math.Max(1, (int)Math.Ceiling(Math.Max(description.Length / descriptionChars,
                showMember ? line.Member.Length / 14d : 0)));
            var height = Math.Max(18, lines * 11 + 6);
            if (y + height > 770)
            {
                page = document.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                gfx = XGraphics.FromPdfPage(page);
                y = 45;
            }
            x = left;
            var day = line.Date.Day.ToString(CultureInfo.InvariantCulture);
            var time = line.Start.Length > 0 ? $"{line.Start} - {line.End}" : "";
            var values = showMember
                ? new[] { day, line.Member, time, description, line.Notes }
                : new[] { day, time, description, line.Notes };
            for (var i = 0; i < values.Length; i++)
            {
                if (IsDayOff(line)) gfx.DrawRectangle(dayOff, x, y, widths[i], height);
                else if ((line.Date.Day & 1) == 0) gfx.DrawRectangle(pale, x, y, widths[i], height);
                gfx.DrawRectangle(XPens.LightGray, x, y, widths[i], height);
                gfx.DrawString(values[i] ?? "", body, XBrushes.Black,
                    new XRect(x + 3, y + 3, widths[i] - 6, height - 6), XStringFormats.TopLeft);
                x += widths[i];
            }
            y += height;
        }
        y = Math.Min(y + 24, 760);
        gfx.DrawString($"Work period: {data.Period}    Employee signature: ____________________",
            body, XBrushes.Black, left, y);
        gfx.DrawString("Supervisor signature: ____________________    Date: ______________",
            body, XBrushes.Black, left, y + 18);
        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    public static IReadOnlyList<ReportLine> ReadSnapshot(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var entries = root.TryGetProperty("entries", out var value) ? value : default;
        var leaves = root.TryGetProperty("leaves", out value) ? value : default;
        var lines = new List<ReportLine>();
        if (entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                var date = DateOnly.Parse(entry.GetProperty("Date").GetString()!, CultureInfo.InvariantCulture);
                var start = TimeOnly.Parse(entry.GetProperty("StartTime").GetString()!, CultureInfo.InvariantCulture);
                var end = TimeOnly.Parse(entry.GetProperty("EndTime").GetString()!, CultureInfo.InvariantCulture);
                lines.Add(new ReportLine(date, start.ToString("HH:mm"), end.ToString("HH:mm"),
                    entry.GetProperty("DurationMinutes").GetInt32(), Get(entry, "TaskName"),
                    Get(entry, "Detail"), Get(entry, "Notes"), Get(entry, "ProjectName") is { Length: > 0 } projectName
                        ? projectName : Get(entry, "ProjectId"),
                    Get(entry, "CategoryName") is { Length: > 0 } categoryName
                        ? categoryName : Get(entry, "CategoryId"), "work"));
            }
        }
        if (leaves.ValueKind == JsonValueKind.Array)
        {
            foreach (var leave in leaves.EnumerateArray())
            {
                var date = DateOnly.Parse(leave.GetProperty("Date").GetString()!, CultureInfo.InvariantCulture);
                lines.Add(new ReportLine(date, "", "", 0, Get(leave, "Kind"), "", Get(leave, "Notes"), "", "", "leave"));
            }
        }
        return lines;
    }

    /// <summary>A row for every day of the month the organization does not work: each holiday by name,
    /// then each other weekday outside the snapshot's <c>workingDays</c> (Monday to Friday for months
    /// closed before work weeks existed).</summary>
    public static IReadOnlyList<ReportLine> ReadDaysOff(string json, int year, int month)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var holidays = new Dictionary<DateOnly, string>();
        if (root.TryGetProperty("holidays", out var value) && value.ValueKind == JsonValueKind.Array)
            foreach (var holiday in value.EnumerateArray())
            {
                var date = DateOnly.Parse(holiday.GetProperty("Date").GetString()!, CultureInfo.InvariantCulture);
                if (date.Year == year && date.Month == month) holidays.TryAdd(date, Get(holiday, "Name"));
            }
        var workingDays = root.TryGetProperty("workingDays", out value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(x => x.GetInt32()).ToArray() : DefaultWorkingDays;
        var lines = new List<ReportLine>();
        for (var date = new DateOnly(year, month, 1); date.Month == month; date = date.AddDays(1))
        {
            if (holidays.TryGetValue(date, out var name))
                lines.Add(new ReportLine(date, "", "", 0, name, "", "Holiday", "", "", "holiday"));
            else if (!workingDays.Contains((int)date.DayOfWeek))
                lines.Add(new ReportLine(date, "", "", 0, "Day off", "", "Weekly day off", "", "", "dayoff"));
        }
        return lines;
    }

    public static int CountLeaveDays(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("leaves", out var leaves) &&
            leaves.ValueKind == JsonValueKind.Array ? leaves.GetArrayLength() : 0;
    }

    private static string Get(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static readonly object FontGate = new();
    private static bool _fontConfigured;

    private static void ConfigureFont()
    {
        if (_fontConfigured) return;
        lock (FontGate)
        {
            if (_fontConfigured) return;
            GlobalFontSettings.FontResolver ??= new NotoThaiFontResolver();
            _fontConfigured = true;
        }
    }

    private sealed class NotoThaiFontResolver : IFontResolver
    {
        private static readonly byte[] FontBytes = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fonts", "NotoSansThai-Regular.ttf"));

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
            familyName.Equals("Noto Sans Thai", StringComparison.OrdinalIgnoreCase)
                ? new FontResolverInfo("NotoThaiRegular", bold, italic) : null;

        public byte[]? GetFont(string faceName) => faceName == "NotoThaiRegular" ? FontBytes : null;
    }
}
