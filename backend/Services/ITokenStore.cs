namespace TimetableSync.Api.Services;

/// <summary>
/// Persists and retrieves the Google OAuth token so it survives server restarts.
/// </summary>
public interface ITokenStore
{
    Task<StoredToken?> LoadAsync(string userId, CancellationToken cancellationToken = default);
    Task SaveAsync(string userId, StoredToken token, CancellationToken cancellationToken = default);
    Task ClearAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed record StoredToken(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAtUtc);
