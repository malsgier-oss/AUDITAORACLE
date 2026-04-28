using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkAudit.Core.Reports.ReportTemplates;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Status summary report: document counts by status (Draft, Reviewed, Cleared, etc.).
/// Professional template with RTL support and bilingual capabilities.
/// </summary>
public static class StatusSummaryReport
{
    private const int MaxDocuments = 50_000;

    public static List<(string Status, int Count)> GetData(IDocumentStore store, DateTime from, DateTime to,
        string? branch = null, string? section = null, string? engagement = null)
    {
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd") + "T23:59:59";
        var docs = store.ListDocuments(dateFrom: fromStr, dateTo: toStr, branch: branch, section: section, engagement: engagement, limit: MaxDocuments);

        var byStatus = docs
            .GroupBy(d => string.IsNullOrEmpty(d.Status) ? "(No Status)" : d.Status)
            .OrderBy(g => Enums.StatusValues.Contains(g.Key) ? Array.IndexOf(Enums.StatusValues, g.Key) : 999)
            .ThenByDescending(g => g.Count())
            .Select(g => (g.Key, g.Count()))
            .ToList();

        return byStatus;
    }

    public static string GeneratePdf(IDocumentStore store, DateTime from, DateTime to,
        string? branch = null, string? section = null, string? filePath = null, bool includeCharts = true, 
        int retentionYears = 7, ReportWatermark watermark = ReportWatermark.None, string? engagement = null, 
        IConfigStore? configStore = null, string language = "en")
    {
        var isArabic = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var rows = GetData(store, from, to, branch, section, engagement);
        var total = rows.Sum(r => r.Count);
        var path = filePath ?? Path.Combine(Path.GetTempPath(), $"WorkAudit_StatusSummary_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf");

        QuestPDF.Settings.License = LicenseType.Community;

        var L = (string key) => ReportLocalizationService.GetString(key, configStore);
        var logoPath = ReportHeaderFooter.GetLogoPath(configStore);

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(QuestPDF.Helpers.Colors.White);
                
                ProfessionalReportTemplate.ApplyLanguageSettings(page, isArabic);
                page.DefaultTextStyle(ProfessionalReportTemplate.GetTextStyle(isArabic, 10));

                page.Header().Element(c => ReportHeaderFooter.ComposeHeader(c,
                    isArabic ? "تقرير ملخص الحالة" : "Status Summary Report",
                    $"{L("Period")}: {from:yyyy-MM-dd} {L("To")} {to:yyyy-MM-dd}  |  {L("TotalDocuments")}: {ArabicFormattingService.FormatNumber(total)}",
                    retentionYears,
                    isArabic,
                    logoPath));

                page.Content().PaddingTop(10).Column(col =>
                {
                    // Section header
                    col.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, 
                        "Status Breakdown", "تفصيل الحالة", isArabic, "📊"));

                    // Professional table
                    col.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.ConstantColumn(100);
                            columns.ConstantColumn(100);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(8)
                                .Text(isArabic ? "الحالة" : "Status")
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                .Bold().FontColor(QuestPDF.Helpers.Colors.White);
                            
                            header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(8)
                                .AlignRight().Text(L("Documents"))
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                .Bold().FontColor(QuestPDF.Helpers.Colors.White);
                            
                            header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(8)
                                .AlignRight().Text("%")
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                .Bold().FontColor(QuestPDF.Helpers.Colors.White);
                        });

                        foreach (var (status, count) in rows)
                        {
                            var percent = total > 0 ? (decimal)count / total * 100 : 0;
                            var statusColor = ProfessionalChartService.GetStatusColor(status);

                            table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(8)
                                .Row(row =>
                                {
                                    row.ConstantItem(4).Background(statusColor);
                                    row.RelativeItem().PaddingLeft(8).Text(status)
                                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                                });
                            
                            table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(8)
                                .AlignRight().Text(ArabicFormattingService.FormatNumber(count))
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                            
                            table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(8)
                                .AlignRight().Text(ArabicFormattingService.FormatPercentage(percent))
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                        }
                    });

                    // Chart
                    if (includeCharts)
                    {
                        var chartData = rows.Select(r => (r.Status, r.Count, ProfessionalChartService.GetStatusColor(r.Status))).ToList();
                        col.Item().PaddingTop(16).Element(c => ProfessionalChartService.RenderPieChart(c, 
                            chartData, "Status Distribution", "توزيع الحالة", isArabic, includeCharts));
                    }
                });

                page.Footer().Element(c => ReportHeaderFooter.ComposeFooter(c, retentionYears, isArabic));
                
                if (watermark != ReportWatermark.None)
                    page.Foreground().Element(c => ReportHeaderFooter.ComposeWatermark(c, watermark, isArabic));
            });
        });
        
        document.GeneratePdf(path);
        return path;
    }
}
