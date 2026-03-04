namespace TimetableSync.Api.Services;

public sealed class RosebankReferenceRow
{
    public bool Verified { get; init; }
    public int Year { get; init; }
    public string Group { get; init; } = string.Empty;
    public string ModuleCode { get; init; } = string.Empty;
    public string Lecturer { get; init; } = "TBA";
    public string Venue { get; init; } = "TBA";
}
