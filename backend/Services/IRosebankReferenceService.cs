namespace TimetableSync.Api.Services;

public interface IRosebankReferenceService
{
    /// <summary>Lookup by module code (fallback for simple timetables).</summary>
    bool TryGetClassDetails(int year, string group, string moduleCode, out string lecturer, out string venue);
    /// <summary>Lookup by exact day + period (preferred, used for rotating schedules).</summary>
    bool TryGetSlotDetails(int year, string group, DayOfWeek day, int period, out string moduleCode, out string lecturer, out string venue);
    IReadOnlyList<RosebankReferenceRow> GetAllRows();
    Task SaveRowsAsync(IEnumerable<RosebankReferenceRow> rows, CancellationToken cancellationToken);
    Task<int> ImportCsvAsync(Stream csvStream, CancellationToken cancellationToken);
}
