using Microsoft.AspNetCore.Mvc;

namespace TimetableSync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class UploadController : ControllerBase
{
    private const string Message = "Deprecated endpoint. Use POST /api/parser/rosebank for parsing and /api/academic/sync or /api/assessment/sync for calendar sync.";

    [HttpPost("preview")]
    public IActionResult Preview() => StatusCode(StatusCodes.Status410Gone, Message);

    [HttpPost("preview-text")]
    public IActionResult PreviewText() => StatusCode(StatusCodes.Status410Gone, Message);

    [HttpPost("build-academic")]
    public IActionResult BuildAcademic() => StatusCode(StatusCodes.Status410Gone, Message);

    [HttpPost("sync")]
    public IActionResult Sync() => StatusCode(StatusCodes.Status410Gone, Message);

    [HttpPost("sync-exam")]
    public IActionResult SyncExam() => StatusCode(StatusCodes.Status410Gone, Message);
}
