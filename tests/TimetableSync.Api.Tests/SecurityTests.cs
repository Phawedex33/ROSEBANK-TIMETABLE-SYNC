using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;
using Moq;
using TimetableSync.Api.Services;
using Xunit;
using FluentAssertions;

namespace TimetableSync.Api.Tests;

public class SecurityTests
{
    private readonly Mock<IHostEnvironment> _mockEnv;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly string _testDir;

    public SecurityTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "timetable-sync-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        _mockEnv = new Mock<IHostEnvironment>();
        _mockEnv.Setup(m => m.ContentRootPath).Returns(_testDir);

        // Simple ephemeral data protection for tests
        _dataProtection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_testDir, "keys")));
    }

    [Fact]
    public async Task EncryptedFileTokenStore_SavesEncryptedData()
    {
        // Arrange
        var store = new EncryptedFileTokenStore(_mockEnv.Object, _dataProtection);
        var token = new StoredToken("secret-access", "secret-refresh", DateTimeOffset.UtcNow.AddHours(1));
        var userId = "user1";

        // Act
        await store.SaveAsync(userId, token);

        // Assert
        var tokenDir = Path.Combine(_testDir, "timetable-sync-token");
        var tokenFile = Path.Combine(tokenDir, "token_user1.dat");
        
        File.Exists(tokenFile).Should().BeTrue();
        
        var rawContent = await File.ReadAllBytesAsync(tokenFile);
        var rawString = System.Text.Encoding.UTF8.GetString(rawContent);
        
        // The raw string should NOT contain the plain-text access token
        rawString.Should().NotContain("secret-access");
        
        // But the store should be able to load and decrypt it
        var loaded = await store.LoadAsync(userId);
        loaded.Should().NotBeNull();
        loaded!.AccessToken.Should().Be("secret-access");
    }

    [Fact]
    public async Task EncryptedFileTokenStore_IsolatesUsers()
    {
        // Arrange
        var store = new EncryptedFileTokenStore(_mockEnv.Object, _dataProtection);
        var token1 = new StoredToken("token1", null, DateTimeOffset.UtcNow.AddHours(1));
        var token2 = new StoredToken("token2", null, DateTimeOffset.UtcNow.AddHours(1));

        // Act
        await store.SaveAsync("user1", token1);
        await store.SaveAsync("user2", token2);

        // Assert
        var loaded1 = await store.LoadAsync("user1");
        var loaded2 = await store.LoadAsync("user2");

        loaded1!.AccessToken.Should().Be("token1");
        loaded2!.AccessToken.Should().Be("token2");
    }

    ~SecurityTests()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }
}
