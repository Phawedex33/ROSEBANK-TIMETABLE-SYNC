namespace TimetableSync.Api.Models;

public sealed class PreviewTextRequest
{
    public ParseMode Mode { get; init; }
    public string Text { get; init; } = string.Empty;
    public bool UseAi { get; init; } = false;
}

