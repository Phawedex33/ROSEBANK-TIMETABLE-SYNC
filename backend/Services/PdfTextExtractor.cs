namespace TimetableSync.Api.Services;

public sealed class PdfTextExtractor : IPdfTextExtractor
{
    private readonly ITextExtractionService _textExtraction;

    public PdfTextExtractor(ITextExtractionService textExtraction)
    {
        _textExtraction = textExtraction;
    }

    public Task<string> ExtractAsync(IFormFile file, CancellationToken cancellationToken)
    {
        return _textExtraction.ExtractAsync(file, cancellationToken);
    }
}
