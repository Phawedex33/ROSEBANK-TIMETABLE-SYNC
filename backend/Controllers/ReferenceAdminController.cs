using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TimetableSync.Api.Services;

namespace TimetableSync.Api.Controllers;

[ApiController]
[Route("api/admin/reference")]
public sealed class ReferenceAdminController : ControllerBase
{
    private const string AdminKeyHeader = "X-Admin-Key";
    private readonly IRosebankReferenceService _referenceService;
    private readonly ReferenceAdminOptions _options;

    public ReferenceAdminController(
        IRosebankReferenceService referenceService,
        IOptions<ReferenceAdminOptions> options)
    {
        _referenceService = referenceService;
        _options = options.Value;
    }

    [HttpGet("config")]
    public IActionResult Config()
    {
        return Ok(new
        {
            enabled = _options.Enabled,
            localhostOnly = _options.LocalhostOnly
        });
    }

    [HttpGet]
    public IActionResult GetRows()
    {
        var authError = ValidateAdminAccess();
        if (authError is not null)
        {
            return authError;
        }

        return Ok(new
        {
            rows = _referenceService.GetAllRows()
        });
    }

    [HttpPut]
    public async Task<IActionResult> SaveRows([FromBody] SaveReferenceRowsRequest request, CancellationToken cancellationToken)
    {
        var authError = ValidateAdminAccess();
        if (authError is not null)
        {
            return authError;
        }

        await _referenceService.SaveRowsAsync(request.Rows, cancellationToken);
        return Ok(new { saved = request.Rows.Count });
    }

    private IActionResult? ValidateAdminAccess()
    {
        if (!_options.Enabled)
        {
            return NotFound();
        }

        if (_options.LocalhostOnly)
        {
            var remoteIp = HttpContext.Connection.RemoteIpAddress;
            if (remoteIp is null || !(IPAddress.IsLoopback(remoteIp) || remoteIp.Equals(HttpContext.Connection.LocalIpAddress)))
            {
                return StatusCode(StatusCodes.Status403Forbidden, "Reference admin is localhost-only.");
            }
        }

        if (string.IsNullOrWhiteSpace(_options.AdminKey))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Reference admin key is not configured.");
        }

        if (!Request.Headers.TryGetValue(AdminKeyHeader, out var providedHeader))
        {
            return Unauthorized("Missing admin key header.");
        }

        if (!FixedEquals(_options.AdminKey, providedHeader.ToString()))
        {
            return Unauthorized("Invalid admin key.");
        }

        return null;
    }

    private static bool FixedEquals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a ?? string.Empty);
        var bb = Encoding.UTF8.GetBytes(b ?? string.Empty);
        if (ab.Length != bb.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }

    public sealed class SaveReferenceRowsRequest
    {
        public List<RosebankReferenceRow> Rows { get; init; } = new();
    }
}
