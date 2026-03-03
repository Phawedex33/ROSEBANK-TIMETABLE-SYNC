namespace TimetableSync.Api.Models;

public sealed class AssessmentPreviewResponse
{
    public string ExtractedText { get; init; } = string.Empty;
    public List<AssessmentEvent> Events { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}
