using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Options;
using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public sealed class GoogleCalendarService : IGoogleCalendarService
{
    private readonly GoogleCalendarOptions _options;

    public GoogleCalendarService(IOptions<GoogleCalendarOptions> options)
    {
        _options = options.Value;
    }

    public async Task<SyncResponse> CreateWeeklyEventsAsync(SyncRequest request, CancellationToken cancellationToken)
    {
        var service = await CreateCalendarClientAsync(cancellationToken);

        var response = new SyncResponse();
        var nowDate = DateOnly.FromDateTime(DateTime.UtcNow);

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
                    $"RRULE:FREQ=WEEKLY;UNTIL={request.SemesterEndDate.ToDateTime(TimeOnly.MaxValue):yyyyMMdd'T'HHmmss'Z'}"
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
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets
            {
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret
            },
            new[] { CalendarService.Scope.Calendar },
            "user",
            cancellationToken,
            new FileDataStore("timetable-sync-token", true));

        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName
        });
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
