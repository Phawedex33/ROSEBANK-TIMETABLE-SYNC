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
    private readonly IAcademicScheduleBuilder _scheduleBuilder;
    private readonly IGoogleCalendarService _calendar;

    public UploadController(
        ITextExtractionService extractor,
        ITimetableParser parser,
        IAcademicScheduleBuilder scheduleBuilder,
        IGoogleCalendarService calendar)
    {
        _extractor = extractor;
        _parser = parser;
        _scheduleBuilder = scheduleBuilder;
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

    [HttpPost("build-academic")]
    public IActionResult BuildAcademic([FromBody] AcademicBuildRequest request)
    {
        if (request.Rows.Count == 0)
        {
            return BadRequest("At least one academic row is required.");
        }

        var events = new List<ClassEvent>();
        var warnings = new List<string>();

        foreach (var row in request.Rows)
        {
            var slot = _scheduleBuilder.ResolvePeriod(row.Period);
            if (slot is null)
            {
                warnings.Add($"Skipped period {row.Period} for {row.Subject}: invalid period.");
                continue;
            }

            var details = row.Subject.Trim();
            if (!string.IsNullOrWhiteSpace(row.Lecturer))
            {
                details += $" | {row.Lecturer.Trim()}";
            }
            if (!string.IsNullOrWhiteSpace(row.Venue))
            {
                details += $" | {row.Venue.Trim()}";
            }
            if (!string.IsNullOrWhiteSpace(request.Group))
            {
                details += $" | {request.Group.Trim()}";
            }

            events.Add(new ClassEvent
            {
                Day = row.Day,
                StartTime = slot.Value.Start,
                EndTime = slot.Value.End,
                Subject = details
            });
        }

        return Ok(new
        {
            events,
            warnings
        });
    }

    [HttpPost("sync-exam")]
    public async Task<IActionResult> SyncExam([FromBody] ExamSyncRequest request, CancellationToken cancellationToken)
    {
        if (request.Events.Count == 0)
        {
            return BadRequest("At least one exam event is required.");
        }

        var response = await _calendar.CreateExamEventsAsync(request, cancellationToken);
        return Ok(response);
    }
}
