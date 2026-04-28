using System.IO;
using System.IO.Compression;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// P0 Chain of custody reporting - read-only document lifecycle timeline.
/// Generates Excel report from audit log for compliance.
/// </summary>
public interface IChainOfCustodyService
{
    /// <summary>Generate chain of custody report for one document. Returns Excel file path.</summary>
    string? GenerateReport(Document document, string? filePath = null);

    /// <summary>Generate chain of custody reports for multiple documents and zip into one archive. Returns ZIP path.</summary>
    string? GenerateBatchReport(IEnumerable<Document> documents, string? zipPath = null);
}

public class ChainOfCustodyService : IChainOfCustodyService
{
    private readonly ILogger _log = LoggingService.ForContext<ChainOfCustodyService>();
    private readonly IAuditLogStore _auditStore;

    public ChainOfCustodyService(IAuditLogStore auditStore)
    {
        _auditStore = auditStore;
    }

    public string? GenerateReport(Document document, string? filePath = null)
    {
        var entries = _auditStore.GetByEntityId("Document", document.Uuid);
        if (entries.Count == 0)
        {
            _log.Warning("No audit entries for document {Uuid}", document.Uuid);
            return null;
        }

        var defaultPath = Path.Combine(
            Path.GetTempPath(),
            $"ChainOfCustody_{document.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");

        var targetPath = filePath ?? defaultPath;

        try
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("Chain of Custody");

            ws.Cell(1, 1).Value = "Document Chain of Custody Report";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 5).Merge();

            ws.Cell(2, 1).Value = $"Document ID: {document.Id}";
            ws.Cell(3, 1).Value = $"UUID: {document.Uuid}";
            ws.Cell(4, 1).Value = $"Type: {document.DocumentType ?? "-"}";
            ws.Cell(5, 1).Value = $"Status: {document.Status}";
            ws.Cell(6, 1).Value = $"Immutable Hash: {document.ImmutableHash ?? "-"}";
            ws.Cell(7, 1).Value = "Generated: " + DateTime.UtcNow.ToString("O");
            ws.Cell(8, 1).Value = "Note: Application-level immutability. NOT hardware-certified WORM.";

            ws.Cell(10, 1).Value = "Timestamp";
            ws.Cell(10, 2).Value = "Action";
            ws.Cell(10, 3).Value = "User";
            ws.Cell(10, 4).Value = "Details";
            ws.Cell(10, 5).Value = "Success";
            ws.Range(10, 1, 10, 5).Style.Font.Bold = true;

            var row = 11;
            foreach (var e in entries.OrderBy(x => x.Timestamp))
            {
                ws.Cell(row, 1).Value = e.Timestamp;
                ws.Cell(row, 2).Value = e.Action;
                ws.Cell(row, 3).Value = e.Username;
                ws.Cell(row, 4).Value = e.Details ?? "";
                ws.Cell(row, 5).Value = e.Success ? "Yes" : "No";
                row++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(targetPath);

            _log.Information("Chain of custody report generated: {Path}", targetPath);
            return targetPath;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to generate chain of custody report");
            return null;
        }
    }

    public string? GenerateBatchReport(IEnumerable<Document> documents, string? zipPath = null)
    {
        var docList = documents.ToList();
        if (docList.Count == 0)
        {
            _log.Warning("No documents provided for batch chain of custody");
            return null;
        }

        var targetZip = zipPath ?? Path.Combine(Path.GetTempPath(), $"ChainOfCustody_Batch_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip");
        var tempDir = Path.Combine(Path.GetTempPath(), "ChainOfCustody_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tempDir);
            var generated = 0;
            foreach (var doc in docList)
            {
                var reportPath = GenerateReport(doc, Path.Combine(tempDir, $"ChainOfCustody_Doc{doc.Id}_{doc.Uuid[..8]}.xlsx"));
                if (reportPath != null) generated++;
            }

            if (generated == 0)
            {
                _log.Warning("No chain of custody reports generated for batch");
                return null;
            }

            if (File.Exists(targetZip)) File.Delete(targetZip);
            ZipFile.CreateFromDirectory(tempDir, targetZip);
            _log.Information("Chain of custody batch report generated: {Path} ({Count} reports)", targetZip, generated);
            return targetZip;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to generate chain of custody batch report");
            return null;
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }
}
