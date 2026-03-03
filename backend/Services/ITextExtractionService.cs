namespace TimetableSync.Api.Services;

public interface ITextExtractionService
{
    Task<string> ExtractAsync(IFormFile file, CancellationToken cancellationToken);
}
