using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public interface IAcademicParser
{
    AcademicPreviewResponse Parse(string input, int year, string group);
}
