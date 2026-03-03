using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;
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

            string text;
            try
            {
                text = ExtractTextWithPdfPig(memory.ToArray());
            }
            catch
            {
                text = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                text = ExtractTextFromPdfBytes(memory.ToArray());
            }

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

    private static string ExtractTextFromPdfBytes(byte[] pdfBytes)
    {
        var latin = Encoding.Latin1.GetString(pdfBytes);
        var streamMatches = Regex.Matches(
            latin,
            @"stream\r?\n(.*?)\r?\nendstream",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        var chunks = new List<string>();
        foreach (Match match in streamMatches)
        {
            var streamData = Encoding.Latin1.GetBytes(match.Groups[1].Value);
            var decompressed = TryDecompressZlib(streamData);
            if (decompressed is null || decompressed.Length == 0)
            {
                continue;
            }

            var content = Encoding.Latin1.GetString(decompressed);
            ExtractPdfTextOperators(content, chunks);
        }

        if (chunks.Count > 0)
        {
            return NormalizeExtractedPdfText(chunks);
        }

        var ascii = Regex.Matches(latin, @"[A-Za-z0-9][A-Za-z0-9\s\-\:\(\)\[\]\/]{20,}")
            .Select(m => m.Value.Trim())
            .Distinct()
            .ToArray();

        return string.Join(Environment.NewLine, ascii);
    }

    private static byte[]? TryDecompressZlib(byte[] data)
    {
        try
        {
            using var input = new MemoryStream(data);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static void ExtractPdfTextOperators(string content, ICollection<string> chunks)
    {
        var tjMatches = Regex.Matches(content, @"\(([^)]{1,300})\)\s*Tj");
        foreach (Match m in tjMatches)
        {
            var text = DecodePdfEscapes(m.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                chunks.Add(text);
            }
        }

        var tjArrayMatches = Regex.Matches(content, @"\[(.*?)\]\s*TJ", RegexOptions.Singleline);
        foreach (Match m in tjArrayMatches)
        {
            var parts = Regex.Matches(m.Groups[1].Value, @"\(([^)]{1,300})\)")
                .Select(x => DecodePdfEscapes(x.Groups[1].Value));
            var joined = string.Concat(parts);
            if (!string.IsNullOrWhiteSpace(joined))
            {
                chunks.Add(joined);
            }
        }
    }

    private static string DecodePdfEscapes(string value)
    {
        return value
            .Replace("\\(", "(")
            .Replace("\\)", ")")
            .Replace("\\n", " ")
            .Replace("\\r", " ")
            .Replace("\\t", " ")
            .Replace("\\\\", "\\");
    }

    private static string NormalizeExtractedPdfText(IEnumerable<string> chunks)
    {
        var cleaned = chunks
            .Select(c => Regex.Replace(c, @"\s+", " ").Trim())
            .Where(c => c.Length > 0)
            .ToList();

        return string.Join(Environment.NewLine, cleaned);
    }

    private static string ExtractTextWithPdfPig(byte[] pdfBytes)
    {
        using var memory = new MemoryStream(pdfBytes);
        using var document = PdfDocument.Open(memory);

        var pages = new List<string>();
        foreach (var page in document.GetPages())
        {
            var byCoordinates = ExtractTextByCoordinates(page.Letters);
            if (!string.IsNullOrWhiteSpace(byCoordinates))
            {
                pages.Add(byCoordinates);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(page.Text))
            {
                pages.Add(page.Text);
            }
        }

        return string.Join(Environment.NewLine, pages);
    }

    private static string ExtractTextByCoordinates(IReadOnlyList<Letter> letters)
    {
        if (letters.Count == 0)
        {
            return string.Empty;
        }

        const double yBucket = 2.5;
        var grouped = letters
            .GroupBy(l => Math.Round(l.GlyphRectangle.Bottom / yBucket) * yBucket)
            .OrderByDescending(g => g.Key);

        var lines = new List<string>();
        foreach (var group in grouped)
        {
            var ordered = group
                .OrderBy(l => l.GlyphRectangle.Left)
                .ToList();

            var sb = new StringBuilder();
            Letter? prev = null;
            foreach (var letter in ordered)
            {
                if (prev is not null)
                {
                    var gap = letter.GlyphRectangle.Left - prev.GlyphRectangle.Right;
                    var prevWidth = Math.Max(prev.GlyphRectangle.Width, 1);
                    if (gap > Math.Max(2.5, prevWidth * 0.6))
                    {
                        sb.Append(' ');
                    }
                }

                sb.Append(letter.Value);
                prev = letter;
            }

            var line = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
