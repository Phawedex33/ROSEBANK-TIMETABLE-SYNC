namespace TimetableSync.Api.Models;

public sealed class AcademicSyncRequest
{
    public int Year { get; init; }
    public string Group { get; init; } = string.Empty;
    public List<ClassEvent> Events { get; init; } = new();
    public DateOnly? SemesterEndDate { get; init; }
    public int WeeksDuration { get; init; } = 16;
    public string TimeZone { get; init; } = "Africa/Johannesburg";
}
