namespace TimetableSync.Api.Services;

public interface IRosebankReferenceService
{
    bool TryGetClassDetails(int year, string group, string moduleCode, out string lecturer, out string venue);
}
