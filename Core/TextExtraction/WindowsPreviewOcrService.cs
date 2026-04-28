using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using PdfiumViewer;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// Uses <see cref="OcrEngine"/> (installed Windows OCR language packs) to produce word-level bounding boxes for on-image selection.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsPreviewOcrService : IWindowsPreviewOcrLayout
{
    public async Task<IReadOnlyList<PreviewOcrRegion>> ExtractLineRegionsAsync(string imagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return Array.Empty<PreviewOcrRegion>();

        IRandomAccessStream? stream = null;
        SoftwareBitmap? softwareBitmap = null;
        try
        {
            stream = await FileRandomAccessStream.OpenAsync(imagePath, FileAccessMode.Read)
                .AsTask(ct).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
            softwareBitmap = await decoder.GetSoftwareBitmapAsync().AsTask(ct).ConfigureAwait(false);

            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                var converted = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                softwareBitmap.Dispose();
                softwareBitmap = converted;
            }

            var allRegions = new List<PreviewOcrRegion>();

            var arabicEngine = TryCreateArabicEngine();
            if (arabicEngine != null)
            {
                var arabicResult = await arabicEngine.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);
                var arabicRegions = BuildLineRegions(arabicResult);
                allRegions.AddRange(arabicRegions);
            }

            var englishEngine = TryCreateEnglishEngine();
            if (englishEngine != null)
            {
                var englishResult = await englishEngine.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);
                var englishRegions = BuildLineRegions(englishResult);
                
                foreach (var enRegion in englishRegions)
                {
                    var isDuplicate = false;
                    foreach (var arRegion in allRegions)
                    {
                        var iou = ComputeIoU(enRegion, arRegion);
                        if (iou > 0.5)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }
                    
                    if (!isDuplicate)
                        allRegions.Add(enRegion);
                }
            }

            if (allRegions.Count == 0)
            {
                var fallbackEngine = CreateBestOcrEngine();
                if (fallbackEngine != null)
                {
                    var fallbackResult = await fallbackEngine.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);
                    allRegions.AddRange(BuildLineRegions(fallbackResult));
                }
            }

            return allRegions.OrderBy(r => r.Y).ThenBy(r => r.X).ToList();
        }
        finally
        {
            softwareBitmap?.Dispose();
            stream?.Dispose();
        }
    }

    public async Task<IReadOnlyList<PreviewOcrRegion>> ExtractPdfPageRegionsAsync(string pdfPath, int pageIndex0, float renderDpi, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        return Array.Empty<PreviewOcrRegion>();
    }

    public async Task<OcrSelectableTextLayout?> ExtractSelectableTextLayoutAsync(string imagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return null;

        IRandomAccessStream? stream = null;
        SoftwareBitmap? softwareBitmap = null;
        try
        {
            stream = await FileRandomAccessStream.OpenAsync(imagePath, FileAccessMode.Read)
                .AsTask(ct).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
            softwareBitmap = await decoder.GetSoftwareBitmapAsync().AsTask(ct).ConfigureAwait(false);

            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                var converted = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                softwareBitmap.Dispose();
                softwareBitmap = converted;
            }

            var lineGroups = new List<List<OcrWordToken>>();
            var arabicLineBoxes = new List<PreviewOcrRegion>();

            var arabicEngine = TryCreateArabicEngine();
            if (arabicEngine != null)
            {
                var arabicResult = await arabicEngine.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);
                AppendWordLines(arabicResult, lineGroups, arabicLineBoxes);
            }

            var englishEngine = TryCreateEnglishEngine();
            if (englishEngine != null)
            {
                var englishResult = await englishEngine.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);
                AppendWordLinesDeduped(englishResult, lineGroups, arabicLineBoxes);
            }

            if (lineGroups.Count == 0)
            {
                var fallbackEngine = CreateBestOcrEngine();
                if (fallbackEngine != null)
                {
                    var fallbackResult = await fallbackEngine.RecognizeAsync(softwareBitmap).AsTask(ct).ConfigureAwait(false);
                    AppendWordLines(fallbackResult, lineGroups, null);
                }
            }

            if (lineGroups.Count == 0)
                return null;

            var layout = OcrSelectableTextLayoutBuilder.BuildFromPreGroupedLines(lineGroups);
            return layout.Spans.Count == 0 ? null : layout;
        }
        finally
        {
            softwareBitmap?.Dispose();
            stream?.Dispose();
        }
    }

    private static void AppendWordLines(OcrResult result, List<List<OcrWordToken>> dest, List<PreviewOcrRegion>? alsoAppendRegions)
    {
        foreach (var line in result.Lines)
        {
            var pr = LineToPreviewRegion(line);
            if (pr == null) continue;
            var words = WordsFromOcrLine(line);
            if (words.Count == 0) continue;
            dest.Add(words);
            alsoAppendRegions?.Add(pr);
        }
    }

    private static void AppendWordLinesDeduped(OcrResult result, List<List<OcrWordToken>> dest, List<PreviewOcrRegion> arabicLineBoxes)
    {
        foreach (var line in result.Lines)
        {
            var pr = LineToPreviewRegion(line);
            if (pr == null) continue;
            var isDuplicate = false;
            foreach (var ar in arabicLineBoxes)
            {
                if (ComputeIoU(pr, ar) > 0.5)
                {
                    isDuplicate = true;
                    break;
                }
            }

            if (isDuplicate) continue;
            var words = WordsFromOcrLine(line);
            if (words.Count > 0)
                dest.Add(words);
        }
    }

    private static PreviewOcrRegion? LineToPreviewRegion(OcrLine line)
    {
        var lineText = line.Text ?? "";
        if (string.IsNullOrWhiteSpace(lineText))
            return null;

        if (line.Words == null || line.Words.Count == 0)
            return null;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var w in line.Words)
        {
            var r = w.BoundingRect;
            minX = Math.Min(minX, r.X);
            minY = Math.Min(minY, r.Y);
            maxX = Math.Max(maxX, r.X + r.Width);
            maxY = Math.Max(maxY, r.Y + r.Height);
        }

        if (minX > maxX || minY > maxY)
            return null;

        return new PreviewOcrRegion(minX, minY, maxX - minX, maxY - minY, lineText);
    }

    private static List<OcrWordToken> WordsFromOcrLine(OcrLine line)
    {
        var list = new List<OcrWordToken>();
        if (line.Words == null) return list;
        foreach (var w in line.Words)
        {
            var r = w.BoundingRect;
            var t = w.Text?.TrimEnd() ?? "";
            if (string.IsNullOrWhiteSpace(t)) continue;
            list.Add(new OcrWordToken(t, r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)));
        }

        return list;
    }

    /// <summary>
    /// Try to create an Arabic OCR engine.
    /// </summary>
    private static OcrEngine? TryCreateArabicEngine()
    {
        var available = OcrEngine.AvailableRecognizerLanguages;
        
        var arabic = available.FirstOrDefault(l => l.LanguageTag.StartsWith("ar", StringComparison.OrdinalIgnoreCase));
        if (arabic != null)
        {
            var engine = OcrEngine.TryCreateFromLanguage(arabic);
            if (engine != null)
                return engine;
        }

        try
        {
            return OcrEngine.TryCreateFromLanguage(new Language("ar-SA")) 
                ?? OcrEngine.TryCreateFromLanguage(new Language("ar"));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Try to create an English OCR engine.
    /// </summary>
    private static OcrEngine? TryCreateEnglishEngine()
    {
        var available = OcrEngine.AvailableRecognizerLanguages;
        
        var english = available.FirstOrDefault(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (english != null)
        {
            var engine = OcrEngine.TryCreateFromLanguage(english);
            if (engine != null)
                return engine;
        }

        try
        {
            return OcrEngine.TryCreateFromLanguage(new Language("en-US")) 
                ?? OcrEngine.TryCreateFromLanguage(new Language("en"));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Prefer user profile (multi-language when packs are installed), then any available recognizer, then common tags.
    /// </summary>
    private static OcrEngine? CreateBestOcrEngine()
    {
        var e = OcrEngine.TryCreateFromUserProfileLanguages();
        if (e != null)
            return e;

        foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
        {
            e = OcrEngine.TryCreateFromLanguage(lang);
            if (e != null)
                return e;
        }

        static OcrEngine? TryTag(string bcp47)
        {
            try
            {
                return OcrEngine.TryCreateFromLanguage(new Language(bcp47));
            }
            catch
            {
                return null;
            }
        }

        return TryTag("ar-SA") ?? TryTag("ar") ?? TryTag("en-US") ?? TryTag("en");
    }

    /// <summary>
    /// Returns one region per recognized line using the OCR engine's canonical text.
    /// </summary>
    private static IReadOnlyList<PreviewOcrRegion> BuildLineRegions(OcrResult result)
    {
        var list = new List<PreviewOcrRegion>();
        foreach (var line in result.Lines)
        {
            var pr = LineToPreviewRegion(line);
            if (pr != null)
                list.Add(pr);
        }

        return list;
    }

    /// <summary>
    /// Converts a GDI+ Bitmap (from PDF rendering) to WinRT SoftwareBitmap for Windows OCR.
    /// Uses memory copy via BitmapData to avoid temporary files.
    /// </summary>
    private static async Task<SoftwareBitmap> ConvertGdiBitmapToSoftwareBitmapAsync(Bitmap gdiBitmap, CancellationToken ct)
    {
        using var memStream = new MemoryStream();
        gdiBitmap.Save(memStream, ImageFormat.Png);
        memStream.Position = 0;

        var ras = new InMemoryRandomAccessStream();
        await ras.WriteAsync(memStream.ToArray().AsBuffer()).AsTask(ct);
        ras.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(ras).AsTask(ct);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync().AsTask(ct);

        if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
            softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
        {
            var converted = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            softwareBitmap.Dispose();
            return converted;
        }

        return softwareBitmap;
    }

    /// <summary>
    /// Compute Intersection over Union for two bounding boxes.
    /// </summary>
    private static double ComputeIoU(PreviewOcrRegion a, PreviewOcrRegion b)
    {
        var x1 = Math.Max(a.X, b.X);
        var y1 = Math.Max(a.Y, b.Y);
        var x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        if (x2 <= x1 || y2 <= y1)
            return 0.0;

        var intersection = (x2 - x1) * (y2 - y1);
        var areaA = a.Width * a.Height;
        var areaB = b.Width * b.Height;
        var union = areaA + areaB - intersection;

        return union > 0 ? intersection / union : 0.0;
    }
}
