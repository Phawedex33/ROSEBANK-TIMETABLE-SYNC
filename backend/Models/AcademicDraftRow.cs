namespace TimetableSync.Api.Models;

public sealed class AcademicDraftRow
{
    public DayOfWeek Day { get; init; }
    public int Period { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string Lecturer { get; init; } = string.Empty;
    public string Venue { get; init; } = string.Empty;
}
