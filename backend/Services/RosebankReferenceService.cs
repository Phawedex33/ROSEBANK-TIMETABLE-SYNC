using System.Text.Json;

namespace TimetableSync.Api.Services;

public sealed class RosebankReferenceService : IRosebankReferenceService
{
    private readonly Lazy<Dictionary<string, ReferenceRow>> _rows;

    public RosebankReferenceService(IHostEnvironment environment)
    {
        _rows = new Lazy<Dictionary<string, ReferenceRow>>(() => LoadReference(environment.ContentRootPath));
    }

    public bool TryGetClassDetails(int year, string group, string moduleCode, out string lecturer, out string venue)
    {
        lecturer = string.Empty;
        venue = string.Empty;
        var normalizedGroup = NormalizeGroup(group);
        var normalizedCode = (moduleCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedCode) || string.IsNullOrWhiteSpace(normalizedGroup))
        {
            return false;
        }

        var key = BuildKey(year, normalizedGroup, normalizedCode);
        if (!_rows.Value.TryGetValue(key, out var row))
        {
            return false;
        }

        lecturer = row.Lecturer;
        venue = row.Venue;
        return true;
    }

    private static Dictionary<string, ReferenceRow> LoadReference(string contentRoot)
    {
        var path = Path.Combine(contentRoot, "Data", "rosebank-reference.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, ReferenceRow>(StringComparer.OrdinalIgnoreCase);
        }

        using var stream = File.OpenRead(path);
        var dataset = JsonSerializer.Deserialize<ReferenceDataset>(stream) ?? new ReferenceDataset();
        var rows = new Dictionary<string, ReferenceRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in dataset.Rows.Where(x => x.Verified))
        {
            if (item.Year <= 0 || string.IsNullOrWhiteSpace(item.Group) || string.IsNullOrWhiteSpace(item.ModuleCode))
            {
                continue;
            }

            var key = BuildKey(item.Year, NormalizeGroup(item.Group), item.ModuleCode.Trim().ToUpperInvariant());
            rows[key] = new ReferenceRow(item.Lecturer?.Trim() ?? "TBA", item.Venue?.Trim() ?? "TBA");
        }

        return rows;
    }

    private static string NormalizeGroup(string? group)
    {
        var digits = new string((group ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
        {
            return string.Empty;
        }

        return $"GR{int.Parse(digits)}";
    }

    private static string BuildKey(int year, string group, string moduleCode) => $"{year}|{group}|{moduleCode}";

    private sealed class ReferenceDataset
    {
        public List<ReferenceRowInput> Rows { get; init; } = new();
    }

    private sealed class ReferenceRowInput
    {
        public bool Verified { get; init; }
        public int Year { get; init; }
        public string Group { get; init; } = string.Empty;
        public string ModuleCode { get; init; } = string.Empty;
        public string Lecturer { get; init; } = "TBA";
        public string Venue { get; init; } = "TBA";
    }

    private sealed record ReferenceRow(string Lecturer, string Venue);
}
