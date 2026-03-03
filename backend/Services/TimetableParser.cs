using System.Globalization;
using System.Text.RegularExpressions;
using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public sealed class TimetableParser : ITimetableParser
{
    private static readonly Regex AcademicLinePattern = new(
        "^(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\\s+(\\d{2}:\\d{2})-(\\d{2}:\\d{2})\\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ExamLinePattern = new(
        "^(?<code>[A-Z]{4}\\d{4})\\s+(?<name>.+?)\\s+(?<type>Test|Exam|Assignment|Practical|Project|Quiz|Presentation)(?<tail>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DatePattern = new(
        "(?<date>\\b\\d{1,2}[-/](Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|\\d{1,2})[-/]\\d{2,4}\\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TimePattern = new(
        "(?<time>\\b([01]?\\d|2[0-3]):[0-5]\\d\\b)",
        RegexOptions.Compiled);

    private static readonly Regex SittingPattern = new(
        "\\bSitting\\s*(?<sitting>[12])\\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public TimetableParseResult Parse(string input, ParseMode mode)
    {
        return mode switch
        {
            ParseMode.Academic => ParseAcademic(input),
            ParseMode.Exam => ParseExam(input),
            _ => new TimetableParseResult()
        };
    }

    private static TimetableParseResult ParseAcademic(string input)
    {
        var result = new TimetableParseResult();

        var lines = input
            .Split(new[] { '\\r', '\\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var match = AcademicLinePattern.Match(line);
            if (!match.Success)
            {
                result.Warnings.Add($"Skipped line: '{line}' (invalid academic format)");
                continue;
            }

            var day = Enum.Parse<DayOfWeek>(match.Groups[1].Value, true);
            var start = TimeOnly.ParseExact(match.Groups[2].Value, "HH:mm", CultureInfo.InvariantCulture);
            var end = TimeOnly.ParseExact(match.Groups[3].Value, "HH:mm", CultureInfo.InvariantCulture);
            var subject = match.Groups[4].Value.Trim();

            if (end <= start)
            {
                result.Warnings.Add($"Skipped line: '{line}' (end time must be after start time)");
                continue;
            }

            result.AcademicEvents.Add(new ClassEvent
            {
                Day = day,
                StartTime = start,
                EndTime = end,
                Subject = subject
            });
        }

        return result;
    }

    private static TimetableParseResult ParseExam(string input)
    {
        var result = new TimetableParseResult();

        var lines = input
            .Split(new[] { '\\r', '\\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 5)
            .ToArray();

        foreach (var line in lines)
        {
            var codeLike = line.Contains("DIS", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(line, "[A-Z]{4}\\d{4}");
            if (!codeLike)
            {
                continue;
            }

            var examMatch = ExamLinePattern.Match(line);
            var dateMatch = DatePattern.Match(line);
            var timeMatch = TimePattern.Match(line);
            var sittingMatch = SittingPattern.Match(line);

            if (!examMatch.Success || !dateMatch.Success || !timeMatch.Success)
            {
                result.Warnings.Add($"Skipped line: '{line}' (missing exam fields)");
                continue;
            }

            if (!TryParseDate(dateMatch.Groups["date"].Value, out var date))
            {
                result.Warnings.Add($"Skipped line: '{line}' (invalid date)");
                continue;
            }

            if (!TimeOnly.TryParseExact(timeMatch.Groups["time"].Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                result.Warnings.Add($"Skipped line: '{line}' (invalid time)");
                continue;
            }

            var sitting = sittingMatch.Success ? int.Parse(sittingMatch.Groups["sitting"].Value, CultureInfo.InvariantCulture) : null;
            var moduleCode = examMatch.Groups["code"].Value.Trim().ToUpperInvariant();
            var moduleName = examMatch.Groups["name"].Value.Trim();
            var assessmentType = examMatch.Groups["type"].Value.Trim();

            result.ExamEvents.Add(new ExamEvent
            {
                ModuleCode = moduleCode,
                ModuleName = moduleName,
                AssessmentType = assessmentType,
                Sitting = sitting,
                Date = date,
                Time = time
            });
        }

        if (result.ExamEvents.Count == 0)
        {
            result.Warnings.Add("No exam events were parsed. Try preview-text mode and paste cleaner text from the PDF.");
        }

        return result;
    }

    private static bool TryParseDate(string input, out DateOnly date)
    {
        var formats = new[] { "d-MMM-yy", "dd-MMM-yy", "d-MMM-yyyy", "dd-MMM-yyyy", "d-M-yyyy", "dd-M-yyyy", "d-M-yy", "dd-M-yy" };
        foreach (var format in formats)
        {
            if (DateOnly.TryParseExact(input, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return true;
            }
        }

        return DateOnly.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
