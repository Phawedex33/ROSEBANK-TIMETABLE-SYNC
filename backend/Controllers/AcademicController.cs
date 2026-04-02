using Microsoft.AspNetCore.Mvc;
using TimetableSync.Api.Models;
using TimetableSync.Api.Services;

namespace TimetableSync.Api.Controllers;

[ApiController]
[Route("api/academic")]
public sealed class AcademicController : ControllerBase
{
    private readonly IPdfTextExtractor _extractor;
    private readonly IAcademicParser _parser;
    private readonly IGoogleCalendarService _calendar;
    private readonly IAiParsingService _aiParser;

    public AcademicController(
        IPdfTextExtractor extractor,
        IAcademicParser parser,
        IGoogleCalendarService calendar,
        IAiParsingService aiParser)
    {
        _extractor = extractor;
        _parser = parser;
        _calendar = calendar;
        _aiParser = aiParser;
    }

    /// <summary>
    /// Parse a timetable PDF and get a preview of events.
    /// Optionally use AI parsing for better accuracy with complex formats.
    /// </summary>
    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    public IActionResult Preview()
    {
        return StatusCode(StatusCodes.Status410Gone, "Deprecated endpoint. Use POST /api/parser/rosebank.");
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromBody] AcademicSyncRequest request, CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue("sync_user_id", out var userId) || string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized("Google account not connected. Open /oauth/google/start first.");
        }

        if (request.Events.Count == 0)
            return BadRequest("At least one class event is required.");

        try
        {
            var response = await _calendar.CreateWeeklyEventsAsync(userId, new SyncRequest
            {
                Events = request.Events,
                SemesterEndDate = request.SemesterEndDate,
                WeeksDuration = request.WeeksDuration,
                TimeZone = request.TimeZone
            }, cancellationToken);

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
