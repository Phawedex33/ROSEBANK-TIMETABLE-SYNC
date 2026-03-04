using System.Text.Json;

namespace TimetableSync.Api.Services;

public sealed class RosebankReferenceService : IRosebankReferenceService
{
    private readonly string _datasetPath;
    private readonly object _sync = new();
    private List<RosebankReferenceRow> _allRows;
    private Dictionary<string, ReferenceRow> _verifiedRows;

    public RosebankReferenceService(IHostEnvironment environment)
    {
        _datasetPath = Path.Combine(environment.ContentRootPath, "Data", "rosebank-reference.json");
        (_allRows, _verifiedRows) = LoadReference(_datasetPath);
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
        if (!_verifiedRows.TryGetValue(key, out var row))
        {
            return false;
        }

        lecturer = row.Lecturer;
        venue = row.Venue;
        return true;
    }

    public IReadOnlyList<RosebankReferenceRow> GetAllRows()
    {
        lock (_sync)
        {
            return _allRows
                .Select(x => new RosebankReferenceRow
                {
                    Verified = x.Verified,
                    Year = x.Year,
                    Group = x.Group,
                    ModuleCode = x.ModuleCode,
                    Lecturer = x.Lecturer,
                    Venue = x.Venue
                })
                .ToList();
        }
    }

    public async Task SaveRowsAsync(IEnumerable<RosebankReferenceRow> rows, CancellationToken cancellationToken)
    {
        var normalizedRows = (rows ?? Array.Empty<RosebankReferenceRow>())
            .Select(NormalizeRow)
            .Where(x => x.Year > 0 && !string.IsNullOrWhiteSpace(x.Group) && !string.IsNullOrWhiteSpace(x.ModuleCode))
            .ToList();

        var dataset = new ReferenceDataset
        {
            Rows = normalizedRows.Select(x => new ReferenceRowInput
            {
                Verified = x.Verified,
                Year = x.Year,
                Group = x.Group,
                ModuleCode = x.ModuleCode,
                Lecturer = x.Lecturer,
                Venue = x.Venue
            }).ToList()
        };

        var tempPath = $"{_datasetPath}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, dataset, new JsonSerializerOptions
            {
                WriteIndented = true
            }, cancellationToken);
        }

        File.Move(tempPath, _datasetPath, overwrite: true);
        var (allRows, verifiedRows) = LoadReference(_datasetPath);

        lock (_sync)
        {
            _allRows = allRows;
            _verifiedRows = verifiedRows;
        }
    }

    private static (List<RosebankReferenceRow> AllRows, Dictionary<string, ReferenceRow> VerifiedRows) LoadReference(string path)
    {
        if (!File.Exists(path))
        {
            return (new List<RosebankReferenceRow>(), new Dictionary<string, ReferenceRow>(StringComparer.OrdinalIgnoreCase));
        }

        using var stream = File.OpenRead(path);
        var dataset = JsonSerializer.Deserialize<ReferenceDataset>(stream) ?? new ReferenceDataset();
        var allRows = new List<RosebankReferenceRow>();
        var verifiedRows = new Dictionary<string, ReferenceRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in dataset.Rows.Select(NormalizeRow))
        {
            if (item.Year <= 0 || string.IsNullOrWhiteSpace(item.Group) || string.IsNullOrWhiteSpace(item.ModuleCode))
            {
                continue;
            }

            allRows.Add(item);
            if (!item.Verified)
            {
                continue;
            }

            var key = BuildKey(item.Year, NormalizeGroup(item.Group), item.ModuleCode.Trim().ToUpperInvariant());
            verifiedRows[key] = new ReferenceRow(item.Lecturer, item.Venue);
        }

        return (allRows, verifiedRows);
    }

    private static RosebankReferenceRow NormalizeRow(ReferenceRowInput row)
    {
        return NormalizeRow(new RosebankReferenceRow
        {
            Verified = row.Verified,
            Year = row.Year,
            Group = row.Group,
            ModuleCode = row.ModuleCode,
            Lecturer = row.Lecturer,
            Venue = row.Venue
        });
    }

    private static RosebankReferenceRow NormalizeRow(RosebankReferenceRow row)
    {
        var group = NormalizeGroup(row.Group);
        var moduleCode = (row.ModuleCode ?? string.Empty).Trim().ToUpperInvariant();
        var lecturer = string.IsNullOrWhiteSpace(row.Lecturer) ? "TBA" : row.Lecturer.Trim();
        var venue = string.IsNullOrWhiteSpace(row.Venue) ? "TBA" : row.Venue.Trim().ToUpperInvariant();

        return new RosebankReferenceRow
        {
            Verified = row.Verified,
            Year = row.Year,
            Group = group,
            ModuleCode = moduleCode,
            Lecturer = lecturer,
            Venue = venue
        };
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
