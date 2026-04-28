namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// One selectable word span with layout metadata and offsets into <see cref="OcrSelectableTextLayout.FullText"/>.
/// </summary>
public sealed class OcrTextSpan
{
    public OcrTextSpan(
        string text,
        double x,
        double y,
        double width,
        double height,
        int lineIndex,
        int wordIndexInLine,
        int spanIndex,
        int charStart,
        int charLength)
    {
        Text = text ?? "";
        X = x;
        Y = y;
        Width = width;
        Height = height;
        LineIndex = lineIndex;
        WordIndexInLine = wordIndexInLine;
        SpanIndex = spanIndex;
        CharStart = charStart;
        CharLength = charLength;
    }

    public string Text { get; }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public int LineIndex { get; }
    public int WordIndexInLine { get; }
    public int SpanIndex { get; }
    public int CharStart { get; }
    public int CharLength { get; }

    public double CenterX => X + Width * 0.5;
    public double CenterY => Y + Height * 0.5;
}
