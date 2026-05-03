using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Core.Export;

/// <summary>
/// Dedicated service for creating PDFs from image files (e.g. document scanning).
/// Isolated from SearchExportService for clarity.
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

        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            foreach (var imagePath in paths)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0);
                    var imageBytes = File.ReadAllBytes(imagePath);
                    // Preserve source pixels: Best disables lossy JPEG re-encoding,
                    // and a high raster DPI prevents QuestPDF from down-sampling
                    // high-resolution captures to the page render DPI (default 72).
                    page.Content()
                        .Image(imageBytes)
                        .WithCompressionQuality(ImageCompressionQuality.Best)
                        .WithRasterDpi(1200)
                        .FitArea();
                });
            }
        });

        document.GeneratePdf(outputPath);
        _log.Information("Created PDF from {Count} images: {Path}", paths.Count, outputPath);
        return outputPath;
    }
}
