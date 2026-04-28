namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// Immutable OCR text layout for span-index selection and copy. <see cref="FullText"/> is contiguous with no gaps; each span maps to a substring.
/// </summary>
public sealed class OcrSelectableTextLayout
{
    public OcrSelectableTextLayout(IReadOnlyList<OcrTextSpan> spans, string fullText)
    {
        Spans = spans ?? throw new ArgumentNullException(nameof(spans));
        FullText = fullText ?? "";
    }

    public IReadOnlyList<OcrTextSpan> Spans { get; }
    public string FullText { get; }

    public static OcrSelectableTextLayout Empty { get; } = new(Array.Empty<OcrTextSpan>(), "");

    /// <summary>Inclusive span indices.</summary>
    public string GetTextForSpanRange(int startSpanIndex, int endSpanIndex)
    {
        if (Spans.Count == 0) return "";
        var lo = Math.Clamp(Math.Min(startSpanIndex, endSpanIndex), 0, Spans.Count - 1);
        var hi = Math.Clamp(Math.Max(startSpanIndex, endSpanIndex), 0, Spans.Count - 1);
        var start = Spans[lo].CharStart;
        var end = Spans[hi].CharStart + Spans[hi].CharLength;
        if (start < 0 || end > FullText.Length || start > end) return "";
        return FullText[start..end];
    }

    public int? TryGetSpanIndexContainingChar(int charIndex)
    {
        if (charIndex < 0 || charIndex >= FullText.Length) return null;
        for (var i = 0; i < Spans.Count; i++)
        {
            var s = Spans[i];
            if (charIndex >= s.CharStart && charIndex < s.CharStart + s.CharLength)
                return i;
        }

        return null;
    }
}
