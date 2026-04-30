using System.IO;
using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkAudit.Core.Reports.ReportTemplates;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Section summary report: document counts per section (Individuals, Companies, Clearing).
/// Professional template with RTL support and bilingual capabilities.
/// </summary>
public static class SectionSummaryReport
{
    private const int MaxDocuments = 50_000;

    public static List<(string Section, int Count)> GetData(IDocumentStore store, DateTime from, DateTime to,
        string? branch = null, string? status = null, string? engagement = null)
    {
        var fromStr = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toStr = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59";
        var docs = store.ListDocuments(dateFrom: fromStr, dateTo: toStr, branch: branch, status: status, engagement: engagement, limit: MaxDocuments, newestFirst: true);

        var bySection = docs
            .GroupBy(d => string.IsNullOrEmpty(d.Section) ? "(No Section)" : d.Section)
            .OrderByDescending(g => g.Count())
            .Select(g => (g.Key, g.Count()))
            .ToList();

        return bySection;
    }

    public static string GeneratePdf(IDocumentStore store, DateTime from, DateTime to,
        string? branch = null, string? status = null, string? section = null, string? filePath = null, 
        bool includeCharts = true, int retentionYears = 7, ReportWatermark watermark = ReportWatermark.None, 
        string? engagement = null, IConfigStore? configStore = null, string language = "en")
    {
        var isArabic = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var rows = GetData(store, from, to, branch, status, engagement);
        if (!string.IsNullOrEmpty(section))
            rows = rows.Where(r => r.Section == section).ToList();

        var total = rows.Sum(r => r.Count);
        var path = filePath ?? Path.Combine(Path.GetTempPath(), $"WorkAudit_SectionSummary_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf");

        QuestPDF.Settings.License = LicenseType.Community;

        var L = (string key) => ReportLocalizationService.GetString(key, configStore);
        var logoPath = ReportHeaderFooter.GetLogoPath(configStore);

        var rowChunks = new List<List<(string Section, int Count)>>();
        for (var i = 0; i < rows.Count; i += ReportConstants.MaxTableRowsPerPage)
            rowChunks.Add(rows.Skip(i).Take(ReportConstants.MaxTableRowsPerPage).ToList());
        if (rowChunks.Count == 0)
            rowChunks.Add(new List<(string, int)>());

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

            for (var pageIdx = 0; pageIdx < rowChunks.Count; pageIdx++)
            {
                var chunk = rowChunks[pageIdx];
                var title = isArabic ? "تقرير ملخص الأقسام" : "Section Summary Report";
                if (rowChunks.Count > 1)
                    title += isArabic ? $" (صفحة {pageIdx + 1}/{rowChunks.Count})" : $" (Page {pageIdx + 1}/{rowChunks.Count})";

                var capturedChunk = chunk;
                var capturedPageIdx = pageIdx;
                AddPage(title, col =>
                {
                    col.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c,
                        capturedPageIdx == 0 ? "Section Analysis" : "Section Analysis (continued)",
                        capturedPageIdx == 0 ? "تحليل الأقسام" : "تحليل الأقسام (تابع)",
                        isArabic, "📑"));

                    col.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.ConstantColumn(100);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(8)
                                .Text(L("Section"))
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                .Bold().FontColor(QuestPDF.Helpers.Colors.White);

                            header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(8)
                                .AlignRight().Text(L("Documents"))
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                .Bold().FontColor(QuestPDF.Helpers.Colors.White);
                        });

                        foreach (var (sectionName, count) in capturedChunk)
                        {
                            table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(8)
                                .Text(sectionName).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));

                            table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(8)
                                .AlignRight().Text(ArabicFormattingService.FormatNumber(count))
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                        }
                    });
                });
            }

            if (includeCharts && rows.Count > 0)
            {
                var chartPageTitle = isArabic
                    ? "تقرير ملخص الأقسام — المستندات حسب القسم"
                    : "Section Summary — Documents by Section";
                AddPage(chartPageTitle, col =>
                {
                    col.Item().Element(c => ProfessionalChartService.RenderBarChart(c,
                        rows.Take(15).Select(r => (r.Section, r.Count)).ToList(),
                        "Documents by Section", "المستندات حسب القسم",
                        isArabic, true, 15));
                });
            }
        });
        
        document.GeneratePdf(path);
        return path;
    }
}
