namespace TimetableSync.Api.Services;

public sealed class ReferenceAdminOptions
{
    public bool Enabled { get; init; }
    public bool LocalhostOnly { get; init; } = true;
    public string AdminKey { get; init; } = string.Empty;
}
