namespace TimetableSync.Api.Models;

public sealed class CalendarDeleteRequest
{
    public string Mode { get; init; } = "Current";
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public string TimeZone { get; init; } = "Africa/Johannesburg";
}
