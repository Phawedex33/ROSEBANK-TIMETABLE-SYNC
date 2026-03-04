namespace TimetableSync.Api.Models;

public sealed class CalendarDeleteResponse
{
    public int Deleted { get; set; }
    public List<string> EventIds { get; set; } = new();
}
