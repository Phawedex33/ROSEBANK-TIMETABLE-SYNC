using System.Globalization;
using System.Text.RegularExpressions;
using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public sealed class AssessmentParser : IAssessmentParser
{
    private const string DateToken = "\\d{1,2}(?:[-/ ]+)(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|\\d{1,2})(?:[-/ ]+)\\d{2,4}";
    private const string TimeToken = "(?:[01]?\\d|2[0-3])[:h][0-5]\\d|23:59";

    private static readonly Regex SegmentPattern = new(
        "(?<prog>DIS\\d)\\s+(?<code>[A-Z]{4}\\d{4})\\s+(?<name>.+?)\\s+(?<type>Practical\\s+Assignment\\s*\\d*(?:\\s+Deferred)?|Practical\\s+Test\\s*\\d*(?:\\s+Deferred)?|Theory\\s+Test\\s*\\d*(?:\\s+Deferred)?|Final\\s+Exam|Assignment\\s*\\d*(?:\\s+Deferred)?|Project\\s*\\d*(?:\\s+Deferred)?|Test\\s*\\d*(?:\\s+Sitting\\s*[12])?|Part\\s*\\d*(?:\\s+Deferred)?|Task\\s*\\d*(?:\\s+Deferred)?|Exam|Quiz\\s*\\d*|Presentation\\s*\\d*)\\s+(?<delivery>Campus\\s+Sitting|Online\\s+Submission(?:\\s+Turnitin)?)\\s+(?<date>" + DateToken + ")\\s+(?<time>" + TimeToken + ")(?=\\s+DIS\\d\\s+[A-Z]{4}\\d{4}\\b|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex RowPattern = new(
        "(?<code>[A-Z]{4}\\d{4})\\s+(?<name>.+?)\\s+(?<type>Practical\\s+Assignment\\s*\\d*|Practical\\s+Test\\s*\\d*|Theory\\s+Test\\s*\\d*|Final\\s+Exam|Assignment\\s*\\d*|Test\\s*\\d*|Exam|Project\\s*\\d*|Quiz\\s*\\d*|Presentation\\s*\\d*)\\s*(?<tail>.*?)\\s+(?<date>" + DateToken + ")\\s+(?<time>" + TimeToken + ")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex PasLinePattern = new(
        "^DIS\\d\\s+(?<code>[A-Z]{4}\\d{4})\\s+(?<name>.+?)\\s+(?<type>Practical\\s+Assignment\\s*\\d*|Practical\\s+Test\\s*\\d*|Theory\\s+Test\\s*\\d*|Final\\s+Exam|Assignment\\s*\\d*(?:\\s+Deferred)?|Project\\s*\\d*(?:\\s+Deferred)?|Test\\s*\\d*(?:\\s+Sitting\\s*[12])?|Part\\s*\\d*(?:\\s+Deferred)?|Task\\s*\\d*(?:\\s+Deferred)?|Exam|Quiz\\s*\\d*|Presentation\\s*\\d*)\\s+(?<delivery>Campus\\s+Sitting|Online\\s+Submission(?:\\s+Turnitin)?)\\s+(?<date>" + DateToken + ")\\s+(?<time>" + TimeToken + ")$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ModuleCodePattern = new(
        "\\b(?<code>[A-Z]{4}\\d{4})\\b",
        RegexOptions.Compiled);

    private static readonly Regex DatePattern = new(
        "\\b(?<date>" + DateToken + ")\\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TimePattern = new(
        "\\b(?<time>" + TimeToken + ")\\b",
        RegexOptions.Compiled);

    private static readonly Regex AssessmentTypePattern = new(
        "(?<type>Practical\\s+Assignment\\s*\\d*|Practical\\s+Test\\s*\\d*|Theory\\s+Test\\s*\\d*|Final\\s+Exam|Assignment\\s*\\d*|Test\\s*\\d*|Exam|Project\\s*\\d*|Quiz\\s*\\d*|Presentation\\s*\\d*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SittingPattern = new(
        "Sitting\\s*(?<sitting>[12])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ProgramCodePattern = new(
        "\\bDIS\\d\\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public AssessmentPreviewResponse Parse(string input)
    {
        var preprocessed = ExpandCompactPasText(input);
        var normalized = Regex.Replace(preprocessed, "\\s+", " ").Trim();
        var response = new AssessmentPreviewResponse
        {
            ExtractedText = preprocessed
        };
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ParseSegmentMatches(normalized, response, dedupe);
        if (response.Events.Count == 0)
        {
            ParseRowMatches(preprocessed, normalized, response, dedupe);
            ParseModuleBlocks(preprocessed, response, dedupe);
        }

        if (response.Events.Count == 0)
        {
            response.Warnings.Add("No assessment rows matched. Check PDF extraction and try preview with cleaner text.");
        }
        else
        {
            PruneLowerQualityDuplicates(response);
        }

        return response;
    }

    private static void ParseSegmentMatches(string normalizedInput, AssessmentPreviewResponse response, ISet<string> dedupe)
    {
        var matches = SegmentPattern.Matches(normalizedInput);
        foreach (Match match in matches)
        {
            var code = match.Groups["code"].Value.Trim().ToUpperInvariant();
            var name = NormalizeText(match.Groups["name"].Value);
            var type = NormalizeText(match.Groups["type"].Value);
            var dateRaw = match.Groups["date"].Value.Trim();
            var timeRaw = match.Groups["time"].Value.Trim();
            var deliveryRaw = NormalizeText(match.Groups["delivery"].Value);

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
            var sittingMatch = SittingPattern.Match(type);
            if (sittingMatch.Success)
            {
                sitting = int.Parse(sittingMatch.Groups["sitting"].Value, CultureInfo.InvariantCulture);
                type = Regex.Replace(type, "\\s+Sitting\\s*[12]", string.Empty, RegexOptions.IgnoreCase).Trim();
            }

            AddEventIfUnique(response, dedupe, new AssessmentEvent
            {
                ModuleCode = code,
                ModuleName = name,
                AssessmentType = type,
                Sitting = sitting,
                Date = date,
                Time = time,
                DeliveryMode = DetectDeliveryMode(deliveryRaw)
            }, $"branch=pas_segment_regex module={code}");
        }
    }

    private static void ParseRowMatches(string preprocessedInput, string normalizedInput, AssessmentPreviewResponse response, ISet<string> dedupe)
    {
        var lines = preprocessedInput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var rawLine in lines)
        {
            var line = NormalizeText(rawLine);
            var lineMatch = PasLinePattern.Match(line);
            if (!lineMatch.Success)
            {
                continue;
            }

            var code = lineMatch.Groups["code"].Value.Trim().ToUpperInvariant();
            var name = NormalizeText(lineMatch.Groups["name"].Value);
            var type = NormalizeText(lineMatch.Groups["type"].Value);
            var dateRaw = lineMatch.Groups["date"].Value.Trim();
            var timeRaw = lineMatch.Groups["time"].Value.Trim();
            var deliveryText = lineMatch.Groups["delivery"].Value;

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
            var sittingMatch = SittingPattern.Match(type);
            if (sittingMatch.Success)
            {
                sitting = int.Parse(sittingMatch.Groups["sitting"].Value, CultureInfo.InvariantCulture);
                type = Regex.Replace(type, "\\s+Sitting\\s*[12]", string.Empty, RegexOptions.IgnoreCase).Trim();
            }

            AddEventIfUnique(response, dedupe, new AssessmentEvent
            {
                ModuleCode = code,
                ModuleName = name,
                AssessmentType = type,
                Sitting = sitting,
                Date = date,
                Time = time,
                DeliveryMode = DetectDeliveryMode(deliveryText)
            }, $"branch=pas_line_regex module={code}");
        }

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
            }, $"branch=pas_row_regex module={code}");
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
                }, $"branch=pas_module_block module={code} dateIndex={dateIndex + 1}");
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

        return afterCode.Length <= 140 ? afterCode : string.Empty;
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
            var raw = timeMatches[idx].Groups["time"].Value.Replace("h", ":", StringComparison.OrdinalIgnoreCase);
            if (TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }
        }

        return deliveryMode.Equals("Online Submission", StringComparison.OrdinalIgnoreCase)
            ? new TimeOnly(23, 59)
            : new TimeOnly(9, 0);
    }

    private static void AddEventIfUnique(
        AssessmentPreviewResponse response,
        ISet<string> dedupe,
        AssessmentEvent ev,
        string diagnostic)
    {
        if (TryGetSuspiciousEventReason(ev, out var suspiciousReason))
        {
            response.Warnings.Add($"Skipped suspicious assessment row for {ev.ModuleCode}: {suspiciousReason}");
            response.Diagnostics.Add($"branch=suspicious_skip module={ev.ModuleCode} reason={suspiciousReason}");
            return;
        }

        var key = $"{ev.ModuleCode}|{ev.AssessmentType}|{ev.Date:yyyy-MM-dd}|{ev.Time:HH:mm}|{ev.Sitting}";
        if (dedupe.Add(key))
        {
            response.Events.Add(ev);
            response.Diagnostics.Add($"{diagnostic} key={key}");
        }
        else
        {
            response.Diagnostics.Add($"branch=dedupe_skip key={key}");
        }
    }

    private static bool TryParseDate(string input, out DateOnly date)
    {
        var formats = new[]
        {
            "d-MMM-yy", "dd-MMM-yy", "d-MMM-yyyy", "dd-MMM-yyyy",
            "d-M-yyyy", "dd-M-yyyy", "d-M-yy", "dd-M-yy",
            "d MMM yy", "dd MMM yy", "d MMMyy", "dd MMMyy",
            "d MMM yyyy", "dd MMM yyyy"
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

    private static bool TryGetSuspiciousEventReason(AssessmentEvent ev, out string reason)
    {
        var moduleName = NormalizeText(ev.ModuleName);
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            reason = string.Empty;
            return false;
        }

        if (moduleName.Length > 120)
        {
            reason = "module name is implausibly long";
            return true;
        }

        var extraModuleCodes = ModuleCodePattern.Matches(moduleName)
            .Select(x => x.Groups["code"].Value.Trim().ToUpperInvariant())
            .Where(code => !string.Equals(code, ev.ModuleCode, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (extraModuleCodes.Count > 0)
        {
            reason = $"module name contains other module codes ({string.Join(", ", extraModuleCodes)})";
            return true;
        }

        if (ProgramCodePattern.IsMatch(moduleName))
        {
            reason = "module name contains programme markers";
            return true;
        }

        if (DatePattern.Matches(moduleName).Count > 0 || TimePattern.Matches(moduleName).Count > 0)
        {
            reason = "module name contains date or time tokens";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static void PruneLowerQualityDuplicates(AssessmentPreviewResponse response)
    {
        var ranked = response.Events
            .Select((ev, index) => new RankedAssessmentEvent(ev, index, CalculateQualityScore(ev)))
            .ToList();

        var removeIndexes = new HashSet<int>();

        foreach (var group in ranked.GroupBy(x => BuildSlotKey(x.Event)))
        {
            var bestScore = group.Max(x => x.Score);
            var bestEvents = group.Where(x => x.Score == bestScore).ToList();
            var hasSpecificEvent = bestEvents.Any(x => !IsGenericAssessmentType(x.Event.AssessmentType));

            foreach (var candidate in group)
            {
                if (candidate.Score < bestScore)
                {
                    removeIndexes.Add(candidate.Index);
                    response.Diagnostics.Add($"branch=quality_prune_skip key={BuildSlotKey(candidate.Event)} type={candidate.Event.AssessmentType}");
                    continue;
                }

                if (hasSpecificEvent &&
                    candidate.Score == bestScore &&
                    IsGenericAssessmentType(candidate.Event.AssessmentType))
                {
                    removeIndexes.Add(candidate.Index);
                    response.Diagnostics.Add($"branch=generic_prune_skip key={BuildSlotKey(candidate.Event)} type={candidate.Event.AssessmentType}");
                }
            }
        }

        if (removeIndexes.Count > 0)
        {
            response.Events = response.Events
                .Where((_, index) => !removeIndexes.Contains(index))
                .ToList();
        }
    }

    private static string BuildSlotKey(AssessmentEvent ev)
    {
        return $"{ev.ModuleCode}|{ev.Date:yyyy-MM-dd}|{ev.Time:HH:mm}|{ev.Sitting}";
    }

    private static int CalculateQualityScore(AssessmentEvent ev)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(ev.ModuleName)) score += 2;
        if (!IsGenericAssessmentType(ev.AssessmentType))
        {
            score += 8;
        }
        else
        {
            score -= 5;
        }
        if (ev.AssessmentType.Contains("Deferred", StringComparison.OrdinalIgnoreCase)) score += 2;
        if (ev.DeliveryMode.Contains("Online", StringComparison.OrdinalIgnoreCase) ||
            ev.DeliveryMode.Contains("Campus", StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
        }

        score += NormalizeAssessmentType(ev.AssessmentType).Length;
        return score;
    }

    private static bool IsGenericAssessmentType(string value)
    {
        return NormalizeAssessmentType(value).Equals("assessment", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAssessmentType(string value)
    {
        var normalized = NormalizeText(value).ToLowerInvariant();
        normalized = Regex.Replace(normalized, "\\bdeferred\\b", string.Empty);
        normalized = Regex.Replace(normalized, "\\bpractical\\b", string.Empty);
        normalized = Regex.Replace(normalized, "\\btheory\\b", string.Empty);
        normalized = Regex.Replace(normalized, "\\bfinal\\b", string.Empty);
        normalized = Regex.Replace(normalized, "\\s+", " ").Trim();
        return normalized;
    }

    private sealed record RankedAssessmentEvent(AssessmentEvent Event, int Index, int Score);

    private static string NormalizeText(string value)
    {
        return Regex.Replace(value, "\\s+", " ").Trim();
    }

    private static string ExpandCompactPasText(string input)
    {
        var expanded = input;
        expanded = Regex.Replace(expanded, "(?<=[a-z\\)])(?=[A-Z])", " ");
        expanded = Regex.Replace(expanded, "(?<=\\d)(?=[A-Z])", " ");
        expanded = Regex.Replace(expanded, "(?<=[0-9])(?=DIS\\d\\b)", "\n");
        expanded = Regex.Replace(expanded, "(?<=[0-9])(?=[A-Z]{4}\\d{4}\\b)", " ");
        expanded = Regex.Replace(expanded, "(?<=[A-Za-z])(?=\\d{1,2}(?:[-/ ]?)[A-Za-z]{3}(?:[-/ ]?)\\d{2,4})", " ");
        expanded = Regex.Replace(expanded, "(?<=[A-Za-z])(?=\\d{2}:\\d{2})", " ");
        expanded = Regex.Replace(expanded, "(?<date>\\d{1,2}(?:[-/ ]?)[A-Za-z]{3}(?:[-/ ]?)\\d{2,4})(?<time>\\d{2}:\\d{2})", "${date} ${time}");
        expanded = Regex.Replace(expanded, "(?<dis>DIS\\d)(?<code>[A-Z]{4}\\d{4})", "${dis} ${code}");
        expanded = Regex.Replace(expanded, "[ \\t\\r\\f\\v]+", " ");
        expanded = expanded.Replace(" DIS1 ", "\nDIS1 ");
        expanded = expanded.Replace(" DIS2 ", "\nDIS2 ");
        expanded = expanded.Replace(" DIS3 ", "\nDIS3 ");
        expanded = Regex.Replace(expanded, " *\\n *", "\n");
        return expanded.Trim();
    }
}
