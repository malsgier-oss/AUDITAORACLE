using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkAudit.Core.Reports.ReportTemplates;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Document type summary report: document counts by document type.
/// Professional template with RTL support and bilingual capabilities.
/// </summary>
public static class DocumentTypeSummaryReport
{
    private const int MaxDocuments = 50_000;

    public static List<(string DocumentType, int Count)> GetData(IDocumentStore store, DateTime from, DateTime to,
        string? branch = null, string? section = null, string? status = null, string? engagement = null)
    {
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd") + "T23:59:59";
        var docs = store.ListDocuments(dateFrom: fromStr, dateTo: toStr, branch: branch, section: section, status: status, engagement: engagement, limit: MaxDocuments);

        var byType = docs
            .GroupBy(d => DocumentTypeInfo.IsUnclassified(d.DocumentType) ? DocumentTypeInfo.UnclassifiedType : d.DocumentType!.Trim())
            .OrderByDescending(g => g.Count())
            .Select(g => (g.Key, g.Count()))
            .ToList();

        return byType;
    }

    public static string GeneratePdf(IDocumentStore store, DateTime from, DateTime to,
        string? branch = null, string? section = null, string? status = null, string? filePath = null, 
        bool includeCharts = true, int retentionYears = 7, ReportWatermark watermark = ReportWatermark.None, 
        string? engagement = null, IConfigStore? configStore = null, string language = "en")
    {
        var isArabic = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var rows = GetData(store, from, to, branch, section, status, engagement);
        var total = rows.Sum(r => r.Count);
        var path = filePath ?? Path.Combine(Path.GetTempPath(), $"WorkAudit_DocumentTypeSummary_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf");

        var displayRows = rows.Count > ReportConstants.MaxTableRowsCap ? rows.Take(ReportConstants.MaxTableRowsCap).ToList() : rows;

        QuestPDF.Settings.License = LicenseType.Community;

        var L = (string key) => ReportLocalizationService.GetString(key, configStore);
        var logoPath = ReportHeaderFooter.GetLogoPath(configStore);

        var tablePageTitle = isArabic ? "تقرير ملخص نوع المستند" : "Document Type Summary Report";

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            void AddPage(string pageTitle, Action<ColumnDescriptor> buildColumn)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(QuestPDF.Helpers.Colors.White);

                    ProfessionalReportTemplate.ApplyLanguageSettings(page, isArabic);
                    page.DefaultTextStyle(ProfessionalReportTemplate.GetTextStyle(isArabic, 10));

                    page.Header().Element(c => ReportHeaderFooter.ComposeHeader(c,
                        pageTitle,
                        $"{L("Period")}: {from:yyyy-MM-dd} {L("To")} {to:yyyy-MM-dd}  |  {L("TotalDocuments")}: {ArabicFormattingService.FormatNumber(total)}",
                        retentionYears,
                        isArabic,
                        logoPath));

                    page.Content().PaddingTop(10).Column(buildColumn);

                    page.Footer().Element(c => ReportHeaderFooter.ComposeFooter(c, retentionYears, isArabic));

                    if (watermark != ReportWatermark.None)
                        page.Foreground().Element(c => ReportHeaderFooter.ComposeWatermark(c, watermark, isArabic));
                });
            }

            AddPage(tablePageTitle, col =>
            {
                col.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c,
                    "Document Type Distribution", "توزيع نوع المستند", isArabic, "📄"));

                if (rows.Count > ReportConstants.MaxTableRowsCap)
                {
                    col.Item().PaddingBottom(8).Text(isArabic
                            ? $"(عرض أعلى {ReportConstants.MaxTableRowsCap} من {rows.Count} نوع)"
                            : $"(Showing top {ReportConstants.MaxTableRowsCap} of {rows.Count} types)")
                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9))
                        .Italic().FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                }

                col.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.ConstantColumn(100);
                        columns.ConstantColumn(100);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(8)
                            .Text(isArabic ? "نوع المستند" : "Document Type")
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

                    foreach (var (docType, count) in displayRows)
                    {
                        var percent = total > 0 ? (decimal)count / total * 100 : 0;

                        table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(8)
                            .Text(docType).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));

                        table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(8)
                            .AlignRight().Text(ArabicFormattingService.FormatNumber(count))
                            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));

                        table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(8)
                            .AlignRight().Text(ArabicFormattingService.FormatPercentage(percent))
                            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                    }
                });
            });

            if (includeCharts && displayRows.Count > 0)
            {
                var chartPageTitle = isArabic
                    ? "تقرير ملخص نوع المستند — أنواع المستندات (أعلى 10)"
                    : "Document Type Summary — Top 10 Types";
                AddPage(chartPageTitle, col =>
                {
                    col.Item().Element(c => ProfessionalChartService.RenderBarChart(c,
                        displayRows.Take(10).Select(r => (r.DocumentType, r.Count)).ToList(),
                        "Document Types (Top 10)", "أنواع المستندات (أعلى 10)",
                        isArabic, true, 10));
                });
            }
        });
        
        document.GeneratePdf(path);
        return path;
    }
}
