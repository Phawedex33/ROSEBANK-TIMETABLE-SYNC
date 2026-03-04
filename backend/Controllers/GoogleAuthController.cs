using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TimetableSync.Api.Services;

namespace TimetableSync.Api.Controllers;

[ApiController]
[Route("oauth/google")]
public sealed class GoogleAuthController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GoogleCalendarOptions _options;

    public GoogleAuthController(IHttpClientFactory httpClientFactory, IOptions<GoogleCalendarOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    [HttpGet("start")]
    public IActionResult Start()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            return BadRequest("Google OAuth client configuration is missing.");
        }

        var state = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString("google_oauth_state", state);

        var scope = Uri.EscapeDataString("https://www.googleapis.com/auth/calendar");
        var redirectUri = Uri.EscapeDataString(_options.RedirectUri);
        var clientId = Uri.EscapeDataString(_options.ClientId);
        var authUrl =
            "https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={clientId}" +
            $"&redirect_uri={redirectUri}" +
            "&response_type=code" +
            $"&scope={scope}" +
            "&access_type=offline" +
            "&prompt=consent" +
            $"&state={Uri.EscapeDataString(state)}";

        return Redirect(authUrl);
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        var accessToken = HttpContext.Session.GetString("google_access_token");
        var refreshToken = HttpContext.Session.GetString("google_refresh_token");
        var expiryUtc = HttpContext.Session.GetString("google_token_expiry_utc");
        var connected = !string.IsNullOrWhiteSpace(accessToken) || !string.IsNullOrWhiteSpace(refreshToken);

        return Ok(new
        {
            connected,
            expiresAtUtc = expiryUtc
        });
    }

    [HttpPost("disconnect")]
    public IActionResult Disconnect()
    {
        HttpContext.Session.Remove("google_access_token");
        HttpContext.Session.Remove("google_refresh_token");
        HttpContext.Session.Remove("google_token_expiry_utc");
        HttpContext.Session.Remove("google_oauth_state");

        return Ok(new { disconnected = true });
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return BadRequest($"Google OAuth error: {error}");
        }

        var expectedState = HttpContext.Session.GetString("google_oauth_state");
        if (string.IsNullOrWhiteSpace(expectedState) || !string.Equals(expectedState, state, StringComparison.Ordinal))
        {
            return BadRequest("Invalid OAuth state.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest("Missing authorization code.");
        }

        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["redirect_uri"] = _options.RedirectUri,
                ["grant_type"] = "authorization_code"
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return BadRequest($"Token exchange failed: {payload}");
        }

        var token = JsonSerializer.Deserialize<GoogleTokenResponse>(payload);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            return BadRequest("Token response was invalid.");
        }

        HttpContext.Session.SetString("google_access_token", token.AccessToken);
        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            HttpContext.Session.SetString("google_refresh_token", token.RefreshToken);
        }

        var expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn, 0));
        HttpContext.Session.SetString("google_token_expiry_utc", expiresAtUtc.ToString("O"));
        HttpContext.Session.Remove("google_oauth_state");

        return Redirect("/?google=connected");
    }

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = string.Empty;
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
