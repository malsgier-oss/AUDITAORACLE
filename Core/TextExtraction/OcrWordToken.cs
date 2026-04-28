namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// A single OCR word with bounds in source image pixel space (same convention as <see cref="PreviewOcrRegion"/>).
/// </summary>
public readonly record struct OcrWordToken(string Text, double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width * 0.5;
    public double CenterY => Y + Height * 0.5;
}
