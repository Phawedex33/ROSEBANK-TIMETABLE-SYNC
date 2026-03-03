namespace TimetableSync.Api.Services;

public sealed class GoogleCalendarOptions
{
    public string ApplicationName { get; init; } = "Timetable Sync";
    public string CalendarId { get; init; } = "primary";
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
}
