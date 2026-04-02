namespace TimetableSync.Api.Models;

public sealed class AssessmentPreviewResponse
{
    public string ExtractedText { get; init; } = string.Empty;
    public List<AssessmentEvent> Events { get; set; } = new();
    public List<string> Warnings { get; init; } = new();
    public List<string> Diagnostics { get; init; } = new();
}
