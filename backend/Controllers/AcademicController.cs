using System.Text;
using Microsoft.AspNetCore.Mvc;
using TimetableSync.Api.Models;
using TimetableSync.Api.Services;

namespace TimetableSync.Api.Controllers;

[ApiController]
[Route("api/academic")]
public sealed class AcademicController : ControllerBase
{
    private readonly ICalendarExportService _calendarExport;

    public AcademicController(
        ICalendarExportService calendarExport)
    {
        _calendarExport = calendarExport;
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

    [HttpPost("export")]
    public IActionResult Export([FromBody] AcademicSyncRequest request)
    {
        if (request.Events.Count == 0)
        {
            return BadRequest("At least one class event is required.");
        }

        var calendarContent = _calendarExport.BuildAcademicCalendar(request);
        var fileName = $"rosebank-academic-{DateOnly.FromDateTime(DateTime.UtcNow):yyyyMMdd}.ics";

        return File(Encoding.UTF8.GetBytes(calendarContent), "text/calendar; charset=utf-8", fileName);
    }
}
