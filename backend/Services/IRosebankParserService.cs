using TimetableSync.Api.Models;

namespace TimetableSync.Api.Services;

public interface IRosebankParserService
{
    Task<object> ParseAsync(RosebankParseRequest request, CancellationToken cancellationToken);
}
