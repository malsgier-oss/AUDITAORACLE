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
/// Daily summary report: documents processed per day in a date range.
/// Professional template with RTL support and bilingual capabilities.
/// </summary>
public static class DailySummaryReport
{
    private const int MaxDocumentsForGrouping = 50_000;

    /// <summary>Returns (date string, count) for each day in range. Uses document store list + in-memory grouping.</summary>
    public static List<(string Date, int Count)> GetDocumentsPerDay(IDocumentStore store, DateTime from, DateTime to, string? branch = null, string? section = null, string? engagement = null)
    {
        var fromStr = from.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toStr = to.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var docs = store.ListDocuments(dateFrom: fromStr, dateTo: toStr + "T23:59:59", branch: branch, section: section, engagement: engagement, limit: MaxDocumentsForGrouping, newestFirst: true);

        var byDay = docs
            .Select(d => ParseDate(d.CaptureTime))
            .Where(dt => dt.HasValue)
            .GroupBy(dt => dt!.Value.Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var list = new List<(string, int)>();
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
        {
            var dateStr = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var count = byDay.TryGetValue(d, out var c) ? c : 0;
            list.Add((dateStr, count));
        }
        return list;
    }

    /// <summary>Generates a professional PDF daily summary report with RTL support.</summary>
    public static string GeneratePdf(IDocumentStore store, DateTime from, DateTime to, string? filePath = null, bool includeCharts = true, int retentionYears = 7, ReportWatermark watermark = ReportWatermark.None, string? branch = null, string? section = null, string? engagement = null, IConfigStore? configStore = null, string language = "en")
    {
        var isArabic = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var rows = GetDocumentsPerDay(store, from, to, branch, section, engagement);
        var total = rows.Sum(r => r.Count);

        var path = filePath ?? Path.Combine(Path.GetTempPath(), $"WorkAudit_DailySummary_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf");

        QuestPDF.Settings.License = LicenseType.Community;

        var L = (string key) => ReportLocalizationService.GetString(key, configStore);
        var logoPath = ReportHeaderFooter.GetLogoPath(configStore);

        var dayChunks = new List<List<(string Date, int Count)>>();
        for (var i = 0; i < rows.Count; i += ReportConstants.MaxDailySummaryDaysPerPage)
            dayChunks.Add(rows.Skip(i).Take(ReportConstants.MaxDailySummaryDaysPerPage).ToList());
        if (dayChunks.Count == 0)
            dayChunks.Add(new List<(string, int)>());

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            // One Page() = one physical sheet. Table (up to 90 rows) + 31 bar rows cannot share one page; chart goes on the next page.
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

            for (var pageIdx = 0; pageIdx < dayChunks.Count; pageIdx++)
            {
                var chunk = dayChunks[pageIdx];
                var pageTitle = dayChunks.Count > 1
                    ? (isArabic ? $"تقرير الملخص اليومي (صفحة {pageIdx + 1}/{dayChunks.Count})" : $"Daily Summary Report (Page {pageIdx + 1}/{dayChunks.Count})")
                    : (isArabic ? "تقرير الملخص اليومي" : "Daily Summary Report");

                void TableBody(ColumnDescriptor col)
                {
                    col.Item().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.ConstantColumn(100);
                        });
                        table.Header(header =>
                        {
                            header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(8)
                                .Text(isArabic ? "التاريخ" : "Date")
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                .Bold().FontColor(QuestPDF.Helpers.Colors.White);
                            header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(8)
                                .AlignRight().Text(L("Documents"))
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                .Bold().FontColor(QuestPDF.Helpers.Colors.White);
                        });
                        foreach (var (date, count) in chunk)
                        {
                            table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(8)
                                .Text(date).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                            table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(8)
                                .AlignRight().Text(ArabicFormattingService.FormatNumber(count))
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                        }
                    });
                }

                if (pageIdx == 0 && includeCharts)
                {
                    AddPage(pageTitle, TableBody);
                    // Last 31 days of series; bar chart is split across pages (31 rows + title do not fit one A4).
                    var chartRows = rows.Count > 31 ? rows.TakeLast(31).ToList() : rows;
                    if (chartRows.Count == 0) { continue; }
                    var baseChartTitle = isArabic
                        ? "تقرير الملخص اليومي — حجم المعالجة"
                        : "Daily Summary — Processing Volume";
                    for (var off = 0; off < chartRows.Count; off += ReportConstants.MaxDailyChartBarRowsPerPage)
                    {
                        var part = chartRows.Skip(off).Take(ReportConstants.MaxDailyChartBarRowsPerPage).ToList();
                        var cont = off > 0
                            ? (isArabic ? " (متابعة)" : " (continued)")
                            : "";
                        var chartPageTitle = baseChartTitle + cont;
                        var partCount = part.Count;
                        AddPage(chartPageTitle, col => col.Item().Element(c => ProfessionalChartService.RenderBarChart(c,
                            part.Select(p => (p.Date, p.Count)).ToList(),
                            "Daily Processing Volume", "حجم المعالجة اليومية", isArabic, true, partCount)));
                    }
                    continue;
                }

                AddPage(pageTitle, TableBody);
            }
        });
        
        document.GeneratePdf(path);
        return path;
    }

    private static DateTime? ParseDate(string? captureTime)
    {
        if (string.IsNullOrEmpty(captureTime)) return null;
        return DateTime.TryParse(captureTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
    }
}
