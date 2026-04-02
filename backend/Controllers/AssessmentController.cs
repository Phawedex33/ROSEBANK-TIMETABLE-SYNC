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
    private readonly IAiParsingService _aiParser;

    public AssessmentController(
        IPdfTextExtractor extractor,
        IAssessmentParser parser,
        IGoogleCalendarService calendar,
        IAiParsingService aiParser)
    {
        _extractor = extractor;
        _parser = parser;
        _calendar = calendar;
        _aiParser = aiParser;
    }

    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    public IActionResult Preview()
    {
        return StatusCode(StatusCodes.Status410Gone, "Deprecated endpoint. Use POST /api/parser/rosebank.");
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromBody] AssessmentSyncRequest request, CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue("sync_user_id", out var userId) || string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized("Google account not connected. Open /oauth/google/start first.");
        }

        if (request.Events.Count == 0)
        {
            return BadRequest("At least one assessment event is required.");
        }

        try
        {
            var response = await _calendar.CreateAssessmentEventsAsync(userId, request, cancellationToken);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
