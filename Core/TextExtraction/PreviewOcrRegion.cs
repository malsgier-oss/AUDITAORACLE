namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// A single selectable text region from Windows preview OCR, in source image pixel coordinates.
/// </summary>
public sealed class PreviewOcrRegion
{
    public PreviewOcrRegion(double x, double y, double width, double height, string text)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Text = text ?? "";
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public string Text { get; }
}
