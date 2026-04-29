using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Helper for exporting summary reports to Excel.
/// </summary>
public static class ExcelReportHelper
{
    public static string ExportBranchSummary(IDocumentStore store, DateTime from, DateTime to,
        string? section, string? status, string? branch, string? filePath = null, string? engagement = null, bool includeCharts = false)
    {
        var rows = BranchSummaryReport.GetData(store, from, to, section, status, engagement);
        if (!string.IsNullOrEmpty(branch))
            rows = rows.Where(r => r.Branch == branch).ToList();
        return ExportTwoColumn(rows.Select(r => (r.Branch, r.Count)).ToList(), "Branch", "Documents",
            "Branch Summary Report", from, to, filePath ?? $"WorkAudit_BranchSummary_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx", includeCharts ? "Documents by Branch" : null);
    }

    public static string ExportSectionSummary(IDocumentStore store, DateTime from, DateTime to,
        string? branch, string? status, string? section, string? filePath = null, string? engagement = null, bool includeCharts = false)
    {
        var rows = SectionSummaryReport.GetData(store, from, to, branch, status, engagement);
        if (!string.IsNullOrEmpty(section))
            rows = rows.Where(r => r.Section == section).ToList();
        return ExportTwoColumn(rows.Select(r => (r.Section, r.Count)).ToList(), "Section", "Documents",
            "Section Summary Report", from, to, filePath ?? $"WorkAudit_SectionSummary_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx", includeCharts ? "Documents by Section" : null);
    }

    public static string ExportStatusSummary(IDocumentStore store, DateTime from, DateTime to,
        string? branch, string? section, string? filePath = null, string? engagement = null, bool includeCharts = false)
    {
        var rows = StatusSummaryReport.GetData(store, from, to, branch, section, engagement);
        return ExportTwoColumn(rows.Select(r => (r.Status, r.Count)).ToList(), "Status", "Documents",
            "Status Summary Report", from, to, filePath ?? $"WorkAudit_StatusSummary_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx", includeCharts ? "Documents by Status" : null);
    }

    public static string ExportDocumentTypeSummary(IDocumentStore store, DateTime from, DateTime to,
        string? branch, string? section, string? status, string? filePath = null, string? engagement = null, bool includeCharts = false)
    {
        var rows = DocumentTypeSummaryReport.GetData(store, from, to, branch, section, status, engagement);
        return ExportTwoColumn(rows.Select(r => (r.DocumentType, r.Count)).ToList(), "Document Type", "Documents",
            "Document Type Summary Report", from, to, filePath ?? $"WorkAudit_DocumentTypeSummary_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx", includeCharts ? "Documents by Type" : null);
    }

    private static string ExportTwoColumn(List<(string Col1, int Col2)> rows, string header1, string header2,
        string title, DateTime from, DateTime to, string fileName, string? chartTitle = null)
    {
        var path = Path.Combine(Path.GetTempPath(), fileName);
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Report");
        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, 2).Merge();
        ws.Cell(2, 1).Value = $"Period: {from:yyyy-MM-dd} to {to:yyyy-MM-dd}";
        ws.Cell(3, 1).Value = "Generated: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        ws.Cell(5, 1).Value = header1;
        ws.Cell(5, 2).Value = header2;
        ws.Range(5, 1, 5, 2).Style.Font.Bold = true;
        var rowNum = 6;
        foreach (var (col1, col2) in rows)
        {
            ws.Cell(rowNum, 1).Value = col1;
            ws.Cell(rowNum, 2).Value = col2;
            rowNum++;
        }
        ws.Columns().AdjustToContents();

        if (!string.IsNullOrEmpty(chartTitle) && rows.Count > 0)
        {
            try
            {
                var chartPng = ExcelChartHelper.CreateBarChartPng(rows, chartTitle);
                if (chartPng != null && chartPng.Length > 0)
                {
                    using var imgStream = new MemoryStream(chartPng);
                    ws.AddPicture(imgStream).MoveTo(ws.Cell(rowNum + 2, 1)).Scale(0.8);
                }
            }
            catch { /* Chart is optional; continue without it */ }
        }

        workbook.SaveAs(path);
        return path;
    }
}
