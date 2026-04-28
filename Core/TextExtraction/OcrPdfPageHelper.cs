using System;
using System.IO;
using PDFtoImage;
using Serilog;
using UglyToad.PdfPig;

namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// Renders PDF pages to PNG for Tesseract using PDFtoImage (same stack as workspace preview).
/// Page count via PdfPig to avoid loading raster data just to count pages.
/// </summary>
public static class OcrPdfPageHelper
{
    /// <summary>Returns number of pages, or 1 if the file cannot be read as PDF.</summary>
    public static int TryGetPageCount(string pdfPath)
    {
        try
        {
            using var doc = PdfDocument.Open(pdfPath);
            var n = doc.NumberOfPages;
            return n < 1 ? 1 : n;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "PdfPig page count failed for {Path}, assuming single page", pdfPath);
            return 1;
        }
    }

    /// <summary>Renders one page to a unique temp PNG path at the given DPI.</summary>
    public static string RenderPageToTempPng(string pdfPath, int pageIndexZeroBased, int dpi)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "_ocr.png");
        using var stream = File.OpenRead(pdfPath);
        var options = new RenderOptions { Dpi = dpi };
        Conversion.SavePng(tempPath, stream, options: options, page: pageIndexZeroBased);
        return tempPath;
    }
}
