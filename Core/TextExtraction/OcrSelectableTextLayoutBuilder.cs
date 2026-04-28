using System.Text;

namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// Builds <see cref="OcrSelectableTextLayout"/> from word tokens with adaptive line grouping and reading order.
/// </summary>
public static class OcrSelectableTextLayoutBuilder
{
    /// <summary>Multiplier for median word height → vertical line-merge threshold.</summary>
    public const double LineThresholdHeightFactor = 0.6;

    public const double LineThresholdMinPx = 8;
    public const double LineThresholdMaxPx = 25;

    /// <summary>
    /// Flat word list (e.g. Tesseract): cluster by center-Y, then sort each line by X (RTL for Arabic-primary lines).
    /// </summary>
    public static OcrSelectableTextLayout BuildFromWordTokens(IReadOnlyList<OcrWordToken> words)
    {
        if (words == null || words.Count == 0)
            return OcrSelectableTextLayout.Empty;

        var tokens = words.Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.Width > 0 && w.Height > 0).ToList();
        if (tokens.Count == 0)
            return OcrSelectableTextLayout.Empty;

        var threshold = ComputeAdaptiveLineThreshold(tokens);
        var lines = GroupIntoLines(tokens, threshold);
        lines = ExpandLinesByHorizontalGaps(lines, tokens);
        lines.Sort((a, b) => a.AverageCenterY.CompareTo(b.AverageCenterY));
        return BuildFromOrderedLines(lines);
    }

    /// <summary>
    /// Pre-grouped lines (e.g. Windows OCR): preserve engine line boundaries; sort words within each line.
    /// </summary>
    /// <summary>Fallback: one word span per legacy line region (preserves API compatibility).</summary>
    public static OcrSelectableTextLayout? BuildFromPreviewOcrRegions(IReadOnlyList<PreviewOcrRegion>? regions)
    {
        if (regions == null || regions.Count == 0)
            return null;

        var lines = new List<List<OcrWordToken>>();
        foreach (var r in regions)
        {
            var t = r.Text?.TrimEnd() ?? "";
            if (string.IsNullOrWhiteSpace(t) || r.Width < 1 || r.Height < 1)
                continue;
            lines.Add(new List<OcrWordToken> { new(t, r.X, r.Y, r.Width, r.Height) });
        }

        if (lines.Count == 0)
            return null;

        var layout = BuildFromPreGroupedLines(lines);
        return layout.Spans.Count == 0 ? null : layout;
    }

    public static OcrSelectableTextLayout BuildFromPreGroupedLines(IReadOnlyList<IReadOnlyList<OcrWordToken>> lineWordLists)
    {
        if (lineWordLists == null || lineWordLists.Count == 0)
            return OcrSelectableTextLayout.Empty;

        var lines = new List<LineBucket>();
        foreach (var list in lineWordLists)
        {
            if (list == null || list.Count == 0) continue;
            var words = list.Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.Width > 0 && w.Height > 0).ToList();
            if (words.Count == 0) continue;
            lines.Add(new LineBucket(words));
        }

        if (lines.Count == 0)
            return OcrSelectableTextLayout.Empty;

        lines.Sort((a, b) => a.AverageCenterY.CompareTo(b.AverageCenterY));
        return BuildFromOrderedLines(lines);
    }

    private static double ComputeAdaptiveLineThreshold(IReadOnlyList<OcrWordToken> tokens)
    {
        var heights = tokens.Select(t => Math.Max(1.0, t.Height)).OrderBy(h => h).ToList();
        var mid = heights.Count / 2;
        var median = heights.Count % 2 == 0
            ? (heights[mid - 1] + heights[mid]) * 0.5
            : heights[mid];
        var t = LineThresholdHeightFactor * median;
        return Math.Clamp(t, LineThresholdMinPx, LineThresholdMaxPx);
    }

    private static List<LineBucket> GroupIntoLines(List<OcrWordToken> tokens, double threshold)
    {
        var sorted = tokens.OrderBy(t => t.CenterY).ThenBy(t => t.X).ToList();
        var lines = new List<LineBucket>();
        foreach (var w in sorted)
        {
            LineBucket? best = null;
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
                lines.Add(new LineBucket(new List<OcrWordToken> { w }));
            else
                best.Add(w);
        }

        return lines;
    }

    /// <summary>
    /// Splits a line bucket when consecutive words (sorted by X) are too far apart — e.g. two columns on the same Y band.
    /// </summary>
    private static List<LineBucket> ExpandLinesByHorizontalGaps(List<LineBucket> lines, IReadOnlyList<OcrWordToken> allTokens)
    {
        var heights = allTokens.Select(t => Math.Max(1.0, t.Height)).OrderBy(h => h).ToList();
        var mid = heights.Count / 2;
        var median = heights.Count % 2 == 0
            ? (heights[mid - 1] + heights[mid]) * 0.5
            : heights[mid];
        var maxGap = Math.Clamp(median * 6, 16.0, 180.0);

        var result = new List<LineBucket>();
        foreach (var line in lines)
        {
            var words = line.Words.OrderBy(w => w.CenterX).ToList();
            if (words.Count <= 1)
            {
                result.Add(line);
                continue;
            }

            var chunk = new List<OcrWordToken> { words[0] };
            for (var i = 1; i < words.Count; i++)
            {
                var prev = words[i - 1];
                var w = words[i];
                var gap = w.X - (prev.X + prev.Width);
                if (gap > maxGap && chunk.Count > 0)
                {
                    result.Add(new LineBucket(chunk));
                    chunk = new List<OcrWordToken> { w };
                }
                else
                {
                    chunk.Add(w);
                }
            }

            if (chunk.Count > 0)
                result.Add(new LineBucket(chunk));
        }

        return result;
    }

    private static OcrSelectableTextLayout BuildFromOrderedLines(List<LineBucket> lines)
    {
        var orderedWordLines = new List<List<OcrWordToken>>();
        foreach (var bucket in lines)
        {
            var lineText = string.Join("", bucket.Words.Select(w => w.Text));
            var rtl = OcrScriptHeuristics.IsPrimarilyArabicScript(lineText);
            var ordered = rtl
                ? bucket.Words.OrderByDescending(w => w.CenterX).ToList()
                : bucket.Words.OrderBy(w => w.CenterX).ToList();
            orderedWordLines.Add(ordered);
        }

        var spans = new List<OcrTextSpan>();
        var sb = new StringBuilder();
        var spanIndex = 0;
        for (var li = 0; li < orderedWordLines.Count; li++)
        {
            var words = orderedWordLines[li];
            for (var wi = 0; wi < words.Count; wi++)
            {
                var w = words[wi];
                if (wi > 0)
                    sb.Append(' ');
                var charStart = sb.Length;
                sb.Append(w.Text);
                var charLen = sb.Length - charStart;
                spans.Add(new OcrTextSpan(
                    w.Text,
                    w.X,
                    w.Y,
                    w.Width,
                    w.Height,
                    li,
                    wi,
                    spanIndex,
                    charStart,
                    charLen));
                spanIndex++;
            }

            if (li < orderedWordLines.Count - 1)
                sb.Append('\n');
        }

        return new OcrSelectableTextLayout(spans, sb.ToString());
    }

    private sealed class LineBucket
    {
        private readonly List<OcrWordToken> _words;

        public LineBucket(List<OcrWordToken> words) => _words = words;

        public LineBucket(IReadOnlyList<OcrWordToken> words) => _words = words.ToList();

        public IReadOnlyList<OcrWordToken> Words => _words;

        public double AverageCenterY => _words.Count == 0 ? 0 : _words.Average(w => w.CenterY);

        public void Add(OcrWordToken w) => _words.Add(w);
    }
}
