using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public interface IGoogleCalendarService
{
    Task<SyncResponse> CreateWeeklyEventsAsync(SyncRequest request, CancellationToken cancellationToken);
}
