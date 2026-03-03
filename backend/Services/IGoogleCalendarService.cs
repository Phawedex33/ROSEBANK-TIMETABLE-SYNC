using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public interface IGoogleCalendarService
{
    Task<SyncResponse> CreateWeeklyEventsAsync(SyncRequest request, CancellationToken cancellationToken);
    Task<SyncResponse> CreateAssessmentEventsAsync(AssessmentSyncRequest request, CancellationToken cancellationToken);
    Task<SyncResponse> CreateExamEventsAsync(ExamSyncRequest request, CancellationToken cancellationToken);
}
