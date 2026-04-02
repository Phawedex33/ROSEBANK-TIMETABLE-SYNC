using System.Globalization;
using System.Text.RegularExpressions;
using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public sealed class RosebankParserService : IRosebankParserService
{
    private static readonly IReadOnlyDictionary<int, (string Start, string End)> PeriodMap =
        new Dictionary<int, (string Start, string End)>
        {
            [1] = ("08:00", "08:50"),
            [2] = ("09:00", "09:50"),
            [3] = ("10:00", "10:50"),
            [4] = ("11:00", "11:50"),
            [5] = ("12:00", "12:50"),
            [6] = ("13:00", "13:50"),
            [7] = ("14:00", "14:50"),
            [8] = ("15:00", "15:50"),
            [9] = ("16:00", "16:50")
        };

    private static readonly IReadOnlyDictionary<string, string> DayMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mo"] = "Monday",
            ["Tu"] = "Tuesday",
            ["We"] = "Wednesday",
            ["Th"] = "Thursday",
            ["Fr"] = "Friday",
            ["Sa"] = "Saturday"
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SubjectNameMap =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["DIS1"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BUIS5111"] = "Business Information Systems",
                ["IQTT5111"] = "Introduction to Quantitative Thinking and Techniques",
                ["PRLD5121"] = "Programming Logic and Design",
                ["PROG5121"] = "Programming 1A"
            },
            ["DIS2"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DATA6211"] = "Database (Introduction)",
                ["ISEC6321"] = "Information Security",
                ["PROG6221"] = "Programming 2A",
                ["SAND6221"] = "System Analysis and Design"
            },
            ["DIS3"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ADDB6311"] = "Advanced Databases",
                ["OPSC6311"] = "Open Source Coding (Introduction)",
                ["WEDE6021"] = "Web Development (Intermediate)",
                ["XISD5319"] = "Work Integrated Learning 3A"
            }
        };

    private readonly IPdfTextExtractor _extractor;
    private readonly IRosebankReferenceService _referenceService;
    private readonly IAssessmentParser _assessmentParser;

    public RosebankParserService(
        IPdfTextExtractor extractor,
        IRosebankReferenceService referenceService,
        IAssessmentParser assessmentParser)
    {
        _extractor = extractor;
        _referenceService = referenceService;
        _assessmentParser = assessmentParser;
    }

    public async Task<object> ParseAsync(RosebankParseRequest request, CancellationToken cancellationToken)
    {
        var missing = ValidateRequest(request);
        if (missing.Count > 0)
        {
            return new RosebankMissingInputsError { MissingFields = missing };
        }

        var studentYear = request.StudentYear!.Trim().ToUpperInvariant();
        var studentGroup = request.StudentGroup!.Trim().ToUpperInvariant();
        var warnings = new List<RosebankWarning>();

        var classText = await _extractor.ExtractAsync(request.ClassSchedulePdf!, cancellationToken);
        var classParse = ParseClassSchedule(classText, studentYear, studentGroup, warnings, _referenceService);

        List<RosebankAssessmentEvent> assessmentEvents = new();
        if (request.AssessmentSchedulePdf is null)
        {
            warnings.Add(new RosebankWarning
            {
                EventId = null,
                Issue = "Assessment schedule PDF was not uploaded. Returned class schedule only.",
                Severity = "info"
            });
        }
        else
        {
            var assessmentText = await _extractor.ExtractAsync(request.AssessmentSchedulePdf, cancellationToken);
            var assessmentParse = _assessmentParser.Parse(assessmentText);
            
            // Add parser warnings to main list
            foreach (var w in assessmentParse.Warnings)
            {
                warnings.Add(new RosebankWarning { Issue = w, Severity = "warning" });
            }

            // Map AssessmentEvent to RosebankAssessmentEvent
            var yearMap = SubjectNameMap.TryGetValue(studentYear, out var map) ? map : new Dictionary<string, string>();
            var allowedSubjectCodes = new HashSet<string>(yearMap.Keys, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < assessmentParse.Events.Count; i++)
            {
                var ev = assessmentParse.Events[i];
                if (allowedSubjectCodes.Count > 0 && !allowedSubjectCodes.Contains(ev.ModuleCode))
                {
                    warnings.Add(new RosebankWarning
                    {
                        EventId = null,
                        Issue = $"Skipped assessment '{ev.ModuleCode}' because it does not match {studentYear}.",
                        Severity = "info"
                    });
                    continue;
                }

                var subjectName = string.IsNullOrWhiteSpace(ev.ModuleName)
                    ? (yearMap.TryGetValue(ev.ModuleCode, out var mapped) ? mapped : null)
                    : ev.ModuleName;

                var isOnline = ev.DeliveryMode.Contains("Online", StringComparison.OrdinalIgnoreCase);
                var submissionType = isOnline ? "online" : "campus_sitting";
                var reminders = isOnline
                    ? new List<string> { "48hrs_before", "24hrs_before", "2hrs_before" }
                    : new List<string> { "24hrs_before", "2hrs_before" };

                assessmentEvents.Add(new RosebankAssessmentEvent
                {
                    Id = $"asm_{i + 1:000}",
                    SubjectCode = ev.ModuleCode,
                    SubjectName = subjectName,
                    AssessmentType = ev.AssessmentType,
                    SubmissionType = submissionType,
                    RequiresTurnitin = ev.DeliveryMode.Contains("Turnitin", StringComparison.OrdinalIgnoreCase),
                    SpecificDate = ev.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DueTime = ev.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
                    IsDeferred = ev.AssessmentType.Contains("Deferred", StringComparison.OrdinalIgnoreCase),
                    Reminders = reminders,
                    Confidence = string.IsNullOrWhiteSpace(subjectName) ? "low" : "high",
                    Notes = null
                });
            }
        }

        var uniqueSubjects = classParse.Events
            .Select(e => e.SubjectName ?? string.Empty)
            .Concat(assessmentEvents.Select(e => e.SubjectName ?? string.Empty))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var assessmentDates = assessmentEvents
            .Select(e => e.SpecificDate)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

        var response = new RosebankParseResponse
        {
            ParsedAt = DateTimeOffset.UtcNow.ToString("O"),
            StudentYear = studentYear,
            StudentGroup = studentGroup,
            Schedules = new RosebankSchedules
            {
                ClassSchedule = new RosebankClassSchedule
                {
                    SourceFormat = "image",
                    TimetableGeneratedDate = classParse.GeneratedDate,
                    Events = classParse.Events
                },
                AssessmentSchedule = new RosebankAssessmentSchedule
                {
                    SourceFormat = "table",
                    Events = assessmentEvents
                }
            },
            Warnings = warnings,
            Summary = new RosebankSummary
            {
                TotalClassEvents = classParse.Events.Count,
                TotalAssessmentEvents = assessmentEvents.Count,
                UniqueSubjects = uniqueSubjects,
                DaysWithClasses = classParse.Events
                    .Select(e => e.DayOfWeek ?? string.Empty)
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(DaySort)
                    .ToList(),
                EarliestAssessmentDate = assessmentDates.FirstOrDefault(),
                LatestAssessmentDate = assessmentDates.LastOrDefault()
            }
        };

        return response;
    }

    private static List<string> ValidateRequest(RosebankParseRequest request)
    {
        var missing = new List<string>();
        var validYears = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DIS1", "DIS2", "DIS3" };
        var validGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GR1", "GR2", "GR3" };

        if (string.IsNullOrWhiteSpace(request.StudentYear) || !validYears.Contains(request.StudentYear))
        {
            missing.Add("student_year");
        }

        if (string.IsNullOrWhiteSpace(request.StudentGroup) || !validGroups.Contains(request.StudentGroup))
        {
            missing.Add("student_group");
        }

        if (request.ClassSchedulePdf is null)
        {
            missing.Add("class_schedule_pdf");
        }

        return missing;
    }

    private static int DaySort(string day)
    {
        return day switch
        {
            "Monday" => 1,
            "Tuesday" => 2,
            "Wednesday" => 3,
            "Thursday" => 4,
            "Friday" => 5,
            "Saturday" => 6,
            _ => 9
        };
    }

    private static (List<RosebankClassEvent> Events, string? GeneratedDate) ParseClassSchedule(
        string text,
        string studentYear,
        string studentGroup,
        List<RosebankWarning> warnings,
        IRosebankReferenceService referenceService)
    {
        var generatedDateMatch = Regex.Match(text, @"Timetable generated:(?<date>\d{4}/\d{2}/\d{2})", RegexOptions.IgnoreCase);
        var generatedDate = generatedDateMatch.Success
            ? generatedDateMatch.Groups["date"].Value.Replace("/", "-", StringComparison.Ordinal)
            : null;

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var section = ExtractGroupSection(lines, studentGroup);
        if (section.Count == 0)
        {
            warnings.Add(new RosebankWarning
            {
                EventId = null,
                Issue = $"No class section found for {studentGroup}.",
                Severity = "critical"
            });
            return (new List<RosebankClassEvent>(), generatedDate);
        }

        var candidates = new List<(string DayCode, int Period, string SubjectCode, string? Room, string? Lecturer)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in section)
        {
            var match = Regex.Match(
                line,
                @"^(?:(?:GR\s*\d+)\s+)?(?<day>Mo|Tu|We|Th|Fr|Sa)\s+(?<period>[1-9])\s+(?<rest>.+)$",
                RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                continue;
            }

            var day = match.Groups["day"].Value;
            var period = int.Parse(match.Groups["period"].Value, CultureInfo.InvariantCulture);
            var rest = match.Groups["rest"].Value.Trim();

            var subjectMatch = Regex.Match(rest, @"\b(?<code>[A-Z]{4}\d{4})\b");
            if (!subjectMatch.Success)
            {
                continue;
            }

            var subjectCode = subjectMatch.Groups["code"].Value.ToUpperInvariant();
            var roomMatch = Regex.Match(rest, @"\b(?<room>PH\s*\d-\d{2})\b", RegexOptions.IgnoreCase);
            var room = roomMatch.Success ? Regex.Replace(roomMatch.Groups["room"].Value.ToUpperInvariant(), @"\s+", " ").Trim() : null;

            var lecturer = rest;
            lecturer = Regex.Replace(lecturer, @"\b[A-Z]{4}\d{4}\b", string.Empty);
            lecturer = Regex.Replace(lecturer, @"\bPH\s*\d-\d{2}\b", string.Empty, RegexOptions.IgnoreCase);
            lecturer = Regex.Replace(lecturer, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(lecturer))
            {
                lecturer = null;
            }

            var key = $"{day}|{period}|{subjectCode}|{room}|{lecturer}";
            if (seen.Add(key))
            {
                candidates.Add((day, period, subjectCode, room, lecturer));
            }
        }

        var grouped = candidates
            .GroupBy(x => (x.DayCode.ToUpperInvariant(), x.SubjectCode, Room: x.Room ?? string.Empty, Lecturer: x.Lecturer ?? string.Empty))
            .SelectMany(group =>
            {
                var ordered = group.OrderBy(x => x.Period).ToList();
                var blocks = new List<(int StartPeriod, int EndPeriod, string DayCode, string SubjectCode, string? Room, string? Lecturer)>();
                int start = ordered[0].Period;
                int end = start;
                for (var i = 1; i < ordered.Count; i++)
                {
                    var current = ordered[i].Period;
                    if (current == end + 1)
                    {
                        end = current;
                        continue;
                    }

                    blocks.Add((start, end, ordered[i - 1].DayCode, group.Key.SubjectCode, group.Key.Room, group.Key.Lecturer));
                    start = current;
                    end = current;
                }

                blocks.Add((start, end, ordered[^1].DayCode, group.Key.SubjectCode, group.Key.Room, group.Key.Lecturer));
                return blocks;
            })
            .OrderBy(x => DaySort(DayMap.TryGetValue(x.DayCode, out var d) ? d : ""))
            .ThenBy(x => x.StartPeriod)
            .ToList();

        var yearMap = SubjectNameMap.TryGetValue(studentYear, out var map) ? map : new Dictionary<string, string>();
        var numericYear = ParseNumericYear(studentYear);
        var events = new List<RosebankClassEvent>();
        for (var i = 0; i < grouped.Count; i++)
        {
            var row = grouped[i];
            var subjectName = yearMap.TryGetValue(row.SubjectCode, out var mappedName) ? mappedName : null;
            var confidence = subjectName is null ? "low" : "high";
            if (subjectName is null)
            {
                warnings.Add(new RosebankWarning
                {
                    EventId = $"cls_{i + 1:000}",
                    Issue = $"Unknown subject code '{row.SubjectCode}' for {studentYear}.",
                    Severity = "warning"
                });
            }

            var start = PeriodMap.TryGetValue(row.StartPeriod, out var startSlot) ? startSlot.Start : null;
            var end = PeriodMap.TryGetValue(row.EndPeriod, out var endSlot) ? endSlot.End : null;
            var lecturer = string.IsNullOrWhiteSpace(row.Lecturer) ? null : row.Lecturer;
            var room = string.IsNullOrWhiteSpace(row.Room) ? null : row.Room;

            if ((lecturer is null || room is null) &&
                TryResolveDay(row.DayCode, out var rowDayOfWeek) &&
                numericYear > 0 &&
                referenceService.TryGetSlotDetails(numericYear, studentGroup, rowDayOfWeek, row.StartPeriod, out _, out var slotLecturer, out var slotVenue))
            {
                lecturer ??= slotLecturer;
                room ??= slotVenue;
            }

            if (start is null || end is null)
            {
                warnings.Add(new RosebankWarning
                {
                    EventId = $"cls_{i + 1:000}",
                    Issue = $"Could not resolve period range {row.StartPeriod}-{row.EndPeriod}.",
                    Severity = "warning"
                });
            }

            if (lecturer is null || room is null)
            {
                warnings.Add(new RosebankWarning
                {
                    EventId = $"cls_{i + 1:000}",
                    Issue = "Venue or lecturer could not be confidently extracted; fallback value applied.",
                    Severity = "warning"
                });
            }

            events.Add(new RosebankClassEvent
            {
                Id = $"cls_{i + 1:000}",
                SubjectCode = row.SubjectCode,
                SubjectName = subjectName,
                DayOfWeek = DayMap.TryGetValue(row.DayCode, out var dayOfWeek) ? dayOfWeek : null,
                StartTime = start,
                EndTime = end,
                Room = room ?? "TBA",
                Lecturer = lecturer ?? "TBA",
                Confidence = confidence,
                Notes = null
            });
        }

        return (events, generatedDate);
    }

    private static List<string> ExtractGroupSection(IEnumerable<string> lines, string studentGroup)
    {
        var output = new List<string>();
        var normalizedGroup = studentGroup.ToUpperInvariant();
        var inSection = false;
        foreach (var line in lines)
        {
            var headerMatch = Regex.Match(line, @"\bGR(?<g>\d+)\b", RegexOptions.IgnoreCase);
            if (headerMatch.Success && line.Contains("Year", StringComparison.OrdinalIgnoreCase))
            {
                var current = $"GR{headerMatch.Groups["g"].Value}";
                inSection = string.Equals(current, normalizedGroup, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inSection)
            {
                output.Add(line);
            }
        }

        return output;
    }


    private static int ParseNumericYear(string studentYear)
    {
        var digits = new string(studentYear.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var year) ? year : 0;
    }

    private static bool TryResolveDay(string dayCode, out DayOfWeek day)
    {
        switch (dayCode.Trim().ToLowerInvariant())
        {
            case "mo":
                day = DayOfWeek.Monday;
                return true;
            case "tu":
                day = DayOfWeek.Tuesday;
                return true;
            case "we":
                day = DayOfWeek.Wednesday;
                return true;
            case "th":
                day = DayOfWeek.Thursday;
                return true;
            case "fr":
                day = DayOfWeek.Friday;
                return true;
            case "sa":
                day = DayOfWeek.Saturday;
                return true;
            default:
                day = default;
                return false;
        }
    }
}
