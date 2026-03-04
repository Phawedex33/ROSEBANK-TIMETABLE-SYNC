using Microsoft.AspNetCore.Mvc;
using TimetableSync.Api.Models;
using TimetableSync.Api.Services;

namespace TimetableSync.Api.Controllers;

[ApiController]
[Route("api/calendar")]
public sealed class CalendarController : ControllerBase
{
    private readonly IGoogleCalendarService _calendar;

    public CalendarController(IGoogleCalendarService calendar)
    {
        _calendar = calendar;
    }

    [HttpPost("delete-synced")]
    public async Task<IActionResult> DeleteSynced([FromBody] CalendarDeleteRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _calendar.DeleteManagedEventsAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
