using System.IO;
using System.Globalization;
using System.Text;
using PDFtoImage;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Serilog;
using SkiaSharp;
using WorkAudit.Core.Services;
using WorkAudit.Storage;
using DomainDocument = WorkAudit.Domain.Document;
using PdfDocumentOpenMode = PdfSharp.Pdf.IO.PdfDocumentOpenMode;
using PdfReader = PdfSharp.Pdf.IO.PdfReader;

namespace WorkAudit.Core.Export;

/// <summary>Options for <see cref="ISearchExportService.ExportToPdf"/> when combining files.</summary>
public sealed class ExportCombinedPdfOptions
{
    /// <summary>
    /// When <see langword="true"/> (default), PDFs that cannot be imported losslessly are merged using a rasterized fallback.
    /// When <see langword="false"/>, import failure throws so the merge does not silently reduce quality.
    /// </summary>
    public bool AllowLossyPdfFallback { get; init; } = true;
}

/// <summary>
/// Exports search results to CSV, Excel, and PDF.
/// </summary>
public interface ISearchExportService
{
    string ExportToCsv(IEnumerable<DomainDocument> documents, string? filePath = null);
    string ExportToExcel(IEnumerable<DomainDocument> documents, string? filePath = null);
    /// <summary>Exports document files (PDFs and images) as a single combined PDF. Returns output path.</summary>
    string ExportToPdf(IEnumerable<DomainDocument> documents, string? filePath = null, ExportCombinedPdfOptions? options = null);
    /// <summary>Creates a PDF from image file paths (for document scanning). Returns output path.</summary>
    string CreatePdfFromImagePaths(IEnumerable<string> imagePaths, string outputPath);
}

public class SearchExportService : ISearchExportService
{
    private readonly ILogger _log = LoggingService.ForContext<SearchExportService>();

    public string ExportToCsv(IEnumerable<DomainDocument> documents, string? filePath = null)
    {
        var list = documents.ToList();
        var notesStore = ServiceContainer.GetOptionalService<INotesStore>();
        var sb = new StringBuilder();
        sb.AppendLine("Id,Uuid,FilePath,DocumentType,Status,Section,ExtractedDate,Amounts,AccountName,AccountNumber,TransactionReference,CaptureTime,Source,Notes,ArchivedAt,RetentionExpiryDate,LegalHold,LegalHoldCaseNumber,LegalHoldReason,ImmutableHash");

        foreach (var d in list)
        {
            var notesSummary = GetNotesSummaryForDocument(notesStore, d.Id);
            var line = string.Join(",", Escape(d.Id.ToString(CultureInfo.InvariantCulture)), Escape(d.Uuid), Escape(d.FilePath),
                Escape(d.DocumentType), Escape(d.Status), Escape(d.Section),
                Escape(d.ExtractedDate), Escape(d.Amounts), Escape(d.AccountName), Escape(d.AccountNumber), Escape(d.TransactionReference), Escape(d.CaptureTime), Escape(d.Source), Escape(notesSummary),
                Escape(d.ArchivedAt), Escape(d.RetentionExpiryDate), Escape(d.LegalHold ? "Yes" : "No"),
                Escape(d.LegalHoldCaseNumber), Escape(d.LegalHoldReason), Escape(d.ImmutableHash));
            sb.AppendLine(line);
        }

        var csv = sb.ToString();
        if (!string.IsNullOrEmpty(filePath))
        {
            File.WriteAllText(filePath, csv, Encoding.UTF8);
            _log.Information("Exported {Count} documents to CSV: {Path}", list.Count, filePath);
        }
        return csv;
    }

    public string ExportToExcel(IEnumerable<DomainDocument> documents, string? filePath = null)
    {
        var list = documents.ToList();
        try
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("Search Results");

            ws.Cell(1, 1).Value = "Id";
            ws.Cell(1, 2).Value = "FilePath";
            ws.Cell(1, 3).Value = "DocumentType";
            ws.Cell(1, 4).Value = "Status";
            ws.Cell(1, 5).Value = "Section";
            ws.Cell(1, 6).Value = "ExtractedDate";
            ws.Cell(1, 7).Value = "Amounts";
            ws.Cell(1, 8).Value = "AccountName";
            ws.Cell(1, 9).Value = "AccountNumber";
            ws.Cell(1, 10).Value = "TransactionReference";
            ws.Cell(1, 11).Value = "CaptureTime";
            ws.Cell(1, 12).Value = "Source";
            ws.Cell(1, 13).Value = "Notes";
            ws.Cell(1, 14).Value = "ArchivedAt";
            ws.Cell(1, 15).Value = "RetentionExpiryDate";
            ws.Cell(1, 16).Value = "LegalHold";
            ws.Cell(1, 17).Value = "LegalHoldCaseNumber";
            ws.Cell(1, 18).Value = "LegalHoldReason";
            ws.Cell(1, 19).Value = "ImmutableHash";

            var notesStore = ServiceContainer.GetOptionalService<INotesStore>();
            var row = 2;
            foreach (var d in list)
            {
                var notesSummary = GetNotesSummaryForDocument(notesStore, d.Id);
                ws.Cell(row, 1).Value = d.Id;
                ws.Cell(row, 2).Value = d.FilePath;
                ws.Cell(row, 3).Value = d.DocumentType ?? "";
                ws.Cell(row, 4).Value = d.Status;
                ws.Cell(row, 5).Value = d.Section;
                ws.Cell(row, 6).Value = d.ExtractedDate ?? "";
                ws.Cell(row, 7).Value = d.Amounts ?? "";
                ws.Cell(row, 8).Value = d.AccountName ?? "";
                ws.Cell(row, 9).Value = d.AccountNumber ?? "";
                ws.Cell(row, 10).Value = d.TransactionReference ?? "";
                ws.Cell(row, 11).Value = d.CaptureTime ?? "";
                ws.Cell(row, 12).Value = d.Source ?? "";
                ws.Cell(row, 13).Value = notesSummary;
                ws.Cell(row, 14).Value = d.ArchivedAt ?? "";
                ws.Cell(row, 15).Value = d.RetentionExpiryDate ?? "";
                ws.Cell(row, 16).Value = d.LegalHold ? "Yes" : "No";
                ws.Cell(row, 17).Value = d.LegalHoldCaseNumber ?? "";
                ws.Cell(row, 18).Value = d.LegalHoldReason ?? "";
                ws.Cell(row, 19).Value = d.ImmutableHash ?? "";
                row++;
            }

            ws.Columns().AdjustToContents();

            var path = filePath ?? Path.Combine(Path.GetTempPath(), $"WorkAudit_Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            workbook.SaveAs(path);
            _log.Information("Exported {Count} documents to Excel: {Path}", list.Count, path);
            return path;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Excel export failed, falling back to CSV");
            return ExportToCsv(list, filePath != null ? Path.ChangeExtension(filePath, ".csv") : null);
        }
    }

    public string ExportToPdf(IEnumerable<DomainDocument> documents, string? filePath = null, ExportCombinedPdfOptions? options = null)
    {
        options ??= new ExportCombinedPdfOptions();
        var list = documents.Where(d => !string.IsNullOrEmpty(d.FilePath) && File.Exists(d.FilePath)).ToList();
        if (list.Count == 0)
        {
            throw new InvalidOperationException("No documents with valid files to export. Ensure FilePath exists and files are accessible.");
        }

        var path = filePath ?? Path.Combine(Path.GetTempPath(), $"WorkAudit_Export_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        const long MaxFileSize = 50 * 1024 * 1024;

        var output = new PdfDocument();
        foreach (var doc in list)
        {
            var ext = Path.GetExtension(doc.FilePath).ToLowerInvariant();
            if (ext == ".pdf")
            {
                try
                {
                    AppendPdfPagesImported(output, doc.FilePath!);
                }
                catch (Exception ex)
                {
                    if (!options.AllowLossyPdfFallback)
                    {
                        throw new InvalidOperationException(
                            $"Lossless PDF merge failed for \"{doc.FilePath}\". The file cannot be combined without re-rendering pages.",
                            ex);
                    }

                    _log.Warning(ex, "Lossless PDF merge failed; using raster fallback for {Path}", doc.FilePath);
                    AppendPdfPagesRasterFallback(output, doc.FilePath!);
                }
            }
            else if (IsSupportedImage(ext))
            {
                var fileInfo = new FileInfo(doc.FilePath!);
                if (fileInfo.Length > MaxFileSize)
                {
                    _log.Warning("Skipping large file ({Size} MB): {Path}", fileInfo.Length / (1024 * 1024), doc.FilePath);
                    continue;
                }

                try
                {
                    AppendImagePageFitA4(output, doc.FilePath!);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "PdfSharp image embed failed; skipping document {Id}: {Path}", doc.Id, doc.FilePath);
                }
            }
            else
            {
                _log.Warning("Skipping document {Id} - unsupported format: {Path}", doc.Id, doc.FilePath);
            }
        }

        if (output.PageCount == 0)
            throw new InvalidOperationException("No pages could be written to the combined PDF.");

        output.Save(path);
        _log.Information("Exported documents to PDF: {Path}", path);
        return path;
    }

    public string CreatePdfFromImagePaths(IEnumerable<string> imagePaths, string outputPath)
    {
        return PdfCreationService.CreateFromImages(imagePaths, outputPath);
    }

    /// <summary>Whether a file path can be included in a combined PDF export (PDF or supported image).</summary>
    public static bool IsSupportedForCombinedPdfExport(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext == ".pdf" || SupportedImageExtensions.Contains(ext);
    }

    /// <summary>
    /// Returns whether PDFsharp can open the file in <see cref="PdfDocumentOpenMode.Import"/> (same as merge).
    /// Use for preflight before enqueueing a lossless-only merge.
    /// </summary>
    public static bool CanMergePdfLosslessly(string pdfPath)
    {
        if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath)) return false;
        if (!string.Equals(Path.GetExtension(pdfPath), ".pdf", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            return doc.PageCount >= 0;
        }
        catch
        {
            return false;
        }
    }

    private const int PdfRenderDpi = 150;
    private static readonly string[] SupportedImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".tif" };

    private static bool IsSupportedImage(string ext) => SupportedImageExtensions.Contains(ext);

    /// <summary>Copies PDF pages without re-rendering (preserves vector/text quality).</summary>
    private static void AppendPdfPagesImported(PdfDocument output, string pdfPath)
    {
        using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        for (var i = 0; i < input.PageCount; i++)
            output.AddPage(input.Pages[i]);
    }

    private static void AppendImagePageFitA4(PdfDocument output, string imagePath)
    {
        var page = output.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        var pageW = page.Width.Point;
        var pageH = page.Height.Point;
        using var gfx = XGraphics.FromPdfPage(page);
        using var img = XImage.FromFile(imagePath);
        var iw = img.PointWidth;
        var ih = img.PointHeight;
        if (iw <= 0 || ih <= 0) return;
        var scale = Math.Min(pageW / iw, pageH / ih);
        var w = iw * scale;
        var h = ih * scale;
        var x = (pageW - w) / 2;
        var y = (pageH - h) / 2;
        gfx.DrawImage(img, x, y, w, h);
    }

    private void AppendPdfPagesRasterFallback(PdfDocument output, string pdfPath)
    {
        var pdfPages = RenderPdfPagesToImages(pdfPath);
        foreach (var pageBytes in pdfPages)
        {
            var page = output.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            var pageW = page.Width.Point;
            var pageH = page.Height.Point;
            using var gfx = XGraphics.FromPdfPage(page);
            using var img = XImage.FromStream(new MemoryStream(pageBytes));
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
    }

    private static List<byte[]> RenderPdfPagesToImages(string pdfPath)
    {
        var result = new List<byte[]>();
        try
        {
            using var pdfDoc = global::UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            var pageCount = pdfDoc.NumberOfPages;
            var options = new RenderOptions(PdfRenderDpi);
            for (var i = 0; i < pageCount; i++)
            {
                using var stream = File.OpenRead(pdfPath);
                using var bitmap = Conversion.ToImage(stream, i, false, null, options);
                if (bitmap == null) continue;
                using var ms = new MemoryStream();
                bitmap.Encode(ms, SKEncodedImageFormat.Png, 90);
                result.Add(ms.ToArray());
            }
        }
        catch (Exception ex)
        {
            LoggingService.ForContext<SearchExportService>().Warning(ex, "Failed to render PDF: {Path}", pdfPath);
        }
        return result;
    }

    private static string GetNotesSummaryForDocument(INotesStore? notesStore, int documentId)
    {
        if (notesStore == null) return "";
        var notes = notesStore.GetByDocumentId(documentId);
        if (notes.Count == 0) return "";
        if (notes.Count == 1) return notes[0].Content.Length > 200 ? notes[0].Content[..200] + "..." : notes[0].Content;
        return $"{notes.Count} note(s): " + (notes[0].Content.Length > 100 ? notes[0].Content[..100] + "..." : notes[0].Content);
    }

    private static string Escape(string? s)
    {
        if (s == null) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
