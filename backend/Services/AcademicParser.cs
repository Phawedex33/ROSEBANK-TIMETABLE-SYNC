using TimetableSync.Api.Models;
using System.Text.RegularExpressions;

namespace TimetableSync.Api.Services;

public sealed class AcademicParser : IAcademicParser
{
    private static readonly Regex ModuleCodePattern = new(@"[A-Z]{4}\s*\d{3,4}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LooseModulePattern = new(@"[A-Z]{3,4}\s*\d{2,4}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CodeWithDetailsPattern = new(@"(?<code>[A-Z]{2,4}\s*\d{2,4})\s*(?<detail1>.*?)\s*(?<venue>\b(?:(?:PH|MGB|[A-C])\s*\d[-\d]*|Room\s*\d+|Lab\s*\d+|Online|Campus|Hall|Auditorium|Gym|Grounds)\b)\s*(?<detail2>.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GroupMarkerPattern = new(@"\d+(?:st|nd|rd|th)\s*Year\s*:\s*GR(?<group>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ITimetableParser _parser;
    private readonly IRosebankReferenceService _referenceService;

    public AcademicParser(ITimetableParser parser, IRosebankReferenceService referenceService)
    {
        _parser = parser;
        _referenceService = referenceService;
    }

    public AcademicPreviewResponse Parse(string input, int year, string group)
    {
        var parsed = _parser.Parse(input, ParseMode.Academic);

        if (!string.IsNullOrWhiteSpace(group))
        {
            // Only filter if at least one event has a group tag — prevents filtering
            var groupTag = " | " + group.ToUpperInvariant();
            if (parsed.AcademicEvents.Any(e => e.Subject.Contains(" | GR", StringComparison.OrdinalIgnoreCase)))
            {
                // Smart range filtering: "GR1-GR3" should match if searching for "GR2"
                parsed.AcademicEvents.RemoveAll(e => !IsGroupMatch(e.Subject, group));
            }
        }

        ApplyReferenceDetails(parsed.AcademicEvents, year, group, parsed.Diagnostics);
        ValidateYearAndGroup(input, year, group, parsed.Warnings, parsed.Diagnostics);

        return new AcademicPreviewResponse
        {
            Year = year,
            Group = group,
            ExtractedText = input,
            Events = parsed.AcademicEvents,
            Warnings = parsed.Warnings,
            Diagnostics = parsed.Diagnostics
        };
    }

    private static void ValidateYearAndGroup(string input, int year, string group, ICollection<string> warnings, ICollection<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var ordinalYear = year switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{year}th"
        };
        
        // Lenient year check: look for "3rd" and "Year" within the same block
        bool foundYear = input.Contains($"{ordinalYear} Year", StringComparison.OrdinalIgnoreCase) || 
                         (input.Contains(ordinalYear, StringComparison.OrdinalIgnoreCase) && input.Contains("Year", StringComparison.OrdinalIgnoreCase));

        if (!foundYear)
        {
            var detectedYears = new List<string>();
            if (input.Contains("1st", StringComparison.OrdinalIgnoreCase)) detectedYears.Add("1st Year");
            if (input.Contains("2nd", StringComparison.OrdinalIgnoreCase)) detectedYears.Add("2nd Year");
            if (input.Contains("3rd", StringComparison.OrdinalIgnoreCase)) detectedYears.Add("3rd Year");

            if (detectedYears.Count > 0)
            {
                warnings.Add($"Year mismatch: You selected {ordinalYear} Year, but the timetable appears to be for {string.Join(" or ", detectedYears)}.");
                diagnostics.Add("branch=academic_year_mismatch");
            }
            else
            {
                warnings.Add($"Year not found: '{ordinalYear} Year' was not detected in the timetable text.");
            }
        }

        if (!string.IsNullOrWhiteSpace(group))
        {
            // Lenient group check: some PDFs might have "GR 1" instead of "GR1"
            var digits = new string(group.Where(char.IsDigit).ToArray());
            var altGroup = $"GR {digits}";
            if (!input.Contains(group, StringComparison.OrdinalIgnoreCase) && !input.Contains(altGroup, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Group mismatch: The group '{group}' was not safely found in the timetable text.");
                diagnostics.Add("branch=academic_group_not_found");
            }
        }
    }

    private void ApplyReferenceDetails(List<ClassEvent> events, int year, string group, ICollection<string> diagnostics)
    {
        if (events.Count == 0)
        {
            return;
        }

        var matched = 0;
        var rewritten = new List<ClassEvent>(events.Count);
        foreach (var e in events)
        {
            var moduleCode = ExtractModuleCode(e.Subject);
            var lecturer = "TBA";
            var venue = "TBA";

            // Priority 1: exact slot lookup — most accurate for rotating schedules
            if (_referenceService.TryGetSlotDetails(year, group, e.Day, ResolvePeriod(e.StartTime), out var slotModuleCode, out var slotLecturer, out var slotVenue))
            {
                moduleCode = slotModuleCode;
                lecturer = slotLecturer;
                venue = slotVenue;
                matched++;
            }
            // Priority 2: module-code lookup (fallback for simple timetables)
            else if (!string.IsNullOrWhiteSpace(moduleCode) && _referenceService.TryGetClassDetails(year, group, moduleCode, out var refLecturer, out var refVenue))
            {
                lecturer = refLecturer;
                venue = refVenue;
                matched++;
            }
            // Priority 2.5: Global module lookup (if group-specific fails, try any group to at least get a guess)
            else if (!string.IsNullOrWhiteSpace(moduleCode) && TryGetGlobalReference(moduleCode, out var globalLecturer, out var globalVenue))
            {
                lecturer = globalLecturer;
                venue = globalVenue;
                matched++;
            }
            else
            {
                // Priority 3: extract directly from raw subject string (PDF cell text)
                var detailMatch = CodeWithDetailsPattern.Match(e.Subject ?? string.Empty);
                if (detailMatch.Success)
                {
                    venue = detailMatch.Groups["venue"].Value.Trim().ToUpperInvariant();
                    var d1 = detailMatch.Groups["detail1"].Value.Trim();
                    var d2 = detailMatch.Groups["detail2"].Value.Trim();
                    lecturer = Regex.Replace((d1 + " " + d2).Trim(), @"[^A-Za-z\s]", "").Trim();
                    if (lecturer.Length > 28) lecturer = lecturer[..25] + "...";
                }
            }

            rewritten.Add(new ClassEvent
            {
                Day = e.Day,
                StartTime = e.StartTime,
                EndTime = e.EndTime,
                Subject = string.IsNullOrWhiteSpace(moduleCode) ? (e.Subject ?? string.Empty) : moduleCode,
                Lecturer = string.IsNullOrWhiteSpace(lecturer) ? "TBA" : lecturer,
                Venue = string.IsNullOrWhiteSpace(venue) ? "TBA" : venue
            });
        }

        events.Clear();
        events.AddRange(rewritten);

        if (matched > 0)
        {
            diagnostics.Add($"branch=rosebank_reference_lookup matched={matched}/{events.Count} year={year} group={group}");
        }
    }

    private static string ExtractModuleCode(string? subject)
    {
        var match = LooseModulePattern.Match(subject ?? string.Empty);
        return match.Success ? match.Value.ToUpperInvariant().Replace(" ", "") : string.Empty;
    }

    private static bool IsGroupMatch(string? subject, string searchGroup)
    {
        if (string.IsNullOrWhiteSpace(subject)) return false;
        if (string.IsNullOrWhiteSpace(searchGroup)) return true;

        var searchNumMatch = Regex.Match(searchGroup, @"\d+");
        if (!searchNumMatch.Success) return true;
        int searchNum = int.Parse(searchNumMatch.Value);

        // Case 1: Direct match for the group (e.g. "GR1", "GR 1", "GROUP 1")
        if (Regex.IsMatch(subject, @"\bGR\s*" + searchNum + @"\b", RegexOptions.IgnoreCase)) return true;

        // Case 2: Range match (e.g. "GR1-GR3")
        var ranges = Regex.Matches(subject, @"GR\s*(?<start>\d+)\s*[-&]\s*GR\s*(?<end>\d+)", RegexOptions.IgnoreCase);
        foreach (Match m in ranges)
        {
            if (int.Parse(m.Groups["start"].Value) <= searchNum && int.Parse(m.Groups["end"].Value) >= searchNum) return true;
        }

        // Case 3: If the subject has NO group markers at all, it's a generic entry, so accept it.
        // Otherwise, if it has markers but none matched above, reject it.
        return !Regex.IsMatch(subject, @"\bGR\s*\d+", RegexOptions.IgnoreCase);
    }

    private bool TryGetGlobalReference(string moduleCode, out string lecturer, out string venue)
    {
        lecturer = "TBA";
        venue = "TBA";

        // Search across common groups (GR1, GR2, GR3)
        foreach (var g in new[] { "GR1", "GR2", "GR3", "GR4", "GR5" })
        {
            if (_referenceService.TryGetClassDetails(1, g, moduleCode, out lecturer, out venue) ||
                _referenceService.TryGetClassDetails(2, g, moduleCode, out lecturer, out venue) ||
                _referenceService.TryGetClassDetails(3, g, moduleCode, out lecturer, out venue))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Converts a start time to a period number (1–12) matching the Rosebank period map.</summary>
    private static int ResolvePeriod(TimeOnly startTime)
    {
        return startTime.Hour switch
        {
            8  => 1,
            9  => 2,
            10 => 3,
            11 => 4,
            12 => 5,
            13 => 6,
            14 => 7,
            15 => 8,
            16 => 9,
            17 => 10,
            18 => 11,
            19 => 12,
            _  => 0
        };
    }
}
