using System.Globalization;
using System.Text;
using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public sealed class CalendarExportService : ICalendarExportService
{
    public string BuildAcademicCalendar(AcademicSyncRequest request)
    {
        var lines = CreateCalendarHeader();
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var weeks = request.WeeksDuration <= 0 ? 16 : request.WeeksDuration;
        var recurrenceEndDate = request.SemesterEndDate ?? today.AddDays(weeks * 7);

        foreach (var item in request.Events)
        {
            var firstOccurrence = GetNextDateForDay(item.Day, today);
            var summary = item.Subject.StartsWith("[CLASS] ", StringComparison.OrdinalIgnoreCase)
                ? item.Subject
                : $"[CLASS] {item.Subject}";

            lines.Add("BEGIN:VEVENT");
            lines.Add($"UID:{BuildUid("class", summary, firstOccurrence, item.StartTime)}");
            lines.Add($"DTSTAMP:{FormatUtc(now)}");
            lines.Add($"DTSTART;TZID={request.TimeZone}:{FormatLocal(firstOccurrence, item.StartTime)}");
            lines.Add($"DTEND;TZID={request.TimeZone}:{FormatLocal(firstOccurrence, item.EndTime)}");
            lines.Add($"SUMMARY:{Escape(summary)}");
            lines.Add($"DESCRIPTION:{Escape(BuildClassDescription(item))}");

            if (!string.IsNullOrWhiteSpace(item.Venue))
            {
                lines.Add($"LOCATION:{Escape(item.Venue)}");
            }

            // Weekly recurrence keeps the exported academic timetable close to the old Google sync behavior.
            lines.Add($"RRULE:FREQ=WEEKLY;UNTIL={FormatUtc(recurrenceEndDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc))}");
            lines.Add("END:VEVENT");
        }

        lines.AddRange(CreateCalendarFooter());
        return string.Join("\r\n", lines) + "\r\n";
    }

    public string BuildAssessmentCalendar(AssessmentSyncRequest request)
    {
        var lines = CreateCalendarHeader();
        var now = DateTimeOffset.UtcNow;
        var duration = request.DurationMinutes <= 0 ? 60 : request.DurationMinutes;

        foreach (var item in request.Events)
        {
            var start = item.Date.ToDateTime(item.Time);
            var end = start.AddMinutes(duration);
            var summary = string.IsNullOrWhiteSpace(item.ModuleName)
                ? $"[ASSESSMENT] {item.ModuleCode} - {item.AssessmentType}"
                : $"[ASSESSMENT] {item.ModuleCode} - {item.ModuleName} ({item.AssessmentType})";

            lines.Add("BEGIN:VEVENT");
            lines.Add($"UID:{BuildUid("assessment", item.ModuleCode, item.Date, item.Time)}");
            lines.Add($"DTSTAMP:{FormatUtc(now)}");
            lines.Add($"DTSTART;TZID={request.TimeZone}:{FormatLocal(item.Date, item.Time)}");
            lines.Add($"DTEND;TZID={request.TimeZone}:{FormatLocal(DateOnly.FromDateTime(end), TimeOnly.FromDateTime(end))}");
            lines.Add($"SUMMARY:{Escape(summary)}");
            lines.Add($"DESCRIPTION:{Escape(BuildAssessmentDescription(item))}");
            lines.Add("END:VEVENT");
        }

        lines.AddRange(CreateCalendarFooter());
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static List<string> CreateCalendarHeader() =>
        new()
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Rosebank Timetable Sync//EN",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH"
        };

    private static IReadOnlyList<string> CreateCalendarFooter() => new[] { "END:VCALENDAR" };

    private static string BuildClassDescription(ClassEvent item)
    {
        var lines = new List<string> { $"Subject: {item.Subject}" };
        if (!string.IsNullOrWhiteSpace(item.Lecturer)) lines.Add($"Lecturer: {item.Lecturer}");
        if (!string.IsNullOrWhiteSpace(item.Venue)) lines.Add($"Venue: {item.Venue}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildAssessmentDescription(AssessmentEvent item)
    {
        var lines = new List<string> { $"Module: {item.ModuleCode}", $"Assessment: {item.AssessmentType}" };
        if (!string.IsNullOrWhiteSpace(item.ModuleName)) lines.Add($"Name: {item.ModuleName}");
        if (!string.IsNullOrWhiteSpace(item.DeliveryMode)) lines.Add($"Mode: {item.DeliveryMode}");
        if (item.Sitting.HasValue) lines.Add($"Sitting: {item.Sitting.Value}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Escape(string value) =>
        value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace(";", @"\;", StringComparison.Ordinal)
            .Replace(",", @"\,", StringComparison.Ordinal)
            .Replace("\r\n", @"\n", StringComparison.Ordinal)
            .Replace("\n", @"\n", StringComparison.Ordinal);

    private static string FormatLocal(DateOnly date, TimeOnly time) =>
        $"{date:yyyyMMdd}T{time:HHmmss}";

    private static string FormatUtc(DateTime value) =>
        value.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string BuildUid(string prefix, string key, DateOnly date, TimeOnly time) =>
        $"{prefix}-{SanitizeUidPart(key)}-{date:yyyyMMdd}-{time:HHmmss}@rosebank-sync.local";

    private static string SanitizeUidPart(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        return builder.ToString().Trim('-');
    }

    private static DateOnly GetNextDateForDay(DayOfWeek target, DateOnly from)
    {
        var daysToAdd = ((int)target - (int)from.DayOfWeek + 7) % 7;
        if (daysToAdd == 0) daysToAdd = 7;
        return from.AddDays(daysToAdd);
    }
}
