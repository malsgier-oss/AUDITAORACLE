using System.IO;
using PDFtoImage;

namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// Workspace preview text-on-image layout using Tesseract line boxes (same engine and settings as background OCR).
/// </summary>
public sealed class TesseractPreviewOcrLayoutService : IWindowsPreviewOcrLayout
{
    private readonly TesseractOcrService _tesseract;

    public TesseractPreviewOcrLayoutService(TesseractOcrService tesseract)
    {
        _tesseract = tesseract;
    }

    public Task<IReadOnlyList<PreviewOcrRegion>> ExtractLineRegionsAsync(string imagePath, CancellationToken ct = default) =>
        _tesseract.ExtractPreviewLineRegionsAsync(imagePath, ct);

    public async Task<IReadOnlyList<PreviewOcrRegion>> ExtractPdfPageRegionsAsync(string pdfPath, int pageIndex0, float renderDpi, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            return Array.Empty<PreviewOcrRegion>();

        var dpi = renderDpi <= 0 ? 300 : (int)Math.Clamp(renderDpi, 72, 600);
        string? tempPath = null;
        try
        {
            tempPath = Path.Combine(Path.GetTempPath(), $"WorkAudit_pdf_regions_{Guid.NewGuid():N}.png");
            using (var stream = File.OpenRead(pdfPath))
            {
                Conversion.SavePng(tempPath, stream, options: new RenderOptions { Dpi = dpi }, page: pageIndex0);
            }

            return await _tesseract.ExtractPreviewLineRegionsAsync(tempPath, ct).ConfigureAwait(false);
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempPath))
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                    /* ignore */
                }
            }
        }
    }

    public Task<OcrSelectableTextLayout?> ExtractSelectableTextLayoutAsync(string imagePath, CancellationToken ct = default) =>
        _tesseract.ExtractSelectableTextLayoutAsync(imagePath, ct);
}
