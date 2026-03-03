using System.Globalization;
using System.Text.RegularExpressions;
using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public sealed class AssessmentParser : IAssessmentParser
{
    private static readonly Regex RowPattern = new(
        "(?<code>[A-Z]{4}\\d{4})\\s+(?<name>.+?)\\s+(?<type>Practical\\s+Assignment\\s*\\d*|Practical\\s+Test\\s*\\d*|Theory\\s+Test\\s*\\d*|Final\\s+Exam|Assignment\\s*\\d*|Test\\s*\\d*|Exam|Project\\s*\\d*|Quiz\\s*\\d*|Presentation\\s*\\d*)\\s*(?<tail>.*?)\\s+(?<date>\\d{1,2}[-/](?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|\\d{1,2})[-/]\\d{2,4})\\s+(?<time>(?:[01]?\\d|2[0-3]):[0-5]\\d|23:59)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex SittingPattern = new(
        "Sitting\\s*(?<sitting>[12])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public AssessmentPreviewResponse Parse(string input)
    {
        var normalized = Regex.Replace(input, "\\s+", " ").Trim();
        var response = new AssessmentPreviewResponse
        {
            ExtractedText = input
        };

        var matches = RowPattern.Matches(normalized);
        foreach (Match match in matches)
        {
            var code = match.Groups["code"].Value.Trim().ToUpperInvariant();
            var name = match.Groups["name"].Value.Trim();
            var type = NormalizeText(match.Groups["type"].Value);
            var dateRaw = match.Groups["date"].Value.Trim();
            var timeRaw = match.Groups["time"].Value.Trim();
            var tail = match.Groups["tail"].Value;

            if (!TryParseDate(dateRaw, out var date))
            {
                response.Warnings.Add($"Skipped {code}: invalid date '{dateRaw}'.");
                continue;
            }

            if (!TimeOnly.TryParse(timeRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                response.Warnings.Add($"Skipped {code}: invalid time '{timeRaw}'.");
                continue;
            }

            int? sitting = null;
            var sittingMatch = SittingPattern.Match(tail);
            if (sittingMatch.Success)
            {
                sitting = int.Parse(sittingMatch.Groups["sitting"].Value, CultureInfo.InvariantCulture);
            }

            var deliveryMode = DetectDeliveryMode($"{type} {tail}");

            response.Events.Add(new AssessmentEvent
            {
                ModuleCode = code,
                ModuleName = NormalizeText(name),
                AssessmentType = type,
                Sitting = sitting,
                Date = date,
                Time = time,
                DeliveryMode = deliveryMode
            });
        }

        if (response.Events.Count == 0)
        {
            response.Warnings.Add("No assessment rows matched. Check PDF extraction and try preview with cleaner text.");
        }

        return response;
    }

    private static string DetectDeliveryMode(string value)
    {
        if (value.Contains("online", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("submission", StringComparison.OrdinalIgnoreCase))
        {
            return "Online Submission";
        }

        if (value.Contains("campus", StringComparison.OrdinalIgnoreCase))
        {
            return "Campus Sitting";
        }

        return "Unspecified";
    }

    private static bool TryParseDate(string input, out DateOnly date)
    {
        var formats = new[]
        {
            "d-MMM-yy", "dd-MMM-yy", "d-MMM-yyyy", "dd-MMM-yyyy",
            "d-M-yyyy", "dd-M-yyyy", "d-M-yy", "dd-M-yy"
        };

        foreach (var format in formats)
        {
            if (DateOnly.TryParseExact(input, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return true;
            }
        }

        return DateOnly.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static string NormalizeText(string value)
    {
        return Regex.Replace(value, "\\s+", " ").Trim();
    }
}
