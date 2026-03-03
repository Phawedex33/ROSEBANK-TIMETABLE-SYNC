namespace TimetableSync.Api.Models;

public sealed class AcademicPreviewResponse
{
    public int Year { get; init; }
    public string Group { get; init; } = string.Empty;
    public string ExtractedText { get; init; } = string.Empty;
    public List<ClassEvent> Events { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public List<string> Diagnostics { get; init; } = new();
}
