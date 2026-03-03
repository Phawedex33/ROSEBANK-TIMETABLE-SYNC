namespace TimetableSync.Api.Models;

public sealed class ExamSyncRequest
{
    public List<ExamEvent> Events { get; init; } = new();
    public string TimeZone { get; init; } = "Africa/Johannesburg";
    public int DurationMinutes { get; init; } = 60;
}
