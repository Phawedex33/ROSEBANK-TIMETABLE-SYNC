using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Options;
using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public sealed class GoogleCalendarService : IGoogleCalendarService
{

    private readonly GoogleCalendarOptions _options;
    private readonly ITokenStore _tokenStore;

    public GoogleCalendarService(IOptions<GoogleCalendarOptions> options, ITokenStore tokenStore)
    {
        _options = options.Value;
        _tokenStore = tokenStore;
    }

    public async Task<SyncResponse> CreateWeeklyEventsAsync(string userId, SyncRequest request, CancellationToken cancellationToken)
    {
        var service = await CreateCalendarClientAsync(userId, cancellationToken);

        var response = new SyncResponse();
        var nowDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var weeks = request.WeeksDuration <= 0 ? 16 : request.WeeksDuration;
        var recurrenceEndDate = request.SemesterEndDate ?? nowDate.AddDays(weeks * 7);

        foreach (var item in request.Events)
        {
            var nextOccurrence = GetNextDateForDay(item.Day, nowDate);

            var start = new DateTimeOffset(
                nextOccurrence.Year,
                nextOccurrence.Month,
                nextOccurrence.Day,
                item.StartTime.Hour,
                item.StartTime.Minute,
                0,
                TimeSpan.Zero);

            var end = new DateTimeOffset(
                nextOccurrence.Year,
                nextOccurrence.Month,
                nextOccurrence.Day,
                item.EndTime.Hour,
                item.EndTime.Minute,
                0,
                TimeSpan.Zero);

            var recurringEvent = new Event
            {
                Summary = item.Subject.StartsWith("[CLASS] ", StringComparison.OrdinalIgnoreCase)
                    ? item.Subject
                    : $"[CLASS] {item.Subject}",
                Description = BuildClassDescription(item),
                Start = new EventDateTime
                {
                    DateTimeDateTimeOffset = start,
                    TimeZone = request.TimeZone
                },
                End = new EventDateTime
                {
                    DateTimeDateTimeOffset = end,
                    TimeZone = request.TimeZone
                },
                Location = string.IsNullOrWhiteSpace(item.Venue) ? null : item.Venue,
                Recurrence = new List<string>
                {
                    $"RRULE:FREQ=WEEKLY;UNTIL={recurrenceEndDate.ToDateTime(TimeOnly.MaxValue):yyyyMMdd'T'HHmmss'Z'}"
                },
                ColorId = "9",
                Reminders = new Event.RemindersData
                {
                    UseDefault = false,
                    Overrides = new List<EventReminder>
                    {
                        new() { Method = "popup", Minutes = 1440 },  // 1 day before
                        new() { Method = "popup", Minutes = 120 }    // 2 hours before
                    }
                }
            };


            var created = await service.Events.Insert(recurringEvent, _options.CalendarId).ExecuteAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(created.Id))
            {
                response.EventIds.Add(created.Id);
            }
        }

        response.Created = response.EventIds.Count;
        return response;
    }

    public async Task<SyncResponse> CreateAssessmentEventsAsync(string userId, AssessmentSyncRequest request, CancellationToken cancellationToken)
    {
        var mapped = request.Events.Select(item => new ExamEvent
        {
            ModuleCode = item.ModuleCode,
            ModuleName = item.ModuleName,
            AssessmentType = item.AssessmentType,
            Sitting = item.Sitting,
            Date = item.Date,
            Time = item.Time,
            DeliveryMode = item.DeliveryMode
        }).ToList();

        return await CreateExamEventsInternalAsync(
            userId,
            mapped,
            request.TimeZone,
            request.DurationMinutes,
            "[ASSESSMENT]",
            "11",
            cancellationToken);
    }

    public async Task<SyncResponse> CreateExamEventsAsync(string userId, ExamSyncRequest request, CancellationToken cancellationToken)
    {
        return await CreateExamEventsInternalAsync(
            userId,
            request.Events,
            request.TimeZone,
            request.DurationMinutes,
            "[EXAM]",
            "11",
            cancellationToken);
    }

    public async Task<CalendarDeleteResponse> DeleteManagedEventsAsync(string userId, CalendarDeleteRequest request, CancellationToken cancellationToken)
    {
        var service = await CreateCalendarClientAsync(userId, cancellationToken);
        var response = new CalendarDeleteResponse();
        var (fromUtc, toUtc) = ResolveDeleteWindow(request.FromDate, request.ToDate);
        var prefixes = ResolveManagedPrefixes(request.Mode);

        var listRequest = service.Events.List(_options.CalendarId);
        listRequest.ShowDeleted = false;
        listRequest.SingleEvents = false;
        listRequest.TimeMinDateTimeOffset = fromUtc;
        listRequest.TimeMaxDateTimeOffset = toUtc;

        string? pageToken = null;
        do
        {
            listRequest.PageToken = pageToken;
            var events = await listRequest.ExecuteAsync(cancellationToken);
            foreach (var ev in events.Items ?? Enumerable.Empty<Event>())
            {
                if (string.IsNullOrWhiteSpace(ev.Id)) continue;

                var summary = ev.Summary ?? string.Empty;
                if (!prefixes.Any(prefix => summary.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) continue;

                await service.Events.Delete(_options.CalendarId, ev.Id).ExecuteAsync(cancellationToken);
                response.EventIds.Add(ev.Id);
            }

            pageToken = events.NextPageToken;
        } while (!string.IsNullOrWhiteSpace(pageToken));

        response.Deleted = response.EventIds.Count;
        return response;
    }

    private async Task<SyncResponse> CreateExamEventsInternalAsync(
        string userId,
        IReadOnlyCollection<ExamEvent> events,
        string timeZone,
        int durationMinutes,
        string tag,
        string colorId,
        CancellationToken cancellationToken)
    {
        var service = await CreateCalendarClientAsync(userId, cancellationToken);
        var response = new SyncResponse();
        var duration = durationMinutes <= 0 ? 60 : durationMinutes;

        foreach (var item in events)
        {
            var start = item.Date.ToDateTime(item.Time, DateTimeKind.Unspecified);
            var end = start.AddMinutes(duration);
            var summary = $"{tag} {item.ModuleCode} - {item.AssessmentType}";
            if (!string.IsNullOrWhiteSpace(item.ModuleName))
            {
                summary = $"{tag} {item.ModuleCode} - {item.ModuleName} ({item.AssessmentType})";
            }

            var examEvent = new Event
            {
                Summary = summary,
                Description = BuildExamDescription(item),
                Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(start, TimeSpan.Zero), TimeZone = timeZone },
                End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(end, TimeSpan.Zero), TimeZone = timeZone },
                ColorId = colorId,
                Reminders = new Event.RemindersData
                {
                    UseDefault = false,
                    Overrides = new List<EventReminder>
                    {
                        new() { Method = "popup", Minutes = 1440 },
                        new() { Method = "popup", Minutes = 120 }
                    }
                }
            };

            var created = await service.Events.Insert(examEvent, _options.CalendarId).ExecuteAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(created.Id))
            {
                response.EventIds.Add(created.Id);
            }
        }

        response.Created = response.EventIds.Count;
        return response;
    }

    private async Task<CalendarService> CreateCalendarClientAsync(string userId, CancellationToken cancellationToken)
    {
        var stored = await _tokenStore.LoadAsync(userId, cancellationToken);
        if (stored is null || (string.IsNullOrWhiteSpace(stored.AccessToken) && string.IsNullOrWhiteSpace(stored.RefreshToken)))
        {
            throw new InvalidOperationException("Google account not connected. Open /oauth/google/start first.");
        }

        var expiresIn = Math.Max(1, (long)Math.Ceiling((stored.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds));
        var tokenResponse = new TokenResponse
        {
            AccessToken = stored.AccessToken,
            RefreshToken = stored.RefreshToken,
            ExpiresInSeconds = expiresIn
        };

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = _options.ClientId, ClientSecret = _options.ClientSecret },
            Scopes = new[] { CalendarService.Scope.Calendar }
        });

        var credential = new UserCredential(flow, "file-store-user", tokenResponse);
        if (credential.Token.IsStale)
        {
            var refreshed = await credential.RefreshTokenAsync(cancellationToken);
            if (!refreshed)
            {
                throw new InvalidOperationException("Google token refresh failed. Reconnect using /oauth/google/start.");
            }

            var newExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, credential.Token.ExpiresInSeconds ?? 3600));
            var refreshToken = !string.IsNullOrWhiteSpace(credential.Token.RefreshToken) ? credential.Token.RefreshToken : stored.RefreshToken;
            await _tokenStore.SaveAsync(userId, new StoredToken(credential.Token.AccessToken, refreshToken, newExpiry), cancellationToken);
        }

        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName
        });
    }

    private static string BuildExamDescription(ExamEvent item)
    {
        var details = new List<string> { $"Module: {item.ModuleCode}", $"Assessment: {item.AssessmentType}" };
        if (!string.IsNullOrWhiteSpace(item.ModuleName)) details.Add($"Name: {item.ModuleName}");
        if (item.Sitting.HasValue) details.Add($"Sitting: {item.Sitting.Value}");
        if (!string.IsNullOrWhiteSpace(item.DeliveryMode)) details.Add($"Mode: {item.DeliveryMode}");
        return string.Join(Environment.NewLine, details);
    }

    private static string BuildClassDescription(ClassEvent item)
    {
        var lines = new List<string> { $"Subject: {item.Subject}" };
        if (!string.IsNullOrWhiteSpace(item.Lecturer)) lines.Add($"Lecturer: {item.Lecturer}");
        if (!string.IsNullOrWhiteSpace(item.Venue)) lines.Add($"Venue: {item.Venue}");
        return string.Join(Environment.NewLine, lines);
    }

    private static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) ResolveDeleteWindow(DateOnly? fromDate, DateOnly? toDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = fromDate ?? today.AddDays(-180);
        var to = toDate ?? today.AddDays(365);
        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        return (fromUtc, toUtc);
    }

    private static IReadOnlyList<string> ResolveManagedPrefixes(string mode)
    {
        var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "academic" or "class") return new[] { "[CLASS]" };
        if (normalized is "assessment" or "exam") return new[] { "[ASSESSMENT]", "[EXAM]" };
        return new[] { "[CLASS]", "[ASSESSMENT]", "[EXAM]" };
    }

    private static DateOnly GetNextDateForDay(DayOfWeek target, DateOnly from)
    {
        var daysToAdd = ((int)target - (int)from.DayOfWeek + 7) % 7;
        if (daysToAdd == 0) daysToAdd = 7;
        return from.AddDays(daysToAdd);
    }
}
