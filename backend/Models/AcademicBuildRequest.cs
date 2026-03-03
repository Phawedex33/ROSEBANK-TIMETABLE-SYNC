namespace TimetableSync.Api.Models;

public sealed class AcademicBuildRequest
{
    public string Group { get; init; } = string.Empty;
    public List<AcademicDraftRow> Rows { get; init; } = new();
}
