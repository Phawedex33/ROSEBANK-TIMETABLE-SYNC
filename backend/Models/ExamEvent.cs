namespace TimetableSync.Api.Models;

public sealed class ExamEvent
{
    public string ModuleCode { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public string AssessmentType { get; init; } = string.Empty;
    public int? Sitting { get; init; }
    public DateOnly Date { get; init; }
    public TimeOnly Time { get; init; }
}
