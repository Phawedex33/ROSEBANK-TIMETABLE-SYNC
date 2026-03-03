using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public interface IAssessmentParser
{
    AssessmentPreviewResponse Parse(string input);
}
