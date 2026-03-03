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

    public AcademicController(
        IPdfTextExtractor extractor,
        IAcademicParser parser,
        IGoogleCalendarService calendar)
    {
        _extractor = extractor;
        _parser = parser;
        _calendar = calendar;
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview(
        [FromForm] IFormFile file,
        [FromForm] int year,
        [FromForm] string group,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return BadRequest("File is required.");
        }

        var text = await _extractor.ExtractAsync(file, cancellationToken);
        var parsed = _parser.Parse(text, year, group ?? string.Empty);
        return Ok(parsed);
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromBody] AcademicSyncRequest request, CancellationToken cancellationToken)
    {
        if (request.Events.Count == 0)
        {
            return BadRequest("At least one class event is required.");
        }

        var response = await _calendar.CreateWeeklyEventsAsync(new SyncRequest
        {
            Events = request.Events,
            SemesterEndDate = request.SemesterEndDate,
            TimeZone = request.TimeZone
        }, cancellationToken);

        return Ok(response);
    }
}
