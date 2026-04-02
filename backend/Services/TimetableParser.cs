using System.Globalization;
using System.Text.RegularExpressions;
using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public sealed class TimetableParser : ITimetableParser
{
    private static readonly Regex AcademicLinePattern = new(
        "^(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\\s+(\\d{2}[:h]\\d{2})-(\\d{2}[:h]\\d{2})\\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AcademicGridPattern = new(        // Match lines like "GR1 Monday 1 DISD5319" or "GR1-GR3 Monday 1 DISD5319"
        @"^(?:(?<group>GR\s*[1-9][0-9]?(?:\s*[-&,]\s*GR\s*[1-9][0-9]?)?)\s+)?(?<day>Mo|Tu|We|Th|Fr|Sa|Su|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\s+(?:P(?:eriod)?\s*)?(?<period>[1-9]|1[0-2])\s+(?<subject>.+?)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AcademicGridTailPattern = new(
        @"^(?<subject>.+?)\s+(?:(?<group>GR\s*[1-9][0-9]?(?:\s*[-&,]\s*GR\s*[1-9][0-9]?)?)\s+)?(?<day>Mo|Tu|We|Th|Fr|Sa|Su|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\s+(?:P(?:eriod)?\s*)?(?<period>[1-9]|1[0-2])$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ExamLinePattern = new(
        "^(?<code>[A-Z]{3,4}\\s*\\d{4})\\s+(?<name>.+?)\\s+(?<type>Test|Exam|Assignment|Practical|Project|Quiz|Presentation)(?<tail>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DatePattern = new(
        "(?<date>\\b\\d{1,2}[-/ ](Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|\\d{1,2})[-/ ]\\d{2,4}\\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TimePattern = new(
        "(?<time>\\b([01]?\\d|2[0-3])[:h][0-5]\\d\\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        var periodMap = GetPeriodMap();

        if (LooksLikeAssessmentTimetable(input))
        {
            result.Warnings.Add("This looks like an assessment/PAS timetable. Switch Timetable Type to 'Exam / Assessment Timetable' and preview again.");
            result.Diagnostics.Add("branch=academic_wrong_mode_detected_pas");
            return result;
        }

        var lines = input
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var stats = new Dictionary<string, int>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var match = AcademicLinePattern.Match(line);
            if (match.Success)
            {
                var day = Enum.Parse<DayOfWeek>(match.Groups[1].Value, true);
                var startRaw = match.Groups[2].Value.Replace("h", ":", StringComparison.OrdinalIgnoreCase);
                var endRaw = match.Groups[3].Value.Replace("h", ":", StringComparison.OrdinalIgnoreCase);
                var start = TimeOnly.ParseExact(startRaw, "HH:mm", CultureInfo.InvariantCulture);
                var end = TimeOnly.ParseExact(endRaw, "HH:mm", CultureInfo.InvariantCulture);
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
                IncrementStat(stats, "academic_time_range");
                continue;
            }

            if (TryParseGridLine(line, periodMap, out var gridEvent, out var gridBranch))
            {
                var existingIndex = result.AcademicEvents.FindIndex(e => e.Day == gridEvent.Day && e.StartTime == gridEvent.StartTime && e.EndTime == gridEvent.EndTime);
                if (existingIndex >= 0)
                {
                    var existing = result.AcademicEvents[existingIndex];
                    result.AcademicEvents[existingIndex] = new ClassEvent
                    {
                        Day = existing.Day,
                        StartTime = existing.StartTime,
                        EndTime = existing.EndTime,
                        Subject = existing.Subject + " " + gridEvent.Subject,
                        Lecturer = existing.Lecturer,
                        Venue = existing.Venue
                    };
                }
                else
                {
                    result.AcademicEvents.Add(gridEvent);
                }
                IncrementStat(stats, gridBranch);
            }
            else
            {
                // Only warn if the line clearly looks like it should have been a class (has a module code)
                if (Regex.IsMatch(line, @"[A-Z]{3,4}\s*\d{3,4}", RegexOptions.IgnoreCase) && !line.Contains(","))
                {
                    result.Warnings.Add($"Skipped module-like line: '{line}' (check format)");
                }
            }
        }

        foreach (var stat in stats)
        {
            result.Diagnostics.Add($"branch={stat.Key} count={stat.Value}");
        }

        return result;
    }

    private static void IncrementStat(Dictionary<string, int> stats, string key)
    {
        if (!stats.TryAdd(key, 1)) stats[key]++;
    }

    private static bool LooksLikeAssessmentTimetable(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;

        var hasPasKeywords =
            input.Contains("Assessment Timetable", StringComparison.OrdinalIgnoreCase) ||
            input.Contains("Campus Sitting", StringComparison.OrdinalIgnoreCase) ||
            input.Contains("Online Submission", StringComparison.OrdinalIgnoreCase) ||
            input.Contains("Turnitin", StringComparison.OrdinalIgnoreCase);

        var disMatches = Regex.Matches(input, "\\bDIS[123]\\b", RegexOptions.IgnoreCase).Count;
        var moduleCodeMatches = Regex.Matches(input, "\\b[A-Z]{4}\\d{4}\\b").Count;

        return hasPasKeywords || (disMatches >= 3 && moduleCodeMatches >= 3);
    }

    private static bool TryParseGridLine(
        string line,
        IReadOnlyDictionary<int, (TimeOnly Start, TimeOnly End)> periodMap,
        out ClassEvent classEvent,
        out string branch)
    {
        classEvent = default!;
        branch = string.Empty;
        var match = AcademicGridPattern.Match(line);
        if (!match.Success)
        {
            match = AcademicGridTailPattern.Match(line);
            if (!match.Success)
            {
                return false;
            }
            branch = "academic_grid_tail";
        }
        else
        {
            branch = "academic_grid_head";
        }

        if (!int.TryParse(match.Groups["period"].Value, out var period) || !periodMap.TryGetValue(period, out var slot))
        {
            return false;
        }

        if (!TryParseDay(match.Groups["day"].Value, out var day))
        {
            return false;
        }

        var subject = match.Groups["subject"].Value.Trim();
        if (match.Groups["group"].Success)
        {
            var grp = match.Groups["group"].Value.Trim().ToUpperInvariant().Replace(" ", "");
            subject = $"{subject} | {grp}";
        }

        classEvent = new ClassEvent
        {
            Day = day,
            StartTime = slot.Start,
            EndTime = slot.End,
            Subject = subject
        };

        return true;
    }

    private static bool TryParseDay(string value, out DayOfWeek day)
    {
        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "mo":
            case "monday":
                day = DayOfWeek.Monday;
                return true;
            case "tu":
            case "tuesday":
                day = DayOfWeek.Tuesday;
                return true;
            case "we":
            case "wednesday":
                day = DayOfWeek.Wednesday;
                return true;
            case "th":
            case "thursday":
                day = DayOfWeek.Thursday;
                return true;
            case "fr":
            case "friday":
                day = DayOfWeek.Friday;
                return true;
            case "sa":
            case "saturday":
                day = DayOfWeek.Saturday;
                return true;
            case "su":
            case "sunday":
                day = DayOfWeek.Sunday;
                return true;
            default:
                day = default;
                return false;
        }
    }

    private static IReadOnlyDictionary<int, (TimeOnly Start, TimeOnly End)> GetPeriodMap()
    {
        return new Dictionary<int, (TimeOnly Start, TimeOnly End)>
        {
            [1] = (new TimeOnly(8, 0), new TimeOnly(8, 50)),
            [2] = (new TimeOnly(9, 0), new TimeOnly(9, 50)),
            [3] = (new TimeOnly(10, 0), new TimeOnly(10, 50)),
            [4] = (new TimeOnly(11, 0), new TimeOnly(11, 50)),
            [5] = (new TimeOnly(12, 0), new TimeOnly(12, 50)),
            [6] = (new TimeOnly(13, 0), new TimeOnly(13, 50)),
            [7] = (new TimeOnly(14, 0), new TimeOnly(14, 50)),
            [8] = (new TimeOnly(15, 0), new TimeOnly(15, 50)),
            [9] = (new TimeOnly(16, 0), new TimeOnly(16, 50)),
            [10] = (new TimeOnly(17, 0), new TimeOnly(17, 50)),
            [11] = (new TimeOnly(18, 0), new TimeOnly(18, 50)),
            [12] = (new TimeOnly(19, 0), new TimeOnly(19, 50))
        };
    }

    private static TimetableParseResult ParseExam(string input)
    {
        var result = new TimetableParseResult();

        var lines = input
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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

            var timeRaw = timeMatch.Groups["time"].Value.Replace("h", ":", StringComparison.OrdinalIgnoreCase);
            if (!TimeOnly.TryParseExact(timeRaw, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                result.Warnings.Add($"Skipped line: '{line}' (invalid time)");
                continue;
            }

            int? sitting = sittingMatch.Success
                ? int.Parse(sittingMatch.Groups["sitting"].Value, CultureInfo.InvariantCulture)
                : null;
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
            result.Diagnostics.Add($"exam-line: branch=exam_line_regex module={moduleCode}");
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
