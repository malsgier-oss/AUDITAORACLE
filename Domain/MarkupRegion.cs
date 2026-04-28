namespace WorkAudit.Domain;

/// <summary>Visual markup region on document preview (image or PDF page).</summary>
public class MarkupRegion
{
    public long Id { get; set; }
    public int DocumentId { get; set; }

    /// <summary>Highlight (filled) or Rectangle (outline).</summary>
    public string Kind { get; set; } = MarkupKind.Highlight;

    /// <summary>ARGB hex e.g. #80FFFF00</summary>
    public string Color { get; set; } = "#80FFFF00";

    /// <summary>0–1 relative to preview content width at capture time.</summary>
    public double NormX { get; set; }
    public double NormY { get; set; }
    public double NormW { get; set; }
    public double NormH { get; set; }

    /// <summary>PDF page index (0-based). Ignored for image surface.</summary>
    public int PageIndex { get; set; }

    /// <summary>Image or Pdf</summary>
    public string PreviewSurface { get; set; } = MarkupPreviewSurface.Image;

    public int? NoteId { get; set; }
    public string? Label { get; set; }

    public string CreatedAt { get; set; } = "";
    public string CreatedBy { get; set; } = "";
}

public static class MarkupKind
{
    public const string Highlight = "Highlight";
    public const string Rectangle = "Rectangle";
    /// <summary>Freehand ink stroke; <see cref="MarkupRegion.Label"/> holds JSON array of [nx, ny] points (0–1).</summary>
    public const string Markup = "Markup";
    /// <summary>Text label; <see cref="MarkupRegion.Label"/> holds the text.</summary>
    public const string Text = "Text";

    public static readonly string[] Values = { Highlight, Rectangle, Markup, Text };
}

public static class MarkupPreviewSurface
{
    public const string Image = "Image";
    public const string Pdf = "Pdf";
}
