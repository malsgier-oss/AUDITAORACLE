using System.IO;
using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkAudit.Core.Reports.ReportTemplates;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports.ComplianceReports;

/// <summary>
/// SOX/IFRS-ready audit trail report: who did what, when.
/// Phase 7.2 Compliance: Regulatory Reports.
/// Uses pagination (bounded rows per page) to avoid stack overflow with large datasets.
/// Professional template with RTL support and bilingual capabilities.
/// </summary>
public static class AuditTrailComplianceReport
{
    /// <summary>Generates a PDF audit trail report for the given date range. Returns path to generated file.</summary>
    public static string GeneratePdf(IAuditLogStore auditStore, DateTime from, DateTime to, string? filePath = null, int limit = 5000, ReportWatermark watermark = ReportWatermark.None, IConfigStore? configStore = null, string language = "en")
    {
        var isArabic = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var entries = auditStore.Query(from, to, null, null, null, archivedOnly: false, limit, 0);

        var path = filePath ?? Path.Combine(
            Path.GetTempPath(),
            $"WorkAudit_AuditTrail_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf");

        QuestPDF.Settings.License = LicenseType.Community;

        var L = (string key) => ReportLocalizationService.GetString(key, configStore);
        var logoPath = ReportHeaderFooter.GetLogoPath(configStore);

        var totalEntries = entries.Count;
        var pageChunks = new List<List<AuditLogEntry>>();
        for (var i = 0; i < entries.Count; i += ReportConstants.MaxTableRowsPerPage)
        {
            var chunk = entries.Skip(i).Take(ReportConstants.MaxTableRowsPerPage).ToList();
            pageChunks.Add(chunk);
        }
        if (pageChunks.Count == 0)
            pageChunks.Add(new List<AuditLogEntry>());

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            for (var p = 0; p < pageChunks.Count; p++)
            {
                var chunk = pageChunks[p];
                var pageIndex = p;
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header()
                        .Column(column =>
                        {
                            column.Item().Text("Audit Trail — Compliance Report (SOX/IFRS-ready)")
                                .Bold().FontSize(14).FontColor(Colors.Blue.Darken2);
                            column.Item().PaddingTop(4).DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken1)).Text(text =>
                            {
                                text.Span("Period: ");
                                text.Span(from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                                text.Span(" to ");
                                text.Span(to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                                text.Span($"  |  {totalEntries} entries total");
                                if (pageChunks.Count > 1)
                                {
                                    text.Span($"  |  Page {pageIndex + 1}/{pageChunks.Count} (rows {pageIndex * ReportConstants.MaxTableRowsPerPage + 1}-{pageIndex * ReportConstants.MaxTableRowsPerPage + chunk.Count})");
                                }
                            });
                        });

                    page.Content().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(55);
                            columns.ConstantColumn(55);
                            columns.ConstantColumn(60);
                            columns.ConstantColumn(55);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().BorderBottom(1).Padding(4).Text("Timestamp").Bold();
                            header.Cell().BorderBottom(1).Padding(4).Text("User").Bold();
                            header.Cell().BorderBottom(1).Padding(4).Text("Action").Bold();
                            header.Cell().BorderBottom(1).Padding(4).Text("Category").Bold();
                            header.Cell().BorderBottom(1).Padding(4).Text("Entity").Bold();
                            header.Cell().BorderBottom(1).Padding(4).Text("Details").Bold();
                        });

                        foreach (var e in chunk)
                        {
                            var ts = e.Timestamp.Length >= 19 ? e.Timestamp.Substring(0, 19).Replace("T", " ") : e.Timestamp;
                            table.Cell().BorderBottom(0.5f).Padding(4).Text(ts);
                            table.Cell().BorderBottom(0.5f).Padding(4).Text(e.Username ?? e.UserId);
                            table.Cell().BorderBottom(0.5f).Padding(4).Text(e.Action);
                            table.Cell().BorderBottom(0.5f).Padding(4).Text(e.Category);
                            table.Cell().BorderBottom(0.5f).Padding(4).Text(Truncate($"{e.EntityType} {e.EntityId ?? ""}", 40));
                            table.Cell().BorderBottom(0.5f).Padding(4).Text(Truncate(e.Details ?? "", 50));
                        }
                    });

                    page.Footer()
                        .AlignCenter()
                        .DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Medium))
                        .Text(x =>
                        {
                            x.Span("Page ");
                            x.CurrentPageNumber();
                            x.Span(" of ");
                            x.TotalPages();
                            x.Span($"  |  Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
                        });
                    if (watermark != ReportWatermark.None)
                        page.Foreground().Element(c => ReportTemplates.ReportHeaderFooter.ComposeWatermark(c, watermark));
                });
            }
        });
        document.GeneratePdf(path);

        return path;
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }
}
