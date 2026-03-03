using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public sealed class GoogleCalendarService : IGoogleCalendarService
{
    private readonly GoogleCalendarOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GoogleCalendarService(IOptions<GoogleCalendarOptions> options, IHttpContextAccessor httpContextAccessor)
    {
        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<SyncResponse> CreateWeeklyEventsAsync(SyncRequest request, CancellationToken cancellationToken)
    {
        var service = await CreateCalendarClientAsync(cancellationToken);

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
                Recurrence = new List<string>
                {
                    $"RRULE:FREQ=WEEKLY;UNTIL={recurrenceEndDate.ToDateTime(TimeOnly.MaxValue):yyyyMMdd'T'HHmmss'Z'}"
                },
                ColorId = "9"
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

    public async Task<SyncResponse> CreateAssessmentEventsAsync(AssessmentSyncRequest request, CancellationToken cancellationToken)
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
            mapped,
            request.TimeZone,
            request.DurationMinutes,
            "[ASSESSMENT]",
            "11",
            cancellationToken);
    }

    public async Task<SyncResponse> CreateExamEventsAsync(ExamSyncRequest request, CancellationToken cancellationToken)
    {
        return await CreateExamEventsInternalAsync(
            request.Events,
            request.TimeZone,
            request.DurationMinutes,
            "[EXAM]",
            "11",
            cancellationToken);
    }

    private async Task<SyncResponse> CreateExamEventsInternalAsync(
        IReadOnlyCollection<ExamEvent> events,
        string timeZone,
        int durationMinutes,
        string tag,
        string colorId,
        CancellationToken cancellationToken)
    {
        var service = await CreateCalendarClientAsync(cancellationToken);
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
                Start = new EventDateTime
                {
                    DateTime = start,
                    TimeZone = timeZone
                },
                End = new EventDateTime
                {
                    DateTime = end,
                    TimeZone = timeZone
                },
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

    private async Task<CalendarService> CreateCalendarClientAsync(CancellationToken cancellationToken)
    {
        var session = _httpContextAccessor.HttpContext?.Session
            ?? throw new InvalidOperationException("Session is not available.");

        var accessToken = session.GetString("google_access_token");
        var refreshToken = session.GetString("google_refresh_token");
        if (string.IsNullOrWhiteSpace(accessToken) && string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException("Google account not connected. Open /oauth/google/start first.");
        }

        var expiresAtRaw = session.GetString("google_token_expiry_utc");
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        if (!string.IsNullOrWhiteSpace(expiresAtRaw) &&
            DateTimeOffset.TryParse(expiresAtRaw, out var parsedExpiry))
        {
            expiresAt = parsedExpiry;
        }

        var expiresIn = Math.Max(1, (long)Math.Ceiling((expiresAt - DateTimeOffset.UtcNow).TotalSeconds));
        var token = new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresInSeconds = expiresIn
        };

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret
            },
            Scopes = new[] { CalendarService.Scope.Calendar }
        });

        var credential = new UserCredential(flow, "web-user", token);
        if (credential.Token.IsStale)
        {
            var refreshed = await credential.RefreshTokenAsync(cancellationToken);
            if (!refreshed)
            {
                throw new InvalidOperationException("Google token refresh failed. Reconnect using /oauth/google/start.");
            }

            PersistTokenToSession(session, credential.Token, refreshToken);
        }

        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName
        });
    }

    private static void PersistTokenToSession(ISession session, TokenResponse token, string? existingRefreshToken)
    {
        if (!string.IsNullOrWhiteSpace(token.AccessToken))
        {
            session.SetString("google_access_token", token.AccessToken);
        }

        var refreshToken = !string.IsNullOrWhiteSpace(token.RefreshToken)
            ? token.RefreshToken
            : existingRefreshToken;
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            session.SetString("google_refresh_token", refreshToken);
        }

        var expiresIn = token.ExpiresInSeconds ?? 3600;
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, expiresIn));
        session.SetString("google_token_expiry_utc", expiresAt.ToString("O"));
    }

    private static string BuildExamDescription(ExamEvent item)
    {
        var details = new List<string>
        {
            $"Module: {item.ModuleCode}",
            $"Assessment: {item.AssessmentType}"
        };

        if (!string.IsNullOrWhiteSpace(item.ModuleName))
        {
            details.Add($"Name: {item.ModuleName}");
        }

        if (item.Sitting.HasValue)
        {
            details.Add($"Sitting: {item.Sitting.Value}");
        }

        if (!string.IsNullOrWhiteSpace(item.DeliveryMode))
        {
            details.Add($"Mode: {item.DeliveryMode}");
        }

        return string.Join(Environment.NewLine, details);
    }

    private static DateOnly GetNextDateForDay(DayOfWeek target, DateOnly from)
    {
        var daysToAdd = ((int)target - (int)from.DayOfWeek + 7) % 7;
        if (daysToAdd == 0)
        {
            daysToAdd = 7;
        }

        return from.AddDays(daysToAdd);
    }
}
