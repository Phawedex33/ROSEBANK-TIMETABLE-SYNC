using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public sealed class AcademicParser : IAcademicParser
{
    private readonly ITimetableParser _parser;

    public AcademicParser(ITimetableParser parser)
    {
        _parser = parser;
    }

    public AcademicPreviewResponse Parse(string input, int year, string group)
    {
        var parsed = _parser.Parse(input, ParseMode.Academic);
        return new AcademicPreviewResponse
        {
            Year = year,
            Group = group,
            ExtractedText = input,
            Events = parsed.AcademicEvents,
            Warnings = parsed.Warnings,
            Diagnostics = parsed.Diagnostics
        };
    }
}
