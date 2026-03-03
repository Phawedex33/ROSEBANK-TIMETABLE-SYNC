namespace TimetableSync.Api.Models;

public sealed class TimetableParseResult
{
    public List<ClassEvent> AcademicEvents { get; init; } = new();
    public List<ExamEvent> ExamEvents { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}
