namespace TimetableSync.Api.Services;

/// <summary>Row in the reference dataset. Day and Period are optional — when blank, acts as a module-level fallback.</summary>
public sealed class RosebankReferenceRow
{
    public bool Verified { get; init; }
    public int Year { get; init; }
    public string Group { get; init; } = string.Empty;
    public string ModuleCode { get; init; } = string.Empty;
    /// <summary>Optional: full day name e.g. "Monday". When set, this row applies only to that day.</summary>
    public string? Day { get; init; }
    /// <summary>Optional: period number (1–12). When set with Day, this row applies to that exact slot.</summary>
    public int? Period { get; init; }
    public string Lecturer { get; init; } = "TBA";
    public string Venue { get; init; } = "TBA";
}
