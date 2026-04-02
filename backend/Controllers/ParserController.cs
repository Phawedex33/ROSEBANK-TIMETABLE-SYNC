using Microsoft.AspNetCore.Mvc;
using TimetableSync.Api.Models;
using TimetableSync.Api.Services;

namespace TimetableSync.Api.Controllers;

[ApiController]
[Route("api/parser")]
public sealed class ParserController : ControllerBase
{
    private readonly IRosebankParserService _rosebankParser;

    public ParserController(IRosebankParserService rosebankParser)
    {
        _rosebankParser = rosebankParser;
    }

    [HttpPost("rosebank")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ParseRosebank([FromForm] RosebankParseRequest request, CancellationToken cancellationToken)
    {
        var result = await _rosebankParser.ParseAsync(request, cancellationToken);
        if (result is RosebankMissingInputsError)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }
}
