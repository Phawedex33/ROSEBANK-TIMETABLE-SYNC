using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public interface ICalendarExportService
{
    string BuildAcademicCalendar(AcademicSyncRequest request);
    string BuildAssessmentCalendar(AssessmentSyncRequest request);
}
