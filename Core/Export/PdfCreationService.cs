using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Core.Export;

/// <summary>
/// Dedicated service for creating PDFs from image files (e.g. document scanning).
/// Uses PdfSharp directly so PNG inputs are embedded losslessly via Flate
/// (no JPEG re-encode, no raster DPI down-sampling).
/// </summary>
public static class PdfCreationService
{
    private static readonly ILogger _log = LoggingService.ForContext(typeof(PdfCreationService));

    public static string CreateFromImages(IEnumerable<string> imagePaths, string outputPath)
    {
        var paths = imagePaths.Where(p => !string.IsNullOrEmpty(p) && File.Exists(p)).ToList();
        if (paths.Count == 0)
            throw new InvalidOperationException("No valid image files to create PDF.");

        const long MaxFileSize = 50 * 1024 * 1024;
        foreach (var path in paths)
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > MaxFileSize)
            {
                _log.Warning("Image file exceeds 50MB limit: {Path} ({Size} MB)", path, fileInfo.Length / (1024 * 1024));
            }
        }

        using var doc = new PdfDocument();
        foreach (var imagePath in paths)
        {
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);
            using var img = XImage.FromFile(imagePath);
            var pageW = page.Width.Point;
            var pageH = page.Height.Point;
            var iw = img.PointWidth;
            var ih = img.PointHeight;
            if (iw <= 0 || ih <= 0) continue;
            var scale = Math.Min(pageW / iw, pageH / ih);
            var w = iw * scale;
            var h = ih * scale;
            var x = (pageW - w) / 2;
            var y = (pageH - h) / 2;
            gfx.DrawImage(img, x, y, w, h);
        }

        doc.Save(outputPath);
        _log.Information("Created PDF from {Count} images: {Path}", paths.Count, outputPath);
        return outputPath;
    }
}
