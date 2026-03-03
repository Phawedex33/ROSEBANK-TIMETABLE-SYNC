namespace TimetableSync.Api.Models;

public sealed class SyncRequest
{
    public List<ClassEvent> Events { get; init; } = new();
    public DateOnly SemesterEndDate { get; init; }
    public string TimeZone { get; init; } = "Africa/Johannesburg";
}
