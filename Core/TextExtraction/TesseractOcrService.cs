using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Tesseract;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;
using System.Threading.Channels;

namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// OCR implementation using Tesseract 5 (LSTM), with OpenCV preprocessing.
/// Language and paths are driven by <see cref="IConfigStore"/> and <see cref="AppConfiguration"/>.
/// </summary>
public class TesseractOcrService : IOcrService, IDisposable
{
    private sealed class OcrPassResult
    {
        public string Name { get; init; } = "";
        public string Text { get; init; } = "";
        public int Confidence { get; init; }
    }

    private sealed class OcrDocumentResult
    {
        public string Text { get; init; } = "";
        public OcrStructuredDataResult Structured { get; init; } = new();
    }

    private const string LegacyTessDataFallback = @"C:\WorkAuditModels\Tesseract\tessdata";

    private readonly AppConfiguration _appConfig;
    private readonly IConfigStore _configStore;
    private readonly Channel<Document> _queue = Channel.CreateUnbounded<Document>();
    private readonly ConcurrentDictionary<int, byte> _ocrInFlight = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _ocrLock = new();
    private Task? _queueTask;
    private bool _disposed;

    public event Action<Document>? OcrCompleted;

    public TesseractOcrService(AppConfiguration appConfig, IConfigStore configStore)
    {
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));

        var tessPath = ResolveTessDataPath();
        if (!Directory.Exists(tessPath))
            Log.Warning("Tesseract tessdata folder not found at: {Path}", tessPath);

        _queueTask = Task.Run(ProcessQueueAsync);
        Log.Information(
            "TesseractOcrService initialized. Tessdata: {Path}. PDF DPI default: {Dpi}.",
            tessPath,
            GetPdfRenderDpi());
    }

    /// <summary>Resolves tessdata directory from current app settings (re-read each OCR for Control Panel changes).</summary>
    private string ResolveTessDataPath()
    {
        var configured = _configStore.GetSettingValue("tesseract_tessdata_path", "")?.Trim();
        if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
            return configured;

        var underBase = Path.Combine(_appConfig.BaseDirectory ?? "", "Tesseract", "tessdata");
        if (Directory.Exists(underBase))
            return underBase;

        if (Directory.Exists(LegacyTessDataFallback))
            return LegacyTessDataFallback;

        return underBase;
    }

    private int GetPdfRenderDpi()
    {
        var dpi = _configStore.GetSettingInt("ocr_pdf_render_dpi", 300);
        if (dpi < 150) dpi = 150;
        if (dpi > 400) dpi = 400;
        return dpi;
    }

    /// <summary>Builds Tesseract language string (e.g. ara+eng) from app_settings default_ocr_language.</summary>
    private string GetTesseractLanguageString()
    {
        var raw = _configStore.GetSettingValue("default_ocr_language", "ara+eng")?.Trim();
        if (string.IsNullOrEmpty(raw))
            return "ara+eng";
        return raw.Replace('_', '+');
    }

    private bool UseLayoutAssembly() => _configStore.GetSettingBool("ocr_use_layout_assembly", true);

    private PageSegMode GetPageSegMode()
    {
        var psm = _configStore.GetSettingInt("ocr_tesseract_psm", 3);
        if (psm < 0 || psm >= (int)PageSegMode.Count)
            return PageSegMode.Auto;
        return (PageSegMode)psm;
    }

    private bool UseMultiPassOcr() => _configStore.GetSettingBool("ocr_enable_multipass", true);

    private bool UseBilingualOrganizedOutput() => _configStore.GetSettingBool("ocr_organize_bilingual_output", false);

    private bool UseAccuracyFirstMerge()
    {
        var raw = _configStore.GetSettingValue("ocr_multipass_merge_mode", "accuracy")?.Trim();
        return string.Equals(raw, "accuracy", StringComparison.OrdinalIgnoreCase);
    }

    private int GetStructuredMinConfidence()
    {
        var v = _configStore.GetSettingInt("ocr_structured_min_confidence", 70);
        return Math.Clamp(v, 0, 100);
    }

    private static string NormalizeLineForDedup(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var t = s.Trim();
        if (t.Length == 0) return "";
        var parts = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" ", parts).ToLowerInvariant();
    }

    private static int LevenshteinDistance(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var n = a.Length;
        var m = b.Length;
        Span<int> prev = stackalloc int[m + 1];
        Span<int> curr = stackalloc int[m + 1];
        for (var j = 0; j <= m; j++) prev[j] = j;
        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            var tmp = prev;
            prev = curr;
            curr = tmp;
        }
        return prev[m];
    }

    private static bool LinesAreFuzzyDuplicate(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        var na = NormalizeLineForDedup(a);
        var nb = NormalizeLineForDedup(b);
        if (na.Length == 0 || nb.Length == 0) return false;
        if (na == nb) return true;

        var minLen = Math.Min(na.Length, nb.Length);
        if (minLen >= 4)
        {
            if (na.Contains(nb, StringComparison.Ordinal) || nb.Contains(na, StringComparison.Ordinal))
                return true;
        }

        var maxLen = Math.Max(na.Length, nb.Length);
        if (maxLen > 500) return false;
        var dist = LevenshteinDistance(na.AsSpan(), nb.AsSpan());
        var ratio = 1.0 - (double)dist / maxLen;
        return ratio >= 0.85;
    }

    private static int ScorePassForTieBreak(int confidence, string name, bool useHeavyBoostOnTie)
    {
        var s = confidence;
        if (useHeavyBoostOnTie && name.Contains("heavy", StringComparison.OrdinalIgnoreCase))
            s += 6;
        return s;
    }

    private static bool ShouldReplaceDuplicateLine(
        string existingLine, int existingConf, string existingName,
        string newLine, int newConf, string newName,
        bool useHeavyBoostOnTie)
    {
        var nOld = NormalizeLineForDedup(existingLine).Length;
        var nNew = NormalizeLineForDedup(newLine).Length;
        if (nNew > nOld) return true;
        if (nNew < nOld) return false;
        return ScorePassForTieBreak(newConf, newName, useHeavyBoostOnTie) >
               ScorePassForTieBreak(existingConf, existingName, useHeavyBoostOnTie);
    }

    private static int FindFuzzyDuplicateLineIndex(IReadOnlyList<string> lines, string candidate)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (LinesAreFuzzyDuplicate(lines[i], candidate))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// First-appearance union of lines with fuzzy deduplication; on duplicate, prefer longer text, then pass score.
    /// </summary>
    private static List<string> MergeLinesOrderedFirstAppearance(
        IReadOnlyList<(string line, int confidence, string name)> ordered,
        bool useHeavyBoostOnTie)
    {
        var texts = new List<string>();
        var confs = new List<int>();
        var names = new List<string>();

        foreach (var (line, confidence, name) in ordered)
        {
            var t = line.TrimEnd();
            if (string.IsNullOrEmpty(t)) continue;

            var dupIdx = FindFuzzyDuplicateLineIndex(texts, t);
            if (dupIdx < 0)
            {
                texts.Add(t);
                confs.Add(confidence);
                names.Add(name);
                continue;
            }

            if (ShouldReplaceDuplicateLine(
                    texts[dupIdx], confs[dupIdx], names[dupIdx],
                    t, confidence, name,
                    useHeavyBoostOnTie))
            {
                texts[dupIdx] = t;
                confs[dupIdx] = confidence;
                names[dupIdx] = name;
            }
        }

        return texts;
    }

    private static List<string> SplitToNonEmptyTrimmedLines(string? text)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(text)) return list;
        var s = text.Replace("\r\n", "\n").Replace("\r", "\n");
        foreach (var line in s.Split('\n', StringSplitOptions.None))
        {
            var t = line.TrimEnd();
            if (!string.IsNullOrEmpty(t))
                list.Add(t);
        }
        return list;
    }

    private static bool IsArabicLetter(char c) =>
        c is >= '\u0600' and <= '\u06FF'
        or >= '\u0750' and <= '\u077F'
        or >= '\u08A0' and <= '\u08FF'
        or >= '\uFB50' and <= '\uFDFF'
        or >= '\uFE70' and <= '\uFEFF';

    /// <summary>True when more than 30% of letter characters are Arabic script (for RTL line ordering).</summary>
    private static bool IsArabicText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var letters = 0;
        var arabic = 0;
        foreach (var c in text)
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (IsArabicLetter(c)) arabic++;
        }

        if (letters == 0) return false;
        return arabic > letters * 0.3;
    }

    private static bool IsWordLayoutReasonable(string? wordLayout, string plainGetText)
    {
        if (string.IsNullOrEmpty(wordLayout)) return false;
        var trimmed = wordLayout.Trim();
        if (trimmed.Length == 0) return false;

        var hasAlphanumeric = false;
        foreach (var c in trimmed)
        {
            if (char.IsLetterOrDigit(c))
            {
                hasAlphanumeric = true;
                break;
            }
        }

        if (!hasAlphanumeric) return false;

        var plainNonWs = 0;
        foreach (var c in plainGetText)
        {
            if (!char.IsWhiteSpace(c)) plainNonWs++;
        }

        if (plainNonWs > 40)
        {
            var layoutNonWs = 0;
            foreach (var c in wordLayout)
            {
                if (!char.IsWhiteSpace(c)) layoutNonWs++;
            }

            if (layoutNonWs < plainNonWs * 0.25) return false;
        }

        return true;
    }

    private readonly struct LayoutWordEntry
    {
        public LayoutWordEntry(string text, int x1, int y1, int x2, int y2, int confidence)
        {
            Text = text;
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            Confidence = confidence;
        }

        public string Text { get; }
        public int X1 { get; }
        public int Y1 { get; }
        public int X2 { get; }
        public int Y2 { get; }
        /// <summary>0–100; words below 20 are dropped (noise).</summary>
        public int Confidence { get; }
        public double CenterX => (X1 + X2) * 0.5;
        public double CenterY => (Y1 + Y2) * 0.5;
        public double Height => Math.Max(1, Y2 - Y1);
    }

    private sealed class LayoutLineBucket
    {
        private readonly List<LayoutWordEntry> _words = new();

        public IReadOnlyList<LayoutWordEntry> Words => _words;
        public double AverageCenterY => _words.Count == 0 ? 0 : _words.Average(w => w.CenterY);

        public void Add(LayoutWordEntry w) => _words.Add(w);
    }

    /// <summary>
    /// Reconstructs page text from word bounding boxes: cluster by Y, sort each line by X (RTL for Arabic-primary lines).
    /// </summary>
    private static string BuildTextFromWordLayout(Page page)
    {
        var words = new List<LayoutWordEntry>();
        using var iter = page.GetIterator();
        iter.Begin();
        do
        {
            do
            {
                do
                {
                    do
                    {
                        var wordText = iter.GetText(PageIteratorLevel.Word);
                        if (string.IsNullOrEmpty(wordText)) continue;
                        if (!iter.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds)) continue;
                        var conf = (int)Math.Round(iter.GetConfidence(PageIteratorLevel.Word));
                        if (conf < 20) continue;
                        words.Add(new LayoutWordEntry(wordText, bounds.X1, bounds.Y1, bounds.X2, bounds.Y2, conf));
                    } while (iter.Next(PageIteratorLevel.Word));
                } while (iter.Next(PageIteratorLevel.TextLine));
            } while (iter.Next(PageIteratorLevel.Para));
        } while (iter.Next(PageIteratorLevel.Block));

        if (words.Count == 0) return "";

        var heights = words.Select(w => w.Height).OrderBy(h => h).ToList();
        var mid = heights.Count / 2;
        var medianHeight = heights.Count % 2 == 0
            ? (heights[mid - 1] + heights[mid]) * 0.5
            : heights[mid];
        var threshold = Math.Clamp(
            medianHeight * OcrSelectableTextLayoutBuilder.LineThresholdHeightFactor,
            OcrSelectableTextLayoutBuilder.LineThresholdMinPx,
            OcrSelectableTextLayoutBuilder.LineThresholdMaxPx);

        var sorted = words.OrderBy(w => w.CenterY).ThenBy(w => w.X1).ToList();
        var lines = new List<LayoutLineBucket>();
        foreach (var w in sorted)
        {
            LayoutLineBucket? best = null;
            var bestDy = double.MaxValue;
            foreach (var line in lines)
            {
                var dy = Math.Abs(w.CenterY - line.AverageCenterY);
                if (dy < threshold && dy < bestDy)
                {
                    bestDy = dy;
                    best = line;
                }
            }

            if (best == null)
            {
                var nb = new LayoutLineBucket();
                nb.Add(w);
                lines.Add(nb);
            }
            else
            {
                best.Add(w);
            }
        }

        lines.Sort((a, b) => a.AverageCenterY.CompareTo(b.AverageCenterY));

        var lineStrings = new List<string>(lines.Count);
        foreach (var bucket in lines)
        {
            var bucketWords = bucket.Words;
            if (bucketWords.Count == 0) continue;

            var lineForDir = string.Concat(bucketWords.Select(x => x.Text));
            var rtl = IsArabicText(lineForDir);
            var ordered = rtl
                ? bucketWords.OrderByDescending(w => w.CenterX).ToList()
                : bucketWords.OrderBy(w => w.CenterX).ToList();
            lineStrings.Add(string.Join(" ", ordered.Select(x => x.Text)));
        }

        return string.Join("\n", lineStrings);
    }

    /// <summary>Line-bounded word assembly (same hierarchy as preview word tokens).</summary>
    private static string ExtractTextFromWordIterator(Page page)
    {
        var lines = new List<string>();
        using var iter = page.GetIterator();
        iter.Begin();
        do
        {
            do
            {
                do
                {
                    var words = new List<string>();
                    do
                    {
                        var wordText = iter.GetText(PageIteratorLevel.Word);
                        if (!string.IsNullOrWhiteSpace(wordText))
                            words.Add(wordText.TrimEnd());
                    } while (iter.Next(PageIteratorLevel.Word));

                    if (words.Count > 0)
                        lines.Add(string.Join(" ", words));
                } while (iter.Next(PageIteratorLevel.TextLine));
            } while (iter.Next(PageIteratorLevel.Para));
        } while (iter.Next(PageIteratorLevel.Block));

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Prefers word-bounding-box reconstruction; otherwise walks layout for line text, unions with <see cref="Page.GetText"/>, and optionally word-level assembly when output looks incomplete.
    /// </summary>
    private static string ExtractTextUsingLayoutIterator(Page page)
    {
        var plain = page.GetText() ?? "";
        var plainTrimmed = plain.Trim();

        var wordLayout = BuildTextFromWordLayout(page);
        if (IsWordLayoutReasonable(wordLayout, plain))
            return wordLayout.TrimEnd();

        var iteratorLines = new List<string>();
        using var iter = page.GetIterator();
        iter.Begin();
        do
        {
            do
            {
                do
                {
                    var lineText = iter.GetText(PageIteratorLevel.TextLine);
                    if (!string.IsNullOrWhiteSpace(lineText))
                        iteratorLines.Add(lineText.TrimEnd());
                } while (iter.Next(PageIteratorLevel.TextLine));
            } while (iter.Next(PageIteratorLevel.Para));
        } while (iter.Next(PageIteratorLevel.Block));

        var plainLines = SplitToNonEmptyTrimmedLines(plain);

        var candidates = new List<(string line, int confidence, string name)>();
        foreach (var l in iteratorLines)
            candidates.Add((l, 0, ""));
        foreach (var l in plainLines)
            candidates.Add((l, 0, ""));

        var joinedIter = string.Join("\n", iteratorLines);
        var suspicious = iteratorLines.Count == 0
                         || (plainTrimmed.Length > joinedIter.Length * 1.25d
                             && plainTrimmed.Length - joinedIter.Length > 20);

        if (suspicious)
        {
            var wordBlock = ExtractTextFromWordIterator(page);
            foreach (var l in SplitToNonEmptyTrimmedLines(wordBlock))
                candidates.Add((l, 0, "word"));
        }

        var merged = MergeLinesOrderedFirstAppearance(candidates, useHeavyBoostOnTie: false);
        if (merged.Count == 0)
            return plainTrimmed;
        return string.Join("\n", merged);
    }

    public void EnqueueOcrTask(Document document)
    {
        if (document == null || string.IsNullOrEmpty(document.FilePath)) return;
        if (!_ocrInFlight.TryAdd(document.Id, 0))
        {
            Log.Debug("Skip duplicate OCR enqueue for document {Id}", document.Id);
            return;
        }
        if (!_queue.Writer.TryWrite(document))
        {
            _ocrInFlight.TryRemove(document.Id, out _);
            return;
        }
        Log.Information("Enqueued Tesseract OCR task for document {Id}", document.Id);
    }

    private static readonly int[] DocReloadRetryDelaysMs = { 50, 100, 200, 400, 800, 1600 };

    private static Document? TryLoadLatestDocument(IDocumentStore? store, int id, out string? reason, int maxAttempts = 6)
    {
        reason = null;
        if (store == null)
        {
            reason = "Document store is unavailable.";
            return null;
        }

        var attempts = Math.Max(1, maxAttempts);
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                var result = store.GetResult(id);
                if (result.IsSuccess && result.Value != null)
                    return result.Value;

                reason = result.Error ?? "GetResult returned no document.";
            }
            catch (Exception ex)
            {
                reason = ex.Message;
            }

            if (i < attempts - 1)
                Thread.Sleep(DocReloadRetryDelaysMs[Math.Min(i, DocReloadRetryDelaysMs.Length - 1)]);
        }

        return null;
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var doc in _queue.Reader.ReadAllAsync(_cts.Token))
        {
            try
            {
                Log.Information("Starting background Tesseract OCR for document {Id}", doc.Id);

                var store = ServiceContainer.GetService<IDocumentStore>();
                var latestDoc = TryLoadLatestDocument(store, doc.Id, out var reloadReason);
                if (latestDoc == null)
                {
                    Log.Warning("Skipping OCR writeback for document {Id}: unable to reload latest DB row. Reason: {Reason}", doc.Id, reloadReason ?? "unknown");
                    continue;
                }
                var targetPath = latestDoc.FilePath!;
                var sw = Stopwatch.StartNew();
                var result = await ExtractDocumentResultAsync(targetPath, _cts.Token);
                sw.Stop();

                var lang = GetTesseractLanguageString();
                latestDoc.OcrLanguage = lang;
                latestDoc.OcrDurationMs = (int)Math.Min(sw.ElapsedMilliseconds, int.MaxValue);

                if (!string.IsNullOrWhiteSpace(result.Text))
                {
                    latestDoc.OcrText = result.Text;
                    ApplyStructuredFields(latestDoc, result.Structured);
                    if (store != null && store.UpdateOcrFields(latestDoc))
                    {
                        Log.Information("Tesseract OCR completed for document {Id} in {Ms}ms", latestDoc.Id, latestDoc.OcrDurationMs);
                        OcrCompleted?.Invoke(latestDoc);
                    }
                    else
                    {
                        Log.Warning("OCR writeback failed for document {Id}: UpdateOcrFields returned false", latestDoc.Id);
                    }
                }
                else
                {
                    Log.Warning("Tesseract OCR returned empty text for document {Id}", latestDoc.Id);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Tesseract OCR pipeline failed for document {Id}", doc.Id);
            }
            finally
            {
                _ocrInFlight.TryRemove(doc.Id, out _);
            }
        }
    }

    public Task<string> ExtractTextAsync(string imagePath, CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            var result = await ExtractDocumentResultAsync(imagePath, ct);
            return result.Text;
        }, ct);
    }

    /// <summary>
    /// Line-level regions in <b>source image pixel space</b> for workspace preview text selection.
    /// Uses the same tessdata, language, and PSM as background OCR; runs on the original file (no resize preprocess) so boxes align with the viewer.
    /// </summary>
    public Task<IReadOnlyList<PreviewOcrRegion>> ExtractPreviewLineRegionsAsync(string imagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return Task.FromResult<IReadOnlyList<PreviewOcrRegion>>(Array.Empty<PreviewOcrRegion>());

        return Task.Run(() =>
        {
            var tessDataPath = ResolveTessDataPath();
            if (!Directory.Exists(tessDataPath))
                return (IReadOnlyList<PreviewOcrRegion>)Array.Empty<PreviewOcrRegion>();

            var lang = GetTesseractLanguageString();

            lock (_ocrLock)
            {
                try
                {
                    tessDataPath = ResolveTessDataPath();
                    if (!Directory.Exists(tessDataPath))
                        return Array.Empty<PreviewOcrRegion>();

                    using var engine = new TesseractEngine(tessDataPath, lang, EngineMode.LstmOnly);
                    engine.SetVariable("preserve_interword_spaces", "1");
                    using var img = Pix.LoadFromFile(imagePath);
                    using var page = engine.Process(img, GetPageSegMode());
                    return ExtractPreviewRegionsFromPage(page);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Tesseract preview layout failed for {Path}", imagePath);
                    return Array.Empty<PreviewOcrRegion>();
                }
            }
        }, ct);
    }

    /// <summary>
    /// Word-level layout in source image pixel space for span-based preview selection.
    /// </summary>
    public Task<OcrSelectableTextLayout?> ExtractSelectableTextLayoutAsync(string imagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return Task.FromResult<OcrSelectableTextLayout?>(null);

        return Task.Run(() =>
        {
            var tessDataPath = ResolveTessDataPath();
            if (!Directory.Exists(tessDataPath))
                return (OcrSelectableTextLayout?)null;

            var lang = GetTesseractLanguageString();

            lock (_ocrLock)
            {
                try
                {
                    tessDataPath = ResolveTessDataPath();
                    if (!Directory.Exists(tessDataPath))
                        return null;

                    using var engine = new TesseractEngine(tessDataPath, lang, EngineMode.LstmOnly);
                    engine.SetVariable("preserve_interword_spaces", "1");
                    using var img = Pix.LoadFromFile(imagePath);
                    using var page = engine.Process(img, GetPageSegMode());
                    var words = ExtractPreviewWordTokensFromPage(page);
                    var layout = OcrSelectableTextLayoutBuilder.BuildFromWordTokens(words);
                    return layout.Spans.Count == 0 ? null : layout;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Tesseract selectable layout failed for {Path}", imagePath);
                    return null;
                }
            }
        }, ct);
    }

    private static IReadOnlyList<OcrWordToken> ExtractPreviewWordTokensFromPage(Page page)
    {
        var list = new List<OcrWordToken>();
        using var iter = page.GetIterator();
        iter.Begin();
        do
        {
            do
            {
                do
                {
                    do
                    {
                        var wordText = iter.GetText(PageIteratorLevel.Word);
                        if (string.IsNullOrEmpty(wordText))
                            continue;

                        // Preview selection requires pixel bounds; skip words Tesseract does not box.
                        if (!iter.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds))
                            continue;

                        var w = Math.Max(1, bounds.X2 - bounds.X1);
                        var h = Math.Max(1, bounds.Y2 - bounds.Y1);
                        list.Add(new OcrWordToken(wordText, bounds.X1, bounds.Y1, w, h));
                    } while (iter.Next(PageIteratorLevel.Word));
                } while (iter.Next(PageIteratorLevel.TextLine));
            } while (iter.Next(PageIteratorLevel.Para));
        } while (iter.Next(PageIteratorLevel.Block));

        return list;
    }

    private static IReadOnlyList<PreviewOcrRegion> ExtractPreviewRegionsFromPage(Page page)
    {
        var list = new List<PreviewOcrRegion>();
        using var iter = page.GetIterator();
        iter.Begin();
        do
        {
            do
            {
                do
                {
                    var lineText = iter.GetText(PageIteratorLevel.TextLine);
                    if (string.IsNullOrWhiteSpace(lineText))
                        continue;

                    if (!iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out var bounds))
                        continue;

                    var w = Math.Max(1, bounds.X2 - bounds.X1);
                    var h = Math.Max(1, bounds.Y2 - bounds.Y1);
                    list.Add(new PreviewOcrRegion(bounds.X1, bounds.Y1, w, h, lineText.TrimEnd()));
                } while (iter.Next(PageIteratorLevel.TextLine));
            } while (iter.Next(PageIteratorLevel.Para));
        } while (iter.Next(PageIteratorLevel.Block));

        return list;
    }

    private Task<OcrDocumentResult> ExtractDocumentResultAsync(string imagePath, CancellationToken ct = default)
    {
        if (!File.Exists(imagePath)) return Task.FromResult(new OcrDocumentResult { Text = string.Empty });
        var tessDataPath = ResolveTessDataPath();
        if (!Directory.Exists(tessDataPath))
            return Task.FromResult(new OcrDocumentResult { Text = "Error: Tesseract data missing." });

        if (imagePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return ExtractPdfDocumentResultAsync(imagePath, ct);

        return Task.Run(() =>
        {
            lock (_ocrLock)
            {
                try
                {
                    tessDataPath = ResolveTessDataPath();
                    if (!Directory.Exists(tessDataPath))
                        return new OcrDocumentResult { Text = "Error: Tesseract data missing." };

                    var normalized = RunOcrPipelineToNormalizedText(imagePath);
                    Log.Information("Tesseract OCR extraction successful for {Path}", imagePath);
                    return FinalizeOcrDocumentResult(normalized);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Tesseract extraction failed for {Path}", imagePath);
                    return new OcrDocumentResult { Text = $"OCR Error: {ex.Message}" };
                }
            }
        }, ct);
    }

    private Task<OcrDocumentResult> ExtractPdfDocumentResultAsync(string pdfPath, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            lock (_ocrLock)
            {
                try
                {
                    var tessDataPath = ResolveTessDataPath();
                    if (!Directory.Exists(tessDataPath))
                        return new OcrDocumentResult { Text = "Error: Tesseract data missing." };

                    var dpi = GetPdfRenderDpi();
                    var pageCount = OcrPdfPageHelper.TryGetPageCount(pdfPath);
                    var tempPaths = new List<string>();
                    try
                    {
                        var parts = new List<string>(pageCount);
                        for (var p = 0; p < pageCount; p++)
                        {
                            var png = OcrPdfPageHelper.RenderPageToTempPng(pdfPath, p, dpi);
                            tempPaths.Add(png);
                            var pageText = RunOcrPipelineToNormalizedText(png);
                            parts.Add($"[Page {p + 1}]\n{pageText}");
                        }

                        var combined = string.Join("\n\n", parts);
                        Log.Information("Tesseract OCR extraction successful for PDF {Path} ({Pages} pages)", pdfPath, pageCount);
                        return FinalizeOcrDocumentResult(combined);
                    }
                    finally
                    {
                        foreach (var tp in tempPaths)
                        {
                            try
                            {
                                if (File.Exists(tp)) File.Delete(tp);
                            }
                            catch
                            {
                                /* ignore */
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Tesseract PDF extraction failed for {Path}", pdfPath);
                    return new OcrDocumentResult { Text = $"OCR Error: {ex.Message}" };
                }
            }
        }, ct);
    }

    private OcrDocumentResult FinalizeOcrDocumentResult(string normalized)
    {
        var withArabic = OcrArabicNormalizer.Normalize(normalized);
        var organized = UseBilingualOrganizedOutput() ? OcrBilingualOrganizer.Organize(withArabic) : withArabic;
        var structured = OcrStructuredDataExtractor.Extract(withArabic);
        return new OcrDocumentResult { Text = organized, Structured = structured };
    }

    private string RunOcrPipelineToNormalizedText(string imagePath)
    {
        var tessDataPath = ResolveTessDataPath();
        if (!Directory.Exists(tessDataPath))
            throw new InvalidOperationException("Tesseract data missing.");

        var lang = GetTesseractLanguageString();
        var variants = BuildPreprocessVariants(imagePath);
        var fallbackVariants = new List<(string name, string path, PageSegMode psm, bool isTempPath)>();
        try
        {
            var passes = RunOcrPasses(variants, tessDataPath, lang);
            var merged = MergePasses(passes);
            var normalized = OcrTextNormalizer.Normalize(merged);
            if (ShouldFallbackRetry(normalized, passes, lang))
            {
                fallbackVariants = BuildFallbackVariants(imagePath);
                if (fallbackVariants.Count > 0)
                {
                    var passes2 = RunOcrPasses(fallbackVariants, tessDataPath, lang);
                    var merged2 = MergePasses(passes2);
                    var normalized2 = OcrTextNormalizer.Normalize(merged2);
                    normalized = MergeTextUnion(normalized, normalized2);
                }
            }

            return normalized;
        }
        finally
        {
            foreach (var p in variants.Concat(fallbackVariants)
                         .Where(v => v.isTempPath && File.Exists(v.path))
                         .Select(v => v.path)
                         .Distinct())
            {
                try
                {
                    File.Delete(p);
                }
                catch
                {
                    /* ignore */
                }
            }
        }
    }

    private List<(string name, string path, PageSegMode psm, bool isTempPath)> BuildFallbackVariants(string imagePath)
    {
        var list = new List<(string name, string path, PageSegMode psm, bool isTempPath)>();
        try
        {
            var heavy = OcrImagePreprocessor.HeavyPreprocess(imagePath, _configStore);
            list.Add(("fallback_heavy_auto", heavy, PageSegMode.Auto, heavy != imagePath));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallback heavy preprocessing failed for {Path}", imagePath);
        }

        try
        {
            var light = OcrImagePreprocessor.LightPreprocess(imagePath);
            list.Add(("fallback_light_sparse", light, PageSegMode.SparseText, light != imagePath));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallback light preprocessing failed for {Path}", imagePath);
        }

        return list
            .GroupBy(x => $"{x.name}|{x.path}|{(int)x.psm}")
            .Select(g => g.First())
            .ToList();
    }

    private List<(string name, string path, PageSegMode psm, bool isTempPath)> BuildPreprocessVariants(string imagePath)
    {
        var variants = new List<(string name, string path, PageSegMode psm, bool isTempPath)>
        {
            ("original_auto", imagePath, GetPageSegMode(), false)
        };

        try
        {
            var heavy = OcrImagePreprocessor.HeavyPreprocess(imagePath, _configStore);
            variants.Add(("heavy_default_psm", heavy, GetPageSegMode(), heavy != imagePath));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Heavy preprocessing failed for {Path}", imagePath);
        }

        if (UseMultiPassOcr())
        {
            try
            {
                var light = OcrImagePreprocessor.LightPreprocess(imagePath);
                variants.Add(("light_auto", light, PageSegMode.Auto, light != imagePath));
                variants.Add(("light_sparse", light, PageSegMode.SparseText, light != imagePath));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Light preprocessing failed for {Path}", imagePath);
            }
        }

        return variants
            .GroupBy(x => $"{x.name}|{x.path}|{(int)x.psm}")
            .Select(g => g.First())
            .ToList();
    }

    private List<OcrPassResult> RunOcrPasses(List<(string name, string path, PageSegMode psm, bool isTempPath)> variants, string tessDataPath, string lang)
    {
        var results = new List<OcrPassResult>();
        foreach (var variant in variants)
        {
            using var engine = new TesseractEngine(tessDataPath, lang, EngineMode.LstmOnly);
            engine.SetVariable("preserve_interword_spaces", "1");
            using var img = Pix.LoadFromFile(variant.path);
            using var page = engine.Process(img, variant.psm);

            var raw = UseLayoutAssembly() ? ExtractTextUsingLayoutIterator(page) : (page.GetText() ?? "");
            var text = OcrTextNormalizer.Normalize(raw);
            results.Add(new OcrPassResult
            {
                Name = variant.name,
                Text = text,
                Confidence = Math.Clamp((int)Math.Round(page.GetMeanConfidence() * 100f), 0, 100)
            });
        }

        return results;
    }

    private string MergePasses(List<OcrPassResult> passes)
    {
        if (passes.Count == 0) return string.Empty;
        if (!UseMultiPassOcr() || passes.Count == 1)
            return OcrNumberProtector.Repair(passes[0].Text);

        var ordered = new List<(string line, int confidence, string name)>();
        foreach (var p in passes)
        {
            foreach (var rawLine in p.Text.Split('\n', StringSplitOptions.None))
            {
                var t = OcrNumberProtector.Repair(rawLine.TrimEnd());
                if (string.IsNullOrEmpty(t)) continue;
                ordered.Add((t, p.Confidence, p.Name));
            }
        }

        var merged = MergeLinesOrderedFirstAppearance(ordered, UseAccuracyFirstMerge());
        if (merged.Count == 0)
            return OcrNumberProtector.Repair(passes.OrderByDescending(p => p.Confidence).First().Text);

        return OcrNumberProtector.Repair(string.Join("\n", merged));
    }

    private static string MergeTextUnion(string primary, string secondary)
    {
        if (string.IsNullOrWhiteSpace(secondary)) return primary;
        var lines = SplitToNonEmptyTrimmedLines(primary);
        var acc = new List<string>(lines);
        foreach (var line in SplitToNonEmptyTrimmedLines(secondary))
        {
            if (FindFuzzyDuplicateLineIndex(acc, line) >= 0) continue;
            acc.Add(line);
        }

        return string.Join("\n", acc);
    }

    private bool ShouldFallbackRetry(string normalizedText, IReadOnlyList<OcrPassResult> passes, string lang)
    {
        var t = normalizedText.Trim();
        var wc = string.IsNullOrEmpty(t) ? 0 : Regex.Split(t, @"\s+").Length;
        if (wc < 10) return true;
        var avg = passes.Count == 0 ? 0 : (int)Math.Round(passes.Average(p => (double)p.Confidence));
        if (avg < 40) return true;
        if (lang.Contains("ara", StringComparison.OrdinalIgnoreCase))
        {
            var hasArabic = false;
            foreach (var c in normalizedText)
            {
                if (c is >= '\u0600' and <= '\u06FF')
                {
                    hasArabic = true;
                    break;
                }
            }

            if (!hasArabic) return true;
        }

        return false;
    }

    private void ApplyStructuredFields(Document doc, OcrStructuredDataResult data)
    {
        var threshold = GetStructuredMinConfidence();
        if (data.AccountNameConfidence >= threshold)
            doc.AccountName = data.AccountName;
        if (data.AccountNumberConfidence >= threshold)
            doc.AccountNumber = data.AccountNumber;
        if (data.TransactionReferenceConfidence >= threshold)
            doc.TransactionReference = data.TransactionReference;
        // Document date (extracted_date) is set only by the user or import — never from OCR.
        if (data.AmountsConfidence >= threshold)
            doc.Amounts = data.Amounts;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_queueTask != null && !_queueTask.IsCompleted)
                _queueTask.Wait(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Tesseract OCR queue wait during dispose");
        }

        try
        {
            _cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down
        }

        GC.SuppressFinalize(this);
    }
}
