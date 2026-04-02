using Microsoft.AspNetCore.Http;

namespace TimetableSync.Api.Models;

public sealed class AcademicPreviewRequest
{
    public IFormFile? File { get; set; }
    public int Year { get; set; }
    public string? Group { get; set; }
    public bool UseAi { get; set; } = false;
}


public sealed class AssessmentPreviewRequest
{
    public IFormFile? File { get; set; }
    public string? Text { get; set; }
}

public sealed class LegacyPreviewRequest
{
    public IFormFile? File { get; set; }
    public ParseMode Mode { get; set; } = ParseMode.Academic;
}
