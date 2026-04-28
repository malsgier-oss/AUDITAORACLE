namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// Extracts text layout from an image for workspace preview selection (implementation: Tesseract line boxes).
/// </summary>
public interface IWindowsPreviewOcrLayout
{
    /// <summary>
    /// Returns one region per recognized text line from an image file, in bitmap pixel space (legacy overlay path).
    /// </summary>
    Task<IReadOnlyList<PreviewOcrRegion>> ExtractLineRegionsAsync(string imagePath, CancellationToken ct = default);

    /// <summary>
    /// PDF preview layout (optional); current implementation may return empty.
    /// </summary>
    Task<IReadOnlyList<PreviewOcrRegion>> ExtractPdfPageRegionsAsync(string pdfPath, int pageIndex0, float renderDpi, CancellationToken ct = default);

    /// <summary>
    /// Word-level selectable layout in image pixel space. Non-breaking addition alongside <see cref="ExtractLineRegionsAsync"/>.
    /// </summary>
    Task<OcrSelectableTextLayout?> ExtractSelectableTextLayoutAsync(string imagePath, CancellationToken ct = default);
}
