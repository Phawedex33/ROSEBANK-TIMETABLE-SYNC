using TimetableSync.Api.Models;
using System.Text.RegularExpressions;

namespace TimetableSync.Api.Services;

public sealed class AcademicParser : IAcademicParser
{
    private static readonly Regex ModuleCodePattern = new("[A-Z]{4}\\d{4}", RegexOptions.Compiled);
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
            var fallback = BuildRosebankAssistedRows(input, group);
            if (fallback.Count > 0)
            {
                parsed.AcademicEvents.AddRange(fallback);
                parsed.Diagnostics.Add($"branch=rosebank_assisted_fallback rows={fallback.Count}");
                parsed.Warnings.Clear();
                parsed.Warnings.Add("Used assisted Rosebank fallback rows from detected module codes. Confirm day/time rows before syncing.");
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

    private static List<ClassEvent> BuildRosebankAssistedRows(string input, string group)
    {
        var chunk = TryExtractGroupChunk(input, group, out var groupChunk)
            ? groupChunk
            : input;

        var codes = ModuleCodePattern.Matches(chunk)
            .Select(m => m.Value.ToUpperInvariant())
            .ToList();

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
                Subject = codes[i]
            });
        }

        return rows;
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
