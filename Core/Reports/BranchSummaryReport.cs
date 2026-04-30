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
/// Branch summary report: document counts per branch.
/// Professional template with RTL support and bilingual capabilities.
/// </summary>
public static class BranchSummaryReport
{
    private const int MaxDocuments = 50_000;

    public static List<(string Branch, int Count)> GetData(IDocumentStore store, DateTime from, DateTime to,
        string? section = null, string? status = null, string? engagement = null, string? branch = null)
    {
        var fromStr = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toStr = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59";
        // Push the branch filter into SQL so the row cap doesn't silently drop the requested branch.
        var docs = store.ListDocuments(dateFrom: fromStr, dateTo: toStr, branch: branch, section: section, status: status, engagement: engagement, limit: MaxDocuments, newestFirst: true);

        var byBranch = docs
            .GroupBy(d => string.IsNullOrEmpty(d.Branch) ? "(No Branch)" : d.Branch)
            .OrderByDescending(g => g.Count())
            .Select(g => (g.Key, g.Count()))
            .ToList();

        return byBranch;
    }

    public static string GeneratePdf(IDocumentStore store, DateTime from, DateTime to,
        string? section = null, string? status = null, string? branch = null, string? filePath = null, 
        bool includeCharts = true, int retentionYears = 7, ReportWatermark watermark = ReportWatermark.None, 
        string? engagement = null, IConfigStore? configStore = null, string language = "en")
    {
        var isArabic = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var rows = GetData(store, from, to, section, status, engagement, branch);

        var total = rows.Sum(r => r.Count);
        var path = filePath ?? Path.Combine(Path.GetTempPath(), $"WorkAudit_BranchSummary_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf");

        QuestPDF.Settings.License = LicenseType.Community;

        var L = (string key) => ReportLocalizationService.GetString(key, configStore);
        var logoPath = ReportHeaderFooter.GetLogoPath(configStore);

        var rowChunks = new List<List<(string Branch, int Count)>>();
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
                var title = isArabic ? "تقرير ملخص الفروع" : "Branch Summary Report";
                if (rowChunks.Count > 1)
                    title += isArabic ? $" (صفحة {pageIdx + 1}/{rowChunks.Count})" : $" (Page {pageIdx + 1}/{rowChunks.Count})";

                var capturedChunk = chunk;
                var capturedPageIdx = pageIdx;
                AddPage(title, col =>
                {
                    col.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c,
                        capturedPageIdx == 0 ? "Branch Analysis" : "Branch Analysis (continued)",
                        capturedPageIdx == 0 ? "تحليل الفروع" : "تحليل الفروع (تابع)",
                        isArabic, "🏢"));

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
                                .Text(isArabic ? "الفرع" : "Branch")
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                .Bold().FontColor(QuestPDF.Helpers.Colors.White);

                            header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(8)
                                .AlignRight().Text(L("Documents"))
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                .Bold().FontColor(QuestPDF.Helpers.Colors.White);
                        });

                        foreach (var (branchName, count) in capturedChunk)
                        {
                            table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(8)
                                .Text(branchName).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));

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
                    ? "تقرير ملخص الفروع — المستندات حسب الفرع (أعلى 15)"
                    : "Branch Summary — Documents by Branch (Top 15)";
                AddPage(chartPageTitle, col =>
                {
                    col.Item().Element(c => ProfessionalChartService.RenderBarChart(c,
                        rows.Take(15).Select(r => (r.Branch, r.Count)).ToList(),
                        "Documents by Branch (Top 15)", "المستندات حسب الفرع (أعلى 15)",
                        isArabic, true, 15));
                });
            }
        });
        
        document.GeneratePdf(path);
        return path;
    }
}
