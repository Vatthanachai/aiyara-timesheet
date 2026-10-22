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

/// <summary><paramref name="StandardDayMinutes"/> is a normal working day (the working hours less the
/// break); a day with more work than that counts as overtime.</summary>
public sealed record ReportDocumentData(string EmployeeName, string Department, string Period,
    IReadOnlyList<ReportLine> Lines, decimal LeaveDays, ReportFormats? Formats = null,
    int StandardDayMinutes = ReportDocumentRenderer.DefaultStandardDayMinutes, string Language = "en");

/// <summary>The organization's date and time formats (Identity's <c>OrganizationFormats</c>):
/// <c>bbbb</c> is the Buddhist year; documents are in English, so <c>MMM</c> is an English month.
/// An unknown value falls back to the default.</summary>
public sealed record ReportFormats(string DateFormat = "dd/MM/yyyy", string TimeFormat = "HH:mm")
{
    public static readonly ReportFormats Default = new();

    public string Date(DateOnly date) => DateFormat switch
    {
        "dd/MM/bbbb" => $"{date.Day:00}/{date.Month:00}/{date.Year + 543}",
        "d MMM yyyy" or "yyyy-MM-dd" or "MM/dd/yyyy" => date.ToString(DateFormat, CultureInfo.InvariantCulture),
        _ => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
    };

    /// <summary>A clock time given as <c>HH:mm</c>; empty stays empty.</summary>
    public string Time(string value) => value.Length == 0 || TimeFormat != "h:mm a" ? value
        : TimeOnly.Parse(value, CultureInfo.InvariantCulture).ToString("h:mm tt", CultureInfo.InvariantCulture);

    public string Period(DateOnly start, DateOnly end) => $"{Date(start)} – {Date(end)}";
}

public static class ReportDocumentRenderer
{
    /// <summary>A normal working day for months closed before working hours existed.</summary>
    public const int DefaultStandardDayMinutes = 8 * 60;

    private static int OvertimeDays(ReportDocumentData data) => data.Lines.Where(x => x.Kind == "work")
        .GroupBy(x => x.Date).Count(group => group.Sum(x => x.DurationMinutes) > data.StandardDayMinutes);

    /// <summary>The weekdays an organization works when its snapshot predates work weeks.</summary>
    private static readonly int[] DefaultWorkingDays = [1, 2, 3, 4, 5];

    public static bool IsDayOff(ReportLine line) => line.Kind is "holiday" or "dayoff";

    /// <summary>Rows by date; a day off sorts first in its day because it names no member or time.</summary>
    private static IEnumerable<ReportLine> Ordered(ReportDocumentData data) =>
        data.Lines.OrderBy(x => x.Date).ThenBy(x => x.Member).ThenBy(x => x.Start);

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
        sheet.Cell(6, 5).Value = "Overtime days"; sheet.Cell(6, 6).Value = OvertimeDays(data);
        var showMember = data.Lines.Any(x => x.Member.Length > 0);
        string[] headers = showMember
            ? ["Date", "Member", "Start", "End", "Hours", "Task", "Detail", "Notes", "Project", "Category"]
            : ["Date", "Start", "End", "Hours", "Task", "Detail", "Notes", "Project", "Category"];
        for (var column = 0; column < headers.Length; column++) sheet.Cell(7, column + 1).Value = headers[column];
        var row = 8;
        DateOnly? previous = null;
        var formats = data.Formats ?? ReportFormats.Default;
        foreach (var line in Ordered(data))
        {
            var column = 1;
            // A day's date only on its first row; the rows under it show their own times.
            if (line.Date != previous) sheet.Cell(row, column).Value = formats.Date(line.Date);
            previous = line.Date;
            column++;
            if (showMember) sheet.Cell(row, column++).Value = line.Member;
            sheet.Cell(row, column++).Value = formats.Time(line.Start);
            sheet.Cell(row, column++).Value = formats.Time(line.End);
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
        // An Excel table with filters and banded rows; it needs a row under its header even when empty.
        var table = sheet.Range(7, 1, Math.Max(row - 1, 8), headers.Length).CreateTable("Timesheet");
        table.Theme = XLTableTheme.TableStyleMedium2;
        table.ShowAutoFilter = true;
        sheet.Columns().AdjustToContents(8, 48);
        sheet.SheetView.FreezeRows(7);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] RenderPdf(ReportDocumentData data)
    {
        ConfigureFont();
        var text = data.Language == "th" ? PdfText.Thai : PdfText.English;
        var document = new PdfDocument();
        document.Info.Title = $"Timesheet {data.Period}";
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        var gfx = XGraphics.FromPdfPage(page);
        var title = new XFont("Sarabun", 15, XFontStyleEx.Bold);
        var body = new XFont("Sarabun", 8, XFontStyleEx.Regular);
        var bold = new XFont("Sarabun", 8, XFontStyleEx.Bold);
        var blue = new XSolidBrush(XColor.FromArgb(57, 111, 190));
        var pale = new XSolidBrush(XColor.FromArgb(224, 234, 250));
        var dayOff = new XSolidBrush(XColor.FromArgb(232, 234, 238));
        const double left = 35, right = 560;
        gfx.DrawString($"{text.Title} · {data.Period}", title, XBrushes.Black,
            new XRect(left, 30, right - left, 24), XStringFormats.TopCenter);
        gfx.DrawString($"{text.Employee}: {data.EmployeeName}    {text.Department}: {data.Department}", body,
            XBrushes.Black, left, 76);
        var workLines = data.Lines.Where(x => x.Kind == "work").ToArray();
        var workedDays = workLines.Select(x => x.Date).Distinct().Count();
        var hours = workLines.Sum(x => x.DurationMinutes) / 60d;
        var overtimeDays = OvertimeDays(data);
        gfx.DrawString($"{text.LeaveDays}: {data.LeaveDays:0.##}    {text.DaysOff}: {DaysOff(data)}    {text.WorkedDays}: {workedDays}    {text.WorkHours}: {hours:0.##}    {text.OvertimeDays}: {overtimeDays}",
            body, XBrushes.Black, left, 91);
        var y = 112d;
        var showMember = data.Lines.Any(x => x.Member.Length > 0);
        // A Member column narrows Task so the table keeps the same width.
        var widths = showMember ? new[] { 42d, 76d, 76d, 214d, 82d } : new[] { 42d, 76d, 290d, 82d };
        var headers = showMember
            ? new[] { text.Date, text.Member, text.Time, text.Task, text.Notes }
            : new[] { text.Date, text.Time, text.Task, text.Notes };
        double x = left;
        for (var i = 0; i < headers.Length; i++)
        {
            gfx.DrawRectangle(blue, x, y, widths[i], 20);
            gfx.DrawString(headers[i], bold, XBrushes.White, x + 3, y + 14);
            x += widths[i];
        }
        y += 20;
        DateOnly? previousDate = null;
        var formats = data.Formats ?? ReportFormats.Default;
        foreach (var line in Ordered(data))
        {
            var description = string.Join(" · ", new[] { line.Task, line.Detail, line.Project, line.Category }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            // A day's date only on its first row, and again at the top of a new page.
            var day = line.Date != previousDate || y == 45 ? line.Date.Day.ToString(CultureInfo.InvariantCulture) : "";
            var time = line.Start.Length > 0 ? $"{formats.Time(line.Start)} - {formats.Time(line.End)}" : "";
            var values = showMember
                ? new[] { day, line.Member, time, description, line.Notes }
                : new[] { day, time, description, line.Notes };
            // The PDF library neither wraps nor clips, so each cell is broken into lines by its measured width.
            var cells = values.Select((value, i) => Wrap(gfx, value, body, widths[i] - 6)).ToArray();
            var height = Math.Max(18, cells.Max(cell => cell.Count) * LineHeight + 6);
            if (y + height > 770)
            {
                page = document.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                gfx = XGraphics.FromPdfPage(page);
                y = 45;
            }
            x = left;
            previousDate = line.Date;
            for (var i = 0; i < values.Length; i++)
            {
                if (IsDayOff(line)) gfx.DrawRectangle(dayOff, x, y, widths[i], height);
                else if ((line.Date.Day & 1) == 0) gfx.DrawRectangle(pale, x, y, widths[i], height);
                gfx.DrawRectangle(XPens.LightGray, x, y, widths[i], height);
                for (var row = 0; row < cells[i].Count; row++)
                    gfx.DrawString(cells[i][row], body, XBrushes.Black,
                        new XRect(x + 3, y + 3 + row * LineHeight, widths[i] - 6, LineHeight), XStringFormats.TopLeft);
                x += widths[i];
            }
            y += height;
        }
        y = Math.Min(y + 24, 760);
        gfx.DrawString($"{text.WorkPeriod}: {data.Period}    {text.EmployeeSignature}: ____________________",
            body, XBrushes.Black, left, y);
        gfx.DrawString($"{text.SupervisorSignature}: ____________________    {text.Date}: ______________",
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
                // A part-day leave carries its minutes; work hours count work rows only.
                lines.Add(new ReportLine(date, "", "", LeaveMinutes(leave) ?? 0, Get(leave, "Kind"), "",
                    Get(leave, "Notes"), "", "", "leave"));
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

    /// <summary>A normal working day from a month snapshot's <c>workHours</c> (the hours less the
    /// break), or null for a month closed before working hours existed.</summary>
    public static int? ReadStandardDayMinutes(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("workHours", out var hours) || hours.ValueKind != JsonValueKind.Object)
            return null;
        static int? Minutes(JsonElement hours, string name) =>
            hours.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
            TimeOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var time)
                ? time.Hour * 60 + time.Minute : null;
        if (Minutes(hours, "startTime") is not { } start || Minutes(hours, "endTime") is not { } end) return null;
        var breakMinutes = Minutes(hours, "breakStart") is { } breakStart && Minutes(hours, "breakEnd") is { } breakEnd
            ? breakEnd - breakStart : 0;
        return end - start - breakMinutes;
    }

    /// <summary>Days of leave in a month snapshot: a whole day each, or a part day's minutes against
    /// the snapshot's normal working day.</summary>
    public static decimal CountLeaveDays(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("leaves", out var leaves) || leaves.ValueKind != JsonValueKind.Array)
            return 0;
        var standard = ReadStandardDayMinutes(json) ?? DefaultStandardDayMinutes;
        return leaves.EnumerateArray().Sum(leave => LeaveDays(LeaveMinutes(leave), standard));
    }

    /// <summary>One leave in days: the whole day, or its minutes against the normal working day, to two places.</summary>
    public static decimal LeaveDays(int? minutes, int standardDayMinutes) =>
        minutes is { } part and > 0 && standardDayMinutes > 0 ? Math.Round((decimal)part / standardDayMinutes, 2) : 1;

    private static int? LeaveMinutes(JsonElement leave) =>
        leave.TryGetProperty("Minutes", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static string Get(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    /// <summary>The fixed words of the PDF, in the language the member chose on the page.</summary>
    private sealed record PdfText(string Title, string Employee, string Department, string LeaveDays, string DaysOff,
        string WorkedDays, string WorkHours, string OvertimeDays, string Date, string Member, string Time, string Task,
        string Notes, string WorkPeriod, string EmployeeSignature, string SupervisorSignature)
    {
        public static readonly PdfText English = new("Timesheet report", "Employee", "Department", "Leave days", "Days off",
            "Worked days", "Work hours", "Overtime days", "Date", "Member", "Time", "Task / activity", "Notes",
            "Work period", "Employee signature", "Supervisor signature");

        public static readonly PdfText Thai = new("รายงานบันทึกเวลา", "พนักงาน", "แผนก", "วันลา", "วันหยุด",
            "วันทำงาน", "ชั่วโมงทำงาน", "วันทำงานล่วงเวลา", "วันที่", "สมาชิก", "เวลา", "งาน / กิจกรรม", "หมายเหตุ",
            "ช่วงเวลาทำงาน", "ลายเซ็นพนักงาน", "ลายเซ็นหัวหน้างาน");
    }

    private const double LineHeight = 12;

    /// <summary>Breaks text into lines no wider than the width: after a space or a dot between parts when there is one,
    /// otherwise between characters, never between a Thai consonant and its vowel or tone marks, nor after a leading vowel.</summary>
    private static List<string> Wrap(XGraphics gfx, string? text, XFont font, double width)
    {
        var lines = new List<string>();
        foreach (var paragraph in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var line = new List<string>();
            var enumerator = StringInfo.GetTextElementEnumerator(paragraph);
            while (enumerator.MoveNext())
            {
                var element = (string)enumerator.Current;
                line.Add(element);
                if (line.Count == 1 || gfx.MeasureString(string.Concat(line), font).Width <= width) continue;
                line.RemoveAt(line.Count - 1);
                var carried = new List<string> { element };
                var space = line.FindLastIndex(x => x is " " or "·");
                if (space > 0)
                {
                    carried.InsertRange(0, line.Skip(space + 1));
                    line.RemoveRange(space + 1, line.Count - space - 1);
                }
                else if (line.Count > 1 && IsLeadingThaiVowel(line[^1]))
                {
                    carried.Insert(0, line[^1]);
                    line.RemoveAt(line.Count - 1);
                }
                lines.Add(string.Concat(line).TrimEnd());
                line = carried;
                if (line[0] == " ") line.RemoveAt(0);
            }
            lines.Add(string.Concat(line));
        }
        return lines;
    }

    private static bool IsLeadingThaiVowel(string element) => element[0] is >= '\u0E40' and <= '\u0E44';

    private static readonly object FontGate = new();
    private static bool _fontConfigured;

    private static void ConfigureFont()
    {
        if (_fontConfigured) return;
        lock (FontGate)
        {
            if (_fontConfigured) return;
            GlobalFontSettings.FontResolver ??= new SarabunFontResolver();
            _fontConfigured = true;
        }
    }

    /// <summary>Sarabun carries Thai and Latin (letters, digits, punctuation) in a real Regular and Bold face.</summary>
    private sealed class SarabunFontResolver : IFontResolver
    {
        private static readonly byte[] Regular = Load("Sarabun-Regular.ttf");
        private static readonly byte[] Bold = Load("Sarabun-Bold.ttf");

        private static byte[] Load(string file) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", file));

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
            familyName.Equals("Sarabun", StringComparison.OrdinalIgnoreCase)
                ? new FontResolverInfo(bold ? "SarabunBold" : "SarabunRegular", false, italic) : null;

        public byte[]? GetFont(string faceName) => faceName switch
        {
            "SarabunRegular" => Regular,
            "SarabunBold" => Bold,
            _ => null
        };
    }
}
