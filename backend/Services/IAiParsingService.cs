using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public interface IAiParsingService
{
    Task<TimetableParseResult> ParseAcademicAsync(string text, CancellationToken cancellationToken = default);
    Task<TimetableParseResult> ParseExamAsync(string text, CancellationToken cancellationToken = default);
}
