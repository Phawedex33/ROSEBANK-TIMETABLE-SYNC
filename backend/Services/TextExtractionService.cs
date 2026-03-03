using System.ComponentModel;
using System.Diagnostics;
using UglyToad.PdfPig;

namespace TimetableSync.Api.Services;

public sealed class TextExtractionService : ITextExtractionService
{
    private readonly ILogger<TextExtractionService> _logger;

    public TextExtractionService(ILogger<TextExtractionService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExtractAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        await using var stream = file.OpenReadStream();

        if (extension == ".pdf")
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;

            using var document = PdfDocument.Open(memory);
            var lines = document.GetPages().Select(p => p.Text);
            var text = string.Join(Environment.NewLine, lines);

            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            _logger.LogInformation("PDF appears to have no selectable text.");
            throw new InvalidOperationException("Scanned PDF detected with no selectable text. Convert page to image (PNG/JPG) and upload, or add a PDF-to-image OCR pipeline.");
        }

        if (extension is ".png" or ".jpg" or ".jpeg")
        {
            return await ExtractFromImageWithTesseractAsync(stream, extension, cancellationToken);
        }

        throw new InvalidOperationException("Unsupported file type. Use PDF, PNG, JPG, or JPEG.");
    }

    private static async Task<string> ExtractFromImageWithTesseractAsync(Stream stream, string extension, CancellationToken cancellationToken)
    {
        var tempImage = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        try
        {
            await using (var fileStream = File.Create(tempImage))
            {
                await stream.CopyToAsync(fileStream, cancellationToken);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "tesseract",
                Arguments = $"\"{tempImage}\" stdout -l eng --oem 1 --psm 6",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Tesseract OCR failed: {error}");
            }

            return output;
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException("Tesseract is not installed or not available in PATH. Install it and retry.");
        }
        finally
        {
            if (File.Exists(tempImage))
            {
                File.Delete(tempImage);
            }
        }
    }
}
