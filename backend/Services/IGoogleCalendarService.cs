using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public interface IGoogleCalendarService
{
    Task<SyncResponse> CreateWeeklyEventsAsync(string userId, SyncRequest request, CancellationToken cancellationToken);
    Task<SyncResponse> CreateAssessmentEventsAsync(string userId, AssessmentSyncRequest request, CancellationToken cancellationToken);
    Task<SyncResponse> CreateExamEventsAsync(string userId, ExamSyncRequest request, CancellationToken cancellationToken);
    Task<CalendarDeleteResponse> DeleteManagedEventsAsync(string userId, CalendarDeleteRequest request, CancellationToken cancellationToken);
}
