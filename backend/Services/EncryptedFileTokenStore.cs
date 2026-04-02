using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;

namespace TimetableSync.Api.Services;

/// <summary>
/// Encrypted, multi-tenant file-based token store. 
/// Uses IDataProtectionProvider to encrypt tokens at rest.
/// Partitions tokens by userId to ensure tenant isolation.
/// </summary>
public sealed class EncryptedFileTokenStore : ITokenStore
{
    private readonly string _baseDir;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public EncryptedFileTokenStore(IHostEnvironment environment, IDataProtectionProvider dataProtectionProvider)
    {
        _baseDir = Path.Combine(environment.ContentRootPath, "timetable-sync-token");
        Directory.CreateDirectory(_baseDir);
        _protector = dataProtectionProvider.CreateProtector("TimetableSync.TokenStorage");
    }

    private string GetUserTokenPath(string userId)
    {
        // Sanitize userId to prevent path traversal
        var safeUserId = string.Concat(userId.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeUserId))
        {
            throw new ArgumentException("Invalid user ID.", nameof(userId));
        }
        return Path.Combine(_baseDir, $"token_{safeUserId}.dat");
    }

    public async Task<StoredToken?> LoadAsync(string userId, CancellationToken cancellationToken = default)
    {
        var path = GetUserTokenPath(userId);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return null;

            var encryptedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var decryptedBytes = _protector.Unprotect(encryptedBytes);
            
            var dto = JsonSerializer.Deserialize<TokenFileDto>(decryptedBytes, JsonOptions);
            if (dto is null || string.IsNullOrWhiteSpace(dto.AccessToken)) return null;

            return new StoredToken(dto.AccessToken, dto.RefreshToken, dto.ExpiresAtUtc);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            // If we can't decrypt or parse, treat it as missing
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(string userId, StoredToken token, CancellationToken cancellationToken = default)
    {
        var path = GetUserTokenPath(userId);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var dto = new TokenFileDto
            {
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken,
                ExpiresAtUtc = token.ExpiresAtUtc
            };

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
            var encryptedBytes = _protector.Protect(jsonBytes);

            var tmpPath = $"{path}.tmp";
            await File.WriteAllBytesAsync(tmpPath, encryptedBytes, cancellationToken);
            File.Move(tmpPath, path, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAsync(string userId, CancellationToken cancellationToken = default)
    {
        var path = GetUserTokenPath(userId);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed class TokenFileDto
    {
        public string AccessToken { get; init; } = string.Empty;
        public string? RefreshToken { get; init; }
        public DateTimeOffset ExpiresAtUtc { get; init; }
    }
}
