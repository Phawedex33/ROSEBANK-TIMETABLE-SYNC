using System.Text;
using Microsoft.AspNetCore.Mvc;
using TimetableSync.Api.Models;
using TimetableSync.Api.Services;

namespace TimetableSync.Api.Controllers;

[ApiController]
[Route("api/assessment")]
public sealed class AssessmentController : ControllerBase
{
    private readonly ICalendarExportService _calendarExport;

    public AssessmentController(
        ICalendarExportService calendarExport)
    {
        _calendarExport = calendarExport;
    }

    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    public IActionResult Preview()
    {
        return StatusCode(StatusCodes.Status410Gone, "Deprecated endpoint. Use POST /api/parser/rosebank.");
    }

    [HttpPost("export")]
    public IActionResult Export([FromBody] AssessmentSyncRequest request)
    {
        if (request.Events.Count == 0)
        {
            return BadRequest("At least one assessment event is required.");
        }

        var calendarContent = _calendarExport.BuildAssessmentCalendar(request);
        var fileName = $"rosebank-assessments-{DateOnly.FromDateTime(DateTime.UtcNow):yyyyMMdd}.ics";

        return File(Encoding.UTF8.GetBytes(calendarContent), "text/calendar; charset=utf-8", fileName);
    }
}
