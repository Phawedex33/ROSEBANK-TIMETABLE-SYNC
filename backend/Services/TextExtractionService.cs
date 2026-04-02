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
        // Hardened: Strictly whitelist extensions and use an internally generated safe filename.
        var safeExtension = extension switch
        {
            ".png" => ".png",
            ".jpg" => ".jpg",
            ".jpeg" => ".jpeg",
            _ => throw new InvalidOperationException("Invalid image extension.")
        };

        var tempImage = Path.Combine(Path.GetTempPath(), $"ocr_{Guid.NewGuid():N}{safeExtension}");
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
            bool isAsc = (page.Text ?? "").Contains("aSc Timetables", StringComparison.OrdinalIgnoreCase) || 
                         (page.Text ?? "").Contains("Rosebank College", StringComparison.OrdinalIgnoreCase) ||
                         byCoordinates.Contains("aSc Timetables", StringComparison.OrdinalIgnoreCase);

            if (isAsc)
            {
                var grid = ExtractAscTimetableGrid(page.Letters);
                if (!string.IsNullOrWhiteSpace(grid))
                {
                    var header = string.Join(Environment.NewLine, byCoordinates.Split('\n').Take(12));
                    pages.Add(header + Environment.NewLine + grid);
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(byCoordinates))
            {
                pages.Add(byCoordinates);
            }
            else if (!string.IsNullOrWhiteSpace(page.Text))
            {
                pages.Add(page.Text);
            }
        }

        return string.Join(Environment.NewLine, pages);
    }

    private record PdfWord(string Text, double Left, double Right, double Top, double Bottom)
    {
        public double CenterX => (Left + Right) / 2;
        public double CenterY => (Top + Bottom) / 2;
    }

    private static string ExtractAscTimetableGrid(IReadOnlyList<Letter> letters)
    {
        if (letters.Count == 0) return string.Empty;

        // ── Step 1: assemble words from letters, grouping by Y-line then by X-gap ──
        var words = new List<PdfWord>();
        var yBucket = 2.5;
        var groupedByLine = letters
            .GroupBy(l => Math.Round(l.GlyphRectangle.Bottom / yBucket) * yBucket)
            .OrderByDescending(g => g.Key);

        foreach (var line in groupedByLine)
        {
            var ordered = line.OrderBy(l => l.GlyphRectangle.Left).ToList();
            var currentWord = new StringBuilder();
            double wordLeft = 0, wordRight = 0, wordTop = 0, wordBottom = double.MaxValue;
            Letter? prev = null;

            foreach (var letter in ordered)
            {
                if (prev != null)
                {
                    var gap = letter.GlyphRectangle.Left - prev.GlyphRectangle.Right;
                    if (gap > Math.Max(2.0, prev.GlyphRectangle.Width * 0.4))
                    {
                        if (currentWord.Length > 0)
                        {
                            words.Add(new PdfWord(currentWord.ToString(), wordLeft, wordRight, wordTop, wordBottom));
                            currentWord.Clear();
                            wordBottom = double.MaxValue;
                        }
                    }
                }

                if (currentWord.Length == 0)
                {
                    wordLeft = letter.GlyphRectangle.Left;
                    wordTop = letter.GlyphRectangle.Top;
                    wordBottom = letter.GlyphRectangle.Bottom;
                }

                wordRight = letter.GlyphRectangle.Right;
                if (letter.GlyphRectangle.Top > wordTop) wordTop = letter.GlyphRectangle.Top;
                if (letter.GlyphRectangle.Bottom < wordBottom) wordBottom = letter.GlyphRectangle.Bottom;
                currentWord.Append(letter.Value);
                prev = letter;
            }

            if (currentWord.Length > 0)
                words.Add(new PdfWord(currentWord.ToString(), wordLeft, wordRight, wordTop, wordBottom));
        }

        // ── Step 2: locate day column (leftmost Mo/Tu/We/Th/Fr) ──
        var dayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };
        var rawDays = words.Where(w => dayNames.Contains(w.Text)).ToList();
        List<PdfWord> days;
        if (rawDays.Count > 0)
        {
            var leftMost = rawDays.Min(d => d.Left);
            days = rawDays.Where(w => w.Left <= leftMost + 20).OrderByDescending(w => w.CenterY).ToList();
        }
        else
        {
            return string.Empty;
        }

        // ── Step 3: locate period row (period numbers 1-12 on same Y as top-day or above) ──
        var periodNames = new HashSet<string> { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12" };
        var topDay = days.First();
        var periods = words
            .Where(w => periodNames.Contains(w.Text) && w.CenterY >= topDay.CenterY)
            .OrderBy(w => w.CenterX)
            .ToList();

        if (periods.Count == 0) return string.Empty;

        // ── Step 4: Bug 5 & 6 fix — detect group labels/ranges by X column ──
        var grComposites = new List<PdfWord>();
        var grWords = words.Where(w => w.Text.Equals("GR", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var grWord in grWords)
        {
            // Look for a digit/range word within 50pt horizontally on the same line
            var digitWord = words.FirstOrDefault(w =>
                Regex.IsMatch(w.Text, @"^([1-9][0-9]?)(?:\s*[-&,]\s*[1-9][0-9]?)?$", RegexOptions.IgnoreCase) &&
                Math.Abs(w.CenterY - grWord.CenterY) < 10 &&
                w.Left > grWord.Right &&
                (w.Left - grWord.Right) < 50);
            if (digitWord != null)
                grComposites.Add(new PdfWord("GR" + digitWord.Text, grWord.Left, digitWord.Right, grWord.Top, grWord.Bottom));
        }
        // Also accept already-merged patterns like "GR1-GR3"
        grComposites.AddRange(words.Where(w => Regex.IsMatch(w.Text, @"^GR[1-9][0-9]?(?:\s*[-&,]\s*[1-9][0-9]?)?$", RegexOptions.IgnoreCase)));
        
        // Deduplicate by position (same Left/Top within 5pt)
        var groupHeaders = grComposites
            .GroupBy(g => (Math.Round(g.Left / 5) * 5, Math.Round(g.CenterY / 5) * 5))
            .Select(g => g.First())
            .OrderBy(g => g.CenterX)
            .ToList();

        // Build period → group lookup
        var periodGroupMap = new Dictionary<double, string>();
        if (groupHeaders.Count > 0)
        {
            foreach (var period in periods)
            {
                var closest = groupHeaders.OrderBy(g => Math.Abs(g.CenterX - period.CenterX)).First();
                periodGroupMap[period.CenterX] = closest.Text.ToUpperInvariant().Replace(" ", "");
            }
        }

        // ── Step 5: extract cell text and emit grid rows ──
        var rows = new List<string>();
        foreach (var day in days)
        {
            var dayIndex = days.IndexOf(day);
            var dayStep = days.Count > 1 && dayIndex < days.Count - 1
                ? days[dayIndex].CenterY - days[dayIndex + 1].CenterY
                : 40.0;

            var topBound = day.CenterY + dayStep * 0.4;
            var bottomBound = day.CenterY - dayStep * 0.6;

            foreach (var period in periods)
            {
                var periodIndex = periods.IndexOf(period);
                var periodStep = periods.Count > 1 && periodIndex < periods.Count - 1
                    ? periods[periodIndex + 1].CenterX - periods[periodIndex].CenterX
                    : 60.0;

                var leftBound = period.CenterX - periodStep * 0.4;
                var rightBound = period.CenterX + periodStep * 0.6;

                var cellWords = words
                    .Where(w => w.CenterY <= topBound && w.CenterY >= bottomBound
                             && w.CenterX >= leftBound && w.CenterX <= rightBound)
                    .OrderByDescending(w => w.CenterY)
                    .ThenBy(w => w.Left)
                    .ToList();

                if (cellWords.Count == 0) continue;

                var rawCellText = string.Join(" ", cellWords.Select(w => w.Text));

                // Bug 3 fix: accept any cell that has letters+digits
                if (!Regex.IsMatch(rawCellText, @"[A-Z]{2,}\s*\d", RegexOptions.IgnoreCase)) continue;

                // Bug 4 fix: single-pass code repair
                var repairedCode = Regex.Replace(
                    rawCellText,
                    @"([A-Z]{2,4})\s+([A-Z]{0,2}\d+(?:\s*\d*)?)",
                    m =>
                    {
                        var letters2 = m.Groups[1].Value;
                        var digits = Regex.Replace(m.Groups[2].Value, @"\s+", "");
                        return letters2 + digits;
                    },
                    RegexOptions.IgnoreCase);

                // Bug 5 fix: group from column map
                var groupPrefix = periodGroupMap.TryGetValue(period.CenterX, out var grp)
                    ? grp + " "
                    : string.Empty;

                rows.Add($"{groupPrefix}{day.Text} {period.Text} {repairedCode}".Trim());
            }
        }

        return string.Join(Environment.NewLine, rows);
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
                        var spaces = (int)Math.Max(1, Math.Round(gap / (prevWidth * 0.4)));
                        sb.Append(new string(' ', Math.Min(spaces, 50)));
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
