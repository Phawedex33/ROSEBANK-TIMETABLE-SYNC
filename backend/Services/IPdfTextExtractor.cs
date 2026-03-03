namespace TimetableSync.Api.Services;

public interface IPdfTextExtractor
{
    Task<string> ExtractAsync(IFormFile file, CancellationToken cancellationToken);
}
