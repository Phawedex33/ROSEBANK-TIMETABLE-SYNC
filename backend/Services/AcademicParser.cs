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
        if (parsed.AcademicEvents.Count == 0 &&
            input.Contains("Rosebank College", StringComparison.OrdinalIgnoreCase) &&
            input.Contains("3rd Year: GR", StringComparison.OrdinalIgnoreCase))
        {
            parsed.Warnings.Add("Rosebank class timetable PDF text is layout-compressed. Upload as image (PNG/JPG) for OCR-based extraction, then confirm rows in the editable preview.");
        }

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
