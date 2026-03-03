using System.Globalization;
using System.Text.RegularExpressions;
using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public sealed class AssessmentParser : IAssessmentParser
{
    private static readonly Regex RowPattern = new(
        "(?<code>[A-Z]{4}\\d{4})\\s+(?<name>.+?)\\s+(?<type>Practical\\s+Assignment\\s*\\d*|Practical\\s+Test\\s*\\d*|Theory\\s+Test\\s*\\d*|Final\\s+Exam|Assignment\\s*\\d*|Test\\s*\\d*|Exam|Project\\s*\\d*|Quiz\\s*\\d*|Presentation\\s*\\d*)\\s*(?<tail>.*?)\\s+(?<date>\\d{1,2}[-/](?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|\\d{1,2})[-/]\\d{2,4})\\s+(?<time>(?:[01]?\\d|2[0-3]):[0-5]\\d|23:59)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex ModuleCodePattern = new(
        "\\b(?<code>[A-Z]{4}\\d{4})\\b",
        RegexOptions.Compiled);

    private static readonly Regex DatePattern = new(
        "\\b(?<date>\\d{1,2}[-/](?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|\\d{1,2})[-/]\\d{2,4})\\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TimePattern = new(
        "\\b(?<time>(?:[01]?\\d|2[0-3]):[0-5]\\d|23:59)\\b",
        RegexOptions.Compiled);

    private static readonly Regex AssessmentTypePattern = new(
        "(?<type>Practical\\s+Assignment\\s*\\d*|Practical\\s+Test\\s*\\d*|Theory\\s+Test\\s*\\d*|Final\\s+Exam|Assignment\\s*\\d*|Test\\s*\\d*|Exam|Project\\s*\\d*|Quiz\\s*\\d*|Presentation\\s*\\d*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ParseRowMatches(normalized, response, dedupe);
        ParseModuleBlocks(input, response, dedupe);

        if (response.Events.Count == 0)
        {
            response.Warnings.Add("No assessment rows matched. Check PDF extraction and try preview with cleaner text.");
        }

        return response;
    }

    private static void ParseRowMatches(string normalizedInput, AssessmentPreviewResponse response, ISet<string> dedupe)
    {
        var matches = RowPattern.Matches(normalizedInput);
        foreach (Match match in matches)
        {
            var code = match.Groups["code"].Value.Trim().ToUpperInvariant();
            var name = NormalizeText(match.Groups["name"].Value);
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
            AddEventIfUnique(response, dedupe, new AssessmentEvent
            {
                ModuleCode = code,
                ModuleName = name,
                AssessmentType = type,
                Sitting = sitting,
                Date = date,
                Time = time,
                DeliveryMode = deliveryMode
            });
        }
    }

    private static void ParseModuleBlocks(string rawInput, AssessmentPreviewResponse response, ISet<string> dedupe)
    {
        var codeMatches = ModuleCodePattern.Matches(rawInput);
        if (codeMatches.Count == 0)
        {
            return;
        }

        for (var i = 0; i < codeMatches.Count; i++)
        {
            var current = codeMatches[i];
            var start = current.Index;
            var end = i == codeMatches.Count - 1 ? rawInput.Length : codeMatches[i + 1].Index;
            var block = rawInput[start..end];
            var blockNormalized = NormalizeText(block);
            var code = current.Groups["code"].Value.Trim().ToUpperInvariant();

            var typeMatch = AssessmentTypePattern.Match(blockNormalized);
            var assessmentType = typeMatch.Success ? NormalizeText(typeMatch.Groups["type"].Value) : "Assessment";
            var name = ExtractModuleName(blockNormalized, code, typeMatch);
            var deliveryMode = DetectDeliveryMode(blockNormalized);

            var dateMatches = DatePattern.Matches(blockNormalized);
            var timeMatches = TimePattern.Matches(blockNormalized);
            var sittingMatches = SittingPattern.Matches(blockNormalized);

            if (dateMatches.Count == 0)
            {
                continue;
            }

            for (var dateIndex = 0; dateIndex < dateMatches.Count; dateIndex++)
            {
                var dateRaw = dateMatches[dateIndex].Groups["date"].Value;
                if (!TryParseDate(dateRaw, out var date))
                {
                    response.Warnings.Add($"Skipped {code}: invalid date '{dateRaw}'.");
                    continue;
                }

                var time = ResolveTimeForIndex(timeMatches, dateIndex, deliveryMode);
                int? sitting = null;
                if (sittingMatches.Count > 0)
                {
                    var sittingIdx = Math.Min(dateIndex, sittingMatches.Count - 1);
                    sitting = int.Parse(sittingMatches[sittingIdx].Groups["sitting"].Value, CultureInfo.InvariantCulture);
                }

                AddEventIfUnique(response, dedupe, new AssessmentEvent
                {
                    ModuleCode = code,
                    ModuleName = name,
                    AssessmentType = assessmentType,
                    Sitting = sitting,
                    Date = date,
                    Time = time,
                    DeliveryMode = deliveryMode
                });
            }
        }
    }

    private static string ExtractModuleName(string blockNormalized, string code, Match typeMatch)
    {
        var afterCode = blockNormalized.StartsWith(code, StringComparison.OrdinalIgnoreCase)
            ? blockNormalized[code.Length..].Trim()
            : blockNormalized;

        if (typeMatch.Success)
        {
            var typeIndex = afterCode.IndexOf(typeMatch.Value, StringComparison.OrdinalIgnoreCase);
            if (typeIndex > 0)
            {
                var name = afterCode[..typeIndex].Trim();
                if (name.Length > 0 && name.Length < 120)
                {
                    return name;
                }
            }
        }

        var headerTerms = new[] { "Programme", "Assessment", "Sitting", "Date", "Time", "Campus", "Online", "Term" };
        foreach (var term in headerTerms)
        {
            var idx = afterCode.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                afterCode = afterCode[..idx].Trim();
            }
        }

        return afterCode.Length <= 120 ? afterCode : string.Empty;
    }

    private static string DetectDeliveryMode(string value)
    {
        if (value.Contains("assignment", StringComparison.OrdinalIgnoreCase))
        {
            return "Online Submission";
        }

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

    private static TimeOnly ResolveTimeForIndex(MatchCollection timeMatches, int dateIndex, string deliveryMode)
    {
        if (timeMatches.Count > 0)
        {
            var idx = Math.Min(dateIndex, timeMatches.Count - 1);
            var raw = timeMatches[idx].Groups["time"].Value;
            if (TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }
        }

        return deliveryMode.Equals("Online Submission", StringComparison.OrdinalIgnoreCase)
            ? new TimeOnly(23, 59)
            : new TimeOnly(9, 0);
    }

    private static void AddEventIfUnique(AssessmentPreviewResponse response, ISet<string> dedupe, AssessmentEvent ev)
    {
        var key = $"{ev.ModuleCode}|{ev.AssessmentType}|{ev.Date:yyyy-MM-dd}|{ev.Time:HH:mm}|{ev.Sitting}";
        if (dedupe.Add(key))
        {
            response.Events.Add(ev);
        }
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
