using Microsoft.AspNetCore.Mvc;
using TimetableSync.Api.Models;
using TimetableSync.Api.Services;

namespace TimetableSync.Api.Controllers;

[ApiController]
[Route("api/assessment")]
public sealed class AssessmentController : ControllerBase
{
    private readonly IPdfTextExtractor _extractor;
    private readonly IAssessmentParser _parser;
    private readonly IGoogleCalendarService _calendar;

    public AssessmentController(
        IPdfTextExtractor extractor,
        IAssessmentParser parser,
        IGoogleCalendarService calendar)
    {
        _extractor = extractor;
        _parser = parser;
        _calendar = calendar;
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview(
        [FromForm] IFormFile? file,
        [FromForm] string? text,
        CancellationToken cancellationToken)
    {
        if (file is null && string.IsNullOrWhiteSpace(text))
        {
            return BadRequest("Provide either a file or text.");
        }

        var extractedText = string.IsNullOrWhiteSpace(text)
            ? await _extractor.ExtractAsync(file!, cancellationToken)
            : text!;

        var parsed = _parser.Parse(extractedText);
        return Ok(parsed);
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromBody] AssessmentSyncRequest request, CancellationToken cancellationToken)
    {
        if (request.Events.Count == 0)
        {
            return BadRequest("At least one assessment event is required.");
        }

        try
        {
            var response = await _calendar.CreateAssessmentEventsAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
