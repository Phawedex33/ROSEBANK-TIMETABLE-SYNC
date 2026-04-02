using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public sealed class AiParsingService : IAiParsingService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiParsingService> _logger;

    private static readonly Dictionary<string, DayOfWeek> DayAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["monday"] = DayOfWeek.Monday, ["mon"] = DayOfWeek.Monday, ["mo"] = DayOfWeek.Monday,
        ["tuesday"] = DayOfWeek.Tuesday, ["tue"] = DayOfWeek.Tuesday, ["tu"] = DayOfWeek.Tuesday,
        ["wednesday"] = DayOfWeek.Wednesday, ["wed"] = DayOfWeek.Wednesday, ["we"] = DayOfWeek.Wednesday,
        ["thursday"] = DayOfWeek.Thursday, ["thu"] = DayOfWeek.Thursday, ["th"] = DayOfWeek.Thursday,
        ["friday"] = DayOfWeek.Friday, ["fri"] = DayOfWeek.Friday, ["fr"] = DayOfWeek.Friday,
        ["saturday"] = DayOfWeek.Saturday, ["sat"] = DayOfWeek.Saturday, ["sa"] = DayOfWeek.Saturday,
        ["sunday"] = DayOfWeek.Sunday, ["sun"] = DayOfWeek.Sunday, ["su"] = DayOfWeek.Sunday,
    };

    public AiParsingService(HttpClient httpClient, IConfiguration configuration, ILogger<AiParsingService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<TimetableParseResult> ParseAcademicAsync(string text, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return Skipped("Gemini API key is missing. AI parsing skipped.");

        if (string.IsNullOrWhiteSpace(text))
            return Skipped("No text provided for AI parsing.");

        var prompt = $@"
You are parsing a university academic timetable. Extract all class schedule entries from the text below.

Return ONLY a valid JSON object — no markdown, no explanation. Use this exact schema:
{{
  ""events"": [
    {{
      ""day"": ""Monday"",           // Full day name: Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday
      ""startTime"": ""08:00"",      // 24-hour HH:mm format
      ""endTime"": ""08:50"",        // 24-hour HH:mm format
      ""subject"": ""DISD5311"",     // Module code or subject name
      ""lecturer"": ""J Smith"",     // Lecturer/tutor name, or ""TBA"" if unknown
      ""venue"": ""PH201"",          // Room/venue code, or ""TBA"" if unknown
      ""group"": ""GR1""            // Student group (e.g. GR1, GR2), or null if not specified
    }}
  ]
}}

Rules:
- If a period number is given (e.g. Period 1 = 08:00-08:50, Period 2 = 09:00-09:50, etc.) convert it.
- Ignore header rows, footer text, and page metadata.
- If multiple groups share an event, create one entry per group.

Timetable text to parse:
---
{text}
---
";

        return await CallGeminiAsync(prompt, apiKey, ParseMode.Academic, cancellationToken);
    }

    public async Task<TimetableParseResult> ParseExamAsync(string text, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return Skipped("Gemini API key is missing. AI parsing skipped.");

        if (string.IsNullOrWhiteSpace(text))
            return Skipped("No text provided for AI parsing.");

        var prompt = $@"
You are parsing a university assessment/exam timetable. Extract all assessment entries from the text below.

Return ONLY a valid JSON object — no markdown, no explanation. Use this exact schema:
{{
  ""events"": [
    {{
      ""moduleCode"": ""DISD5311"",         // Module code in UPPERCASE
      ""moduleName"": ""Digital Strategy"", // Full module name
      ""assessmentType"": ""Exam"",         // One of: Exam, Test, Assignment, Practical, Quiz, Presentation, Project
      ""date"": ""2026-06-15"",             // yyyy-MM-dd format
      ""time"": ""09:00"",                  // 24-hour HH:mm format. Use 23:59 for online submissions.
      ""sitting"": 1,                       // 1 or 2 for multiple sittings, or null
      ""deliveryMode"": ""Campus Sitting""  // ""Campus Sitting"" or ""Online Submission"" or ""Unspecified""
    }}
  ]
}}

Rules:
- Use 23:59 as the time for online submissions/assignments.
- If a sitting number is mentioned (e.g. ""Sitting 1""), capture it; otherwise use null.
- Ignore header rows and metadata text.

Timetable text to parse:
---
{text}
---
";

        return await CallGeminiAsync(prompt, apiKey, ParseMode.Exam, cancellationToken);
    }

    private async Task<TimetableParseResult> CallGeminiAsync(string prompt, string apiKey, ParseMode mode, CancellationToken cancellationToken)
    {
        var result = new TimetableParseResult();
        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";
            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                },
                generationConfig = new
                {
                    response_mime_type = "application/json",
                    temperature = 0.1
                }
            };

            var response = await _httpClient.PostAsJsonAsync(url, requestBody, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Gemini API error {StatusCode}: {Error}", response.StatusCode, error);
                result.Warnings.Add($"Gemini API returned {response.StatusCode}. Check the API key and quotas.");
                return result;
            }

            var json = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: cancellationToken);
            var content = json?.Candidates?[0]?.Content?.Parts?[0]?.Text;

            if (string.IsNullOrWhiteSpace(content))
            {
                result.Warnings.Add("Gemini returned an empty response.");
                return result;
            }

            _logger.LogDebug("Gemini raw response: {Content}", content);

            // Strip markdown code fences if model ignores response_mime_type
            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = content.IndexOf('\n');
                var lastFence = content.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline > 0 && lastFence > firstNewline)
                    content = content[(firstNewline + 1)..lastFence].Trim();
            }

            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("events", out var eventsElement))
            {
                result.Warnings.Add("Gemini response did not contain an 'events' array.");
                return result;
            }

            var parsed = 0;
            foreach (var item in eventsElement.EnumerateArray())
            {
                try
                {
                    if (mode == ParseMode.Academic && item.TryGetProperty("day", out _))
                    {
                        var evt = ParseAcademicEvent(item);
                        if (evt is not null) { result.AcademicEvents.Add(evt); parsed++; }
                    }
                    else if (mode == ParseMode.Exam && item.TryGetProperty("date", out _))
                    {
                        var evt = ParseExamEvent(item);
                        if (evt is not null) { result.ExamEvents.Add(evt); parsed++; }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipped one AI event due to parse error");
                    result.Warnings.Add($"Skipped one event: {ex.Message}");
                }
            }

            result.Diagnostics.Add($"ai_parse_success mode={mode} events={parsed}");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parse failure on Gemini response");
            result.Warnings.Add($"AI response JSON was not valid: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AI parsing");
            result.Warnings.Add($"AI parsing error: {ex.Message}");
        }

        return result;
    }

    private static ClassEvent? ParseAcademicEvent(JsonElement item)
    {
        var dayRaw = item.GetProperty("day").GetString() ?? string.Empty;
        if (!DayAliases.TryGetValue(dayRaw.Trim(), out var day)) return null;

        var startRaw = item.GetProperty("startTime").GetString() ?? "08:00";
        var endRaw = item.GetProperty("endTime").GetString() ?? "08:50";
        if (!TimeOnly.TryParseExact(startRaw, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) return null;
        if (!TimeOnly.TryParseExact(endRaw, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) return null;

        var subject = item.GetProperty("subject").GetString() ?? "Unknown";
        var group = item.TryGetProperty("group", out var g) && g.ValueKind != JsonValueKind.Null ? g.GetString() : null;
        if (!string.IsNullOrWhiteSpace(group)) subject = $"{subject} | {group.ToUpperInvariant()}";

        return new ClassEvent
        {
            Day = day,
            StartTime = start,
            EndTime = end,
            Subject = subject,
            Lecturer = (item.TryGetProperty("lecturer", out var l) ? l.GetString() : null) ?? "TBA",
            Venue = (item.TryGetProperty("venue", out var v) ? v.GetString() : null) ?? "TBA"
        };
    }

    private static ExamEvent? ParseExamEvent(JsonElement item)
    {
        var dateRaw = item.GetProperty("date").GetString() ?? string.Empty;
        if (!DateOnly.TryParse(dateRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;

        var timeRaw = item.GetProperty("time").GetString() ?? "09:00";
        if (!TimeOnly.TryParseExact(timeRaw, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;

        var sitting = item.TryGetProperty("sitting", out var s) && s.ValueKind == JsonValueKind.Number
            ? (int?)s.GetInt32() : null;

        return new ExamEvent
        {
            ModuleCode = (item.TryGetProperty("moduleCode", out var mc) ? mc.GetString() : null) ?? "Unknown",
            ModuleName = (item.TryGetProperty("moduleName", out var mn) ? mn.GetString() : null) ?? "Unknown",
            AssessmentType = (item.TryGetProperty("assessmentType", out var at) ? at.GetString() : null) ?? "Assessment",
            Date = date,
            Time = time,
            Sitting = sitting,
            DeliveryMode = (item.TryGetProperty("deliveryMode", out var dm) ? dm.GetString() : null) ?? "Unspecified"
        };
    }

    private static TimetableParseResult Skipped(string reason)
        => new() { Warnings = { reason } };

    // ── Internal Gemini response shape ───────────────────────────────────────
    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public Candidate[]? Candidates { get; set; }

        public sealed class Candidate
        {
            [JsonPropertyName("content")]
            public Content? Content { get; set; }
        }

        public sealed class Content
        {
            [JsonPropertyName("parts")]
            public Part[]? Parts { get; set; }
        }

        public sealed class Part
        {
            [JsonPropertyName("text")]
            public string? Text { get; set; }
        }
    }
}
