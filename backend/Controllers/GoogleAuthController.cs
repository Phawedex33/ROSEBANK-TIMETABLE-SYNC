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
    private readonly ITokenStore _tokenStore;

    public GoogleAuthController(
        IHttpClientFactory httpClientFactory,
        IOptions<GoogleCalendarOptions> options,
        ITokenStore tokenStore)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _tokenStore = tokenStore;
    }

    private string GetSessionUserId()
    {
        if (Request.Cookies.TryGetValue("sync_user_id", out var userId) && !string.IsNullOrWhiteSpace(userId))
        {
            return userId;
        }

        userId = Guid.NewGuid().ToString("N");
        Response.Cookies.Append("sync_user_id", userId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true, // We are running https on 7068
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });

        return userId;
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
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var userId = GetSessionUserId();
        var token = await _tokenStore.LoadAsync(userId, cancellationToken);
        var connected = token is not null &&
            (!string.IsNullOrWhiteSpace(token.AccessToken) || !string.IsNullOrWhiteSpace(token.RefreshToken));

        return Ok(new
        {
            connected,
            email = (string?)null,
            expiresAtUtc = token?.ExpiresAtUtc.ToString("O")
        });
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        var userId = GetSessionUserId();
        await _tokenStore.ClearAsync(userId, cancellationToken);
        HttpContext.Session.Remove("google_oauth_state");
        return Ok(new { disconnected = true });
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
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

        var userId = GetSessionUserId();
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn, 0));
        await _tokenStore.SaveAsync(userId, new StoredToken(token.AccessToken, token.RefreshToken, expiresAt), cancellationToken);

        HttpContext.Session.Remove("google_oauth_state");

        return Redirect("/?google=connected");
    }

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
