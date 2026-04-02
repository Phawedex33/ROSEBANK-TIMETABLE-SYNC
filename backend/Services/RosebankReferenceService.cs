using System.Text.Json;

namespace TimetableSync.Api.Services;

public sealed class RosebankReferenceService : IRosebankReferenceService
{
    private readonly string _datasetPath;
    private readonly object _sync = new();
    private List<RosebankReferenceRow> _allRows;
    private Dictionary<string, ReferenceRow> _verifiedRows;        // key: year|group|moduleCode
    private Dictionary<string, SlotRow> _slotRows;                 // key: year|group|day|period

    public RosebankReferenceService(IHostEnvironment environment)
    {
        _datasetPath = Path.Combine(environment.ContentRootPath, "Data", "rosebank-reference.json");
        (_allRows, _verifiedRows, _slotRows) = LoadReference(_datasetPath);
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

    public bool TryGetSlotDetails(int year, string group, DayOfWeek day, int period, out string moduleCode, out string lecturer, out string venue)
    {
        moduleCode = string.Empty;
        lecturer = string.Empty;
        venue = string.Empty;
        var normalizedGroup = NormalizeGroup(group);
        if (string.IsNullOrWhiteSpace(normalizedGroup)) return false;

        var key = BuildSlotKey(year, normalizedGroup, day.ToString(), period);
        lock (_sync)
        {
            if (!_slotRows.TryGetValue(key, out var row)) return false;
            moduleCode = row.ModuleCode;
            lecturer = row.Lecturer;
            venue = row.Venue;
            return true;
        }
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
                Day = x.Day,
                Period = x.Period,
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
        var (allRows, verifiedRows, slotRows) = LoadReference(_datasetPath);

        lock (_sync)
        {
            _allRows = allRows;
            _verifiedRows = verifiedRows;
            _slotRows = slotRows;
        }
    }

    public async Task<int> ImportCsvAsync(Stream csvStream, CancellationToken cancellationToken)
    {
        var importedRows = new List<RosebankReferenceRow>();
        using (var reader = new StreamReader(csvStream))
        {
            var header = await reader.ReadLineAsync(); // Skip header
            while (await reader.ReadLineAsync() is string line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 5) continue;

                importedRows.Add(new RosebankReferenceRow
                {
                    Year = int.TryParse(parts[0], out var y) ? y : 0,
                    Group = parts[1].Trim(),
                    ModuleCode = parts[2].Trim(),
                    Lecturer = parts[3].Trim(),
                    Venue = parts[4].Trim(),
                    Verified = parts.Length > 5 && (bool.TryParse(parts[5], out var v) ? v : true),
                    Day = parts.Length > 6 ? parts[6].Trim() : null,
                    Period = parts.Length > 7 && int.TryParse(parts[7], out var p) ? p : null
                });
            }
        }

        if (importedRows.Count == 0) return 0;

        List<RosebankReferenceRow> allRowsCopy;
        lock (_sync)
        {
            allRowsCopy = _allRows.ToList();
        }

        var existingKeys = new HashSet<string>(allRowsCopy.Select(r => $"{r.Year}|{NormalizeGroup(r.Group)}|{r.ModuleCode.Trim().ToUpperInvariant()}|{r.Day}|{r.Period}"), StringComparer.OrdinalIgnoreCase);
        int addedCount = 0;

        foreach (var row in importedRows)
        {
            var normalized = NormalizeRow(row);
            var key = $"{normalized.Year}|{normalized.Group}|{normalized.ModuleCode}|{normalized.Day}|{normalized.Period}";
            if (existingKeys.Add(key))
            {
                allRowsCopy.Add(normalized);
                addedCount++;
            }
        }

        if (addedCount > 0)
        {
            await SaveRowsAsync(allRowsCopy, cancellationToken);
        }

        return addedCount;
    }

    private static (List<RosebankReferenceRow> AllRows, Dictionary<string, ReferenceRow> VerifiedRows, Dictionary<string, SlotRow> SlotRows) LoadReference(string path)
    {
        if (!File.Exists(path))
        {
            return (new(), new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase));
        }

        using var stream = File.OpenRead(path);
        var dataset = JsonSerializer.Deserialize<ReferenceDataset>(stream) ?? new ReferenceDataset();
        var allRows = new List<RosebankReferenceRow>();
        var verifiedRows = new Dictionary<string, ReferenceRow>(StringComparer.OrdinalIgnoreCase);
        var slotRows = new Dictionary<string, SlotRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in dataset.Rows.Select(NormalizeRow))
        {
            if (item.Year <= 0 || string.IsNullOrWhiteSpace(item.Group) || string.IsNullOrWhiteSpace(item.ModuleCode))
            {
                continue;
            }

            allRows.Add(item);
            if (!item.Verified) continue;

            // If row has Day + Period, register it as a per-slot entry
            if (!string.IsNullOrWhiteSpace(item.Day) && item.Period.HasValue)
            {
                var slotKey = BuildSlotKey(item.Year, NormalizeGroup(item.Group), item.Day, item.Period.Value);
                slotRows[slotKey] = new SlotRow(item.ModuleCode.Trim().ToUpperInvariant(), item.Lecturer, item.Venue);
            }

            // Always also register the module-level entry as fallback
            var key = BuildKey(item.Year, NormalizeGroup(item.Group), item.ModuleCode.Trim().ToUpperInvariant());
            verifiedRows[key] = new ReferenceRow(item.Lecturer, item.Venue);
        }

        return (allRows, verifiedRows, slotRows);
    }

    private static RosebankReferenceRow NormalizeRow(ReferenceRowInput row)
    {
        return NormalizeRow(new RosebankReferenceRow
        {
            Verified = row.Verified,
            Year = row.Year,
            Group = row.Group,
            ModuleCode = row.ModuleCode,
            Day = row.Day,
            Period = row.Period,
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
            Day = string.IsNullOrWhiteSpace(row.Day) ? null : row.Day.Trim(),
            Period = row.Period,
            Lecturer = lecturer,
            Venue = venue
        };
    }

    private static string NormalizeGroup(string? group)
    {
        var digits = new string((group ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits)) return string.Empty;
        return $"GR{int.Parse(digits)}";
    }

    private static string BuildKey(int year, string group, string moduleCode) => $"{year}|{group}|{moduleCode}";
    private static string BuildSlotKey(int year, string group, string day, int period) => $"{year}|{group}|{day}|{period}";

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
        public string? Day { get; init; }
        public int? Period { get; init; }
        public string Lecturer { get; init; } = "TBA";
        public string Venue { get; init; } = "TBA";
    }

    private sealed record ReferenceRow(string Lecturer, string Venue);
    private sealed record SlotRow(string ModuleCode, string Lecturer, string Venue);
}
