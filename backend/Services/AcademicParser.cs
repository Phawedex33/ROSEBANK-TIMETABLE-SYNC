using TimetableSync.Api.Models;
using System.Text.RegularExpressions;

namespace TimetableSync.Api.Services;

public sealed class AcademicParser : IAcademicParser
{
    private static readonly Regex ModuleCodePattern = new("[A-Z]{4}\\d{4}", RegexOptions.Compiled);
    private static readonly Regex LooseModulePattern = new("[A-Z]{4}\\d{2,4}", RegexOptions.Compiled);
    private static readonly Regex CodeWithDetailsPattern = new("(?<code>[A-Z]{4}\\d{4})(?<detail>.*?)(?<venue>PH\\s*\\d-\\d{2})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GroupMarkerPattern = new("3rd\\s*Year\\s*:\\s*GR(?<group>\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ITimetableParser _parser;

    public AcademicParser(ITimetableParser parser)
    {
        _parser = parser;
    }

    public AcademicPreviewResponse Parse(string input, int year, string group)
    {
        var parsed = _parser.Parse(input, ParseMode.Academic);
        if (parsed.AcademicEvents.Count == 0 &&
            input.Contains("Rosebank College", StringComparison.OrdinalIgnoreCase) &&
            input.Contains("3rd Year: GR", StringComparison.OrdinalIgnoreCase))
        {
            var fallback = BuildRosebankAssistedRows(input, group, year);
            if (fallback.Count > 0)
            {
                parsed.AcademicEvents.AddRange(fallback);
                parsed.Diagnostics.Add($"branch=rosebank_assisted_fallback rows={fallback.Count}");
                parsed.Warnings.Clear();
                parsed.Warnings.Add("Used assisted Rosebank fallback rows from detected module codes. Confirm day/period before syncing.");
                if (fallback.Any(e => e.Lecturer == "TBA" || e.Venue == "TBA"))
                {
                    parsed.Warnings.Add("Lecturer/venue values marked 'TBA' are unresolved from the source PDF and should be treated as unknown.");
                }
            }
            else
            {
                parsed.Warnings.Add("Rosebank class timetable PDF text is layout-compressed. Upload as image (PNG/JPG) for OCR-based extraction, then confirm rows in the editable preview.");
            }
        }

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

    private static List<ClassEvent> BuildRosebankAssistedRows(string input, string group, int year)
    {
        var chunk = TryExtractGroupChunk(input, group, out var groupChunk)
            ? groupChunk
            : input;

        var codes = DetectModuleCodes(chunk, year).ToList();
        var detailsByCode = ExtractDetailsByCode(chunk);

        if (codes.Count == 0)
        {
            return new List<ClassEvent>();
        }

        var slots = new (DayOfWeek Day, TimeOnly Start, TimeOnly End)[]
        {
            (DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(8, 50)),
            (DayOfWeek.Tuesday, new TimeOnly(8, 0), new TimeOnly(8, 50)),
            (DayOfWeek.Wednesday, new TimeOnly(8, 0), new TimeOnly(8, 50)),
            (DayOfWeek.Thursday, new TimeOnly(8, 0), new TimeOnly(8, 50)),
            (DayOfWeek.Friday, new TimeOnly(8, 0), new TimeOnly(8, 50)),
            (DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(9, 50)),
            (DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(9, 50)),
            (DayOfWeek.Wednesday, new TimeOnly(9, 0), new TimeOnly(9, 50)),
            (DayOfWeek.Thursday, new TimeOnly(9, 0), new TimeOnly(9, 50)),
            (DayOfWeek.Friday, new TimeOnly(9, 0), new TimeOnly(9, 50)),
            (DayOfWeek.Monday, new TimeOnly(10, 0), new TimeOnly(10, 50)),
            (DayOfWeek.Tuesday, new TimeOnly(10, 0), new TimeOnly(10, 50))
        };

        var rows = new List<ClassEvent>();
        var capped = Math.Min(codes.Count, slots.Length);
        for (var i = 0; i < capped; i++)
        {
            var slot = slots[i];
            rows.Add(new ClassEvent
            {
                Day = slot.Day,
                StartTime = slot.Start,
                EndTime = slot.End,
                Subject = codes[i],
                Lecturer = ResolveLecturer(codes[i], detailsByCode),
                Venue = ResolveVenue(codes[i], detailsByCode)
            });
        }

        return rows;
    }

    private static Dictionary<string, (string Lecturer, string Venue)> ExtractDetailsByCode(string chunk)
    {
        var map = new Dictionary<string, (string Lecturer, string Venue)>(StringComparer.OrdinalIgnoreCase);
        var matches = CodeWithDetailsPattern.Matches(chunk);
        foreach (Match match in matches)
        {
            var code = match.Groups["code"].Value.ToUpperInvariant();
            var venue = NormalizeVenue(match.Groups["venue"].Value);
            var lecturer = NormalizeLecturer(match.Groups["detail"].Value);

            if (!map.ContainsKey(code))
            {
                map[code] = (lecturer, venue);
            }
        }

        return map;
    }

    private static string ResolveLecturer(
        string code,
        IReadOnlyDictionary<string, (string Lecturer, string Venue)> extracted)
    {
        if (extracted.TryGetValue(code, out var ext) && !string.IsNullOrWhiteSpace(ext.Lecturer))
        {
            return ext.Lecturer;
        }

        return "TBA";
    }

    private static string ResolveVenue(
        string code,
        IReadOnlyDictionary<string, (string Lecturer, string Venue)> extracted)
    {
        if (extracted.TryGetValue(code, out var ext) && !string.IsNullOrWhiteSpace(ext.Venue))
        {
            return ext.Venue;
        }

        return "TBA";
    }

    private static string NormalizeVenue(string raw)
    {
        return Regex.Replace(raw ?? string.Empty, "\\s+", " ").Trim().ToUpperInvariant();
    }

    private static string NormalizeLecturer(string raw)
    {
        var cleaned = Regex.Replace(raw ?? string.Empty, "[^A-Za-z\\s]", " ");
        cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim();
        if (cleaned.Length > 28)
        {
            cleaned = cleaned[..28].Trim();
        }

        return cleaned;
    }

    private static IEnumerable<string> DetectModuleCodes(string chunk, int year)
    {
        var known = GetKnownYearModuleCodes(year);
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in ModuleCodePattern.Matches(chunk))
        {
            var code = m.Value.ToUpperInvariant();
            if (seen.Add(code))
            {
                ordered.Add(code);
            }
        }

        foreach (Match m in LooseModulePattern.Matches(chunk))
        {
            var token = m.Value.ToUpperInvariant();
            if (token.Length < 6)
            {
                continue;
            }

            var prefix = token[..4];
            var digits = token[4..];
            var mapped = known.FirstOrDefault(k =>
                k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                k[4..].StartsWith(digits, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(mapped) && seen.Add(mapped))
            {
                ordered.Add(mapped);
            }
        }

        foreach (var knownCode in known)
        {
            if (seen.Add(knownCode))
            {
                ordered.Add(knownCode);
            }
        }

        return ordered;
    }

    private static IReadOnlyList<string> GetKnownYearModuleCodes(int year)
    {
        return year switch
        {
            3 => new[] { "ADDB6311", "OPSC6311", "WEDE6021", "XISD5319" },
            _ => Array.Empty<string>()
        };
    }

    private static bool TryExtractGroupChunk(string input, string group, out string chunk)
    {
        chunk = string.Empty;
        var groupNumber = Regex.Match(group ?? string.Empty, "\\d+").Value;
        if (string.IsNullOrWhiteSpace(groupNumber))
        {
            return false;
        }

        var markers = GroupMarkerPattern.Matches(input);
        if (markers.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            var markerGroup = marker.Groups["group"].Value;
            if (!string.Equals(markerGroup, groupNumber, StringComparison.Ordinal))
            {
                continue;
            }

            var nextStart = i < markers.Count - 1 ? markers[i + 1].Index : input.Length;
            var prevStart = i == 0 ? 0 : markers[i - 1].Index + markers[i - 1].Length;

            var forwardChunk = marker.Index < nextStart ? input[marker.Index..nextStart] : string.Empty;
            var backwardChunk = prevStart < marker.Index + marker.Length ? input[prevStart..(marker.Index + marker.Length)] : string.Empty;

            var forwardScore = ModuleCodePattern.Matches(forwardChunk).Count;
            var backwardScore = ModuleCodePattern.Matches(backwardChunk).Count;

            chunk = forwardScore >= backwardScore ? forwardChunk : backwardChunk;
            if (string.IsNullOrWhiteSpace(chunk))
            {
                return false;
            }

            return true;
        }

        return false;
    }
}
