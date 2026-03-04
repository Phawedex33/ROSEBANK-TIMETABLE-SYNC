namespace TimetableSync.Api.Models;

public sealed class ClassEvent
{
    public DayOfWeek Day { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string Lecturer { get; init; } = string.Empty;
    public string Venue { get; init; } = string.Empty;
}
