namespace TimetableSync.Api.Models;

public sealed class AssessmentSyncRequest
{
    public List<AssessmentEvent> Events { get; init; } = new();
    public string TimeZone { get; init; } = "Africa/Johannesburg";
    public int DurationMinutes { get; init; } = 60;
}
