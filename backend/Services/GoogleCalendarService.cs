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

        var service = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName
        });

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
                Summary = item.Subject,
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
