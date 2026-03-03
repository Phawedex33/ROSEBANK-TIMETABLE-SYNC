using Microsoft.AspNetCore.Mvc;
using TimetableSync.Api.Models;
using TimetableSync.Api.Services;

namespace TimetableSync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class UploadController : ControllerBase
{
    private readonly ITextExtractionService _extractor;
    private readonly ITimetableParser _parser;
    private readonly IGoogleCalendarService _calendar;

    public UploadController(
        ITextExtractionService extractor,
        ITimetableParser parser,
        IGoogleCalendarService calendar)
    {
        _extractor = extractor;
        _parser = parser;
        _calendar = calendar;
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview(
        [FromForm] IFormFile file,
        [FromForm] ParseMode mode = ParseMode.Academic,
        CancellationToken cancellationToken = default)
    {
        if (file is null)
        {
            return BadRequest("File is required.");
        }

        var extracted = await _extractor.ExtractAsync(file, cancellationToken);
        var parsed = _parser.Parse(extracted, mode);

        return Ok(new
        {
            mode,
            extractedText = extracted,
            parsed.AcademicEvents,
            parsed.ExamEvents,
            parsed.Warnings
        });
    }

    [HttpPost("preview-text")]
    public IActionResult PreviewText([FromBody] PreviewTextRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest("Text is required.");
        }

        var parsed = _parser.Parse(request.Text, request.Mode);

        return Ok(new
        {
            mode = request.Mode,
            extractedText = request.Text,
            parsed.AcademicEvents,
            parsed.ExamEvents,
            parsed.Warnings
        });
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromBody] SyncRequest request, CancellationToken cancellationToken)
    {
        if (request.Events.Count == 0)
        {
            return BadRequest("At least one event is required.");
        }

        var response = await _calendar.CreateWeeklyEventsAsync(request, cancellationToken);
        return Ok(response);
    }
}
