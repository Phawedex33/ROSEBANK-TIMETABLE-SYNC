namespace TimetableSync.Api.Models;

public sealed class SyncResponse
{
    public int Created { get; set; }
    public List<string> EventIds { get; set; } = new();
}
