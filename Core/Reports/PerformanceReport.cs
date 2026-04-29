using System.IO;
using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkAudit.Core.Reports.ReportTemplates;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Performance report: volume, throughput, clearing rate, issue rate by branch/section.
/// </summary>
public static class PerformanceReport
{
    private const int MaxDocuments = 50_000;

    public static List<PerformanceRow> GetDataByBranch(IDocumentStore store, DateTime from, DateTime to, string? branch = null, string? section = null, string? engagement = null)
    {
        var fromStr = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toStr = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59";
        var docs = store.ListDocuments(dateFrom: fromStr, dateTo: toStr, branch: branch, section: section, engagement: engagement, limit: MaxDocuments);
        var days = Math.Max(1, (to - from).Days + 1);

        var byBranch = docs
            .GroupBy(d => string.IsNullOrEmpty(d.Branch) ? "(No Branch)" : d.Branch)
            .Select(g =>
            {
                var list = g.ToList();
                var total = list.Count;
                var cleared = list.Count(d => d.Status == Enums.Status.Cleared);
                var issue = list.Count(d => d.Status == Enums.Status.Issue);
                var active = list.Count(d => d.Status != Enums.Status.Archived);
                var clearingRate = active > 0 ? (decimal)cleared / active * 100 : 0;
                var issueRate = total > 0 ? (decimal)issue / total * 100 : 0;
                var throughput = (decimal)total / days;

                return new PerformanceRow
                {
                    Name = g.Key,
                    Volume = total,
                    Throughput = throughput,
                    ClearingRate = clearingRate,
                    IssueRate = issueRate,
                    Draft = list.Count(d => d.Status == Enums.Status.Draft),
                    Reviewed = list.Count(d => d.Status == Enums.Status.Reviewed),
                    ReadyForAudit = list.Count(d => d.Status == Enums.Status.ReadyForAudit),
                    Issue = issue,
                    Cleared = cleared,
                    Archived = list.Count(d => d.Status == Enums.Status.Archived)
                };
            })
            .OrderByDescending(r => r.Volume)
            .ToList();

        return byBranch;
    }

    public static List<PerformanceRow> GetDataBySection(IDocumentStore store, DateTime from, DateTime to, string? branch = null, string? section = null, string? engagement = null)
    {
        var fromStr = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toStr = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59";
        var docs = store.ListDocuments(dateFrom: fromStr, dateTo: toStr, branch: branch, section: section, engagement: engagement, limit: MaxDocuments);
        var days = Math.Max(1, (to - from).Days + 1);

        var bySection = docs
            .GroupBy(d => string.IsNullOrEmpty(d.Section) ? "(No Section)" : d.Section)
            .Select(g =>
            {
                var list = g.ToList();
                var total = list.Count;
                var cleared = list.Count(d => d.Status == Enums.Status.Cleared);
                var issue = list.Count(d => d.Status == Enums.Status.Issue);
                var active = list.Count(d => d.Status != Enums.Status.Archived);
                var clearingRate = active > 0 ? (decimal)cleared / active * 100 : 0;
                var issueRate = total > 0 ? (decimal)issue / total * 100 : 0;
                var throughput = (decimal)total / days;

                return new PerformanceRow
                {
                    Name = g.Key,
                    Volume = total,
                    Throughput = throughput,
                    ClearingRate = clearingRate,
                    IssueRate = issueRate,
                    Draft = list.Count(d => d.Status == Enums.Status.Draft),
                    Reviewed = list.Count(d => d.Status == Enums.Status.Reviewed),
                    ReadyForAudit = list.Count(d => d.Status == Enums.Status.ReadyForAudit),
                    Issue = issue,
                    Cleared = cleared,
                    Archived = list.Count(d => d.Status == Enums.Status.Archived)
                };
            })
            .OrderByDescending(r => r.Volume)
            .ToList();

        return bySection;
    }

    public static string GeneratePdf(IDocumentStore store, IDocumentAssignmentStore? assignmentStore, DateTime from, DateTime to,
        bool byBranch = true, string? branch = null, string? section = null, string? filePath = null, bool includeCharts = true, int retentionYears = 7,
        IKpiService? kpiService = null, IRiskScoringService? riskScoringService = null, IQualityMetricsService? qualityMetricsService = null, ReportWatermark watermark = ReportWatermark.None, string? engagement = null, IConfigStore? configStore = null, string language = "en")
    {
        var isArabic = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var rows = byBranch ? GetDataByBranch(store, from, to, branch, section, engagement) : GetDataBySection(store, from, to, branch, section, engagement);
        if (!string.IsNullOrEmpty(branch))
            rows = rows.Where(r => r.Name == branch).ToList();
        if (!string.IsNullOrEmpty(section))
            rows = rows.Where(r => r.Name == section).ToList();

        var total = rows.Sum(r => r.Volume);
        var path = filePath ?? Path.Combine(Path.GetTempPath(), $"WorkAudit_Performance_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf");

        var periodDays = (to - from).Days + 1;
        var priorFrom = from.AddDays(-periodDays - 1);
        var priorTo = from.AddDays(-1);
        var priorRows = byBranch ? GetDataByBranch(store, priorFrom, priorTo, branch, section, engagement) : GetDataBySection(store, priorFrom, priorTo, branch, section, engagement);
        if (!string.IsNullOrEmpty(branch)) priorRows = priorRows.Where(r => r.Name == branch).ToList();
        if (!string.IsNullOrEmpty(section)) priorRows = priorRows.Where(r => r.Name == section).ToList();
        var priorTotal = priorRows.Sum(r => r.Volume);
        var periodChangePct = priorTotal > 0 ? (decimal)(total - priorTotal) / priorTotal * 100 : 0;

        var yoyFrom = from.AddYears(-1);
        var yoyTo = to.AddYears(-1);
        var yoyRows = byBranch ? GetDataByBranch(store, yoyFrom, yoyTo, branch, section, engagement) : GetDataBySection(store, yoyFrom, yoyTo, branch, section, engagement);
        if (!string.IsNullOrEmpty(branch)) yoyRows = yoyRows.Where(r => r.Name == branch).ToList();
        if (!string.IsNullOrEmpty(section)) yoyRows = yoyRows.Where(r => r.Name == section).ToList();
        var yoyTotal = yoyRows.Sum(r => r.Volume);
        var yoyChangePct = yoyTotal > 0 ? (decimal)(total - yoyTotal) / yoyTotal * 100 : 0;

        ComparisonResult? docYoy = null, docQoq = null, docMom = null;
        if (ServiceContainer.IsInitialized)
        {
            var c = ServiceContainer.GetOptionalService<IComparativeAnalysisService>();
            if (c != null)
            {
                docYoy = c.CompareYearOverYear(from, to, branch, section, engagement);
                docQoq = c.CompareQuarterOverQuarter(from, to, branch, section, engagement);
                docMom = c.CompareMonthOverMonth(from, to, branch, section, engagement);
            }
        }
        var comparativeDocumentSummary = ComparativePeriodSummaryText.Format(docYoy, docQoq, docMom, isArabic);

        QuestPDF.Settings.License = LicenseType.Community;

        var L = (string key) => ReportLocalizationService.GetString(key, configStore);
        var logoPath = ReportHeaderFooter.GetLogoPath(configStore);

        var priorByName = priorRows.ToDictionary(r => r.Name, r => r.Volume);
        var yoyByName = yoyRows.ToDictionary(r => r.Name, r => r.Volume);
        var hasPriorOrYoy = priorTotal > 0 || yoyTotal > 0;

        var rowChunks = new List<List<PerformanceRow>>();
        for (var i = 0; i < rows.Count; i += ReportConstants.MaxTableRowsPerPage)
            rowChunks.Add(rows.Skip(i).Take(ReportConstants.MaxTableRowsPerPage).ToList());
        if (rowChunks.Count == 0)
            rowChunks.Add(new List<PerformanceRow>());

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            for (var pageIdx = 0; pageIdx < rowChunks.Count; pageIdx++)
            {
                var chunk = rowChunks[pageIdx];
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(QuestPDF.Helpers.Colors.White);
                    
                    ProfessionalReportTemplate.ApplyLanguageSettings(page, isArabic);
                    page.DefaultTextStyle(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));

                    var title = isArabic ? "تقرير الأداء" : "Performance Report";
                    title += " — " + (byBranch ? (isArabic ? "حسب الفرع" : "By Branch") : (isArabic ? "حسب القسم" : "By Section"));

                    page.Header().Element(c => ReportHeaderFooter.ComposeHeader(c,
                        title,
                        $"{L("Period")}: {from:yyyy-MM-dd} {L("To")} {to:yyyy-MM-dd}  |  {L("TotalDocuments")}: {ArabicFormattingService.FormatNumber(total)}" +
                        (priorTotal > 0 ? $"  |  vs prior: {(periodChangePct >= 0 ? "+" : "")}{ArabicFormattingService.FormatDecimal(periodChangePct, 1)}%" : "") +
                        (yoyTotal > 0 ? $"  |  vs YoY: {(yoyChangePct >= 0 ? "+" : "")}{ArabicFormattingService.FormatDecimal(yoyChangePct, 1)}%" : "") +
                        (rowChunks.Count > 1 ? $"  |  Page {pageIdx + 1}/{rowChunks.Count}" : ""),
                        retentionYears,
                        isArabic,
                        logoPath));

                    page.Content().PaddingTop(10).Column(col =>
                    {
                        if (pageIdx == 0)
                        {
                            if (priorTotal > 0 || yoyTotal > 0)
                            {
                                col.Item().Text("Period-over-Period & Year-over-Year Comparison").Bold().FontSize(10);
                                if (priorTotal > 0)
                                    col.Item().PaddingTop(4).Text($"  This period: {ArabicFormattingService.FormatNumber(total)} docs  |  Prior period: {ArabicFormattingService.FormatNumber(priorTotal)} docs  |  Change: {(periodChangePct >= 0 ? "+" : "")}{ArabicFormattingService.FormatDecimal(periodChangePct, 1)}%").FontSize(9);
                                if (yoyTotal > 0)
                                    col.Item().PaddingTop(2).Text($"  Same period last year: {ArabicFormattingService.FormatNumber(yoyTotal)} docs  |  YoY change: {(yoyChangePct >= 0 ? "+" : "")}{ArabicFormattingService.FormatDecimal(yoyChangePct, 1)}%").FontSize(9);
                                col.Item().PaddingTop(8);
                            }

                            if (!string.IsNullOrEmpty(comparativeDocumentSummary))
                            {
                                col.Item()
                                    .Text(isArabic ? "مقارنة أحجام الوثائق (سنة / ربع / شهر)" : "Comparative document volumes (YoY / QoQ / MoM)")
                                    .Bold().FontSize(10);
                                col.Item().PaddingTop(4)
                                    .Text(comparativeDocumentSummary)
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9))
                                    .LineHeight(1.45f)
                                    .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                                col.Item().PaddingTop(8);
                            }

                            if (kpiService != null && rows.Count > 0)
                            {
                                var firstRow = rows[0];
                                var clearingVar = kpiService.GetVariance(KpiNames.ClearingRate, firstRow.ClearingRate, byBranch ? firstRow.Name : null, byBranch ? null : firstRow.Name);
                                var throughputVar = kpiService.GetVariance(KpiNames.Throughput, firstRow.Throughput, byBranch ? firstRow.Name : null, byBranch ? null : firstRow.Name);
                                var issueVar = kpiService.GetVariance(KpiNames.IssueRate, firstRow.IssueRate, byBranch ? firstRow.Name : null, byBranch ? null : firstRow.Name);
                                col.Item().Text("KPI Variance (Sample)").Bold().FontSize(10);
                                col.Item().PaddingTop(4).Table(kpiTable =>
                                {
                                    kpiTable.ColumnsDefinition(cd => { cd.ConstantColumn(120); cd.ConstantColumn(60); cd.ConstantColumn(60); cd.ConstantColumn(60); cd.ConstantColumn(60); });
                                    kpiTable.Header(h =>
                                    {
                                        h.Cell().BorderBottom(1).Padding(4).Text("KPI").Bold();
                                        h.Cell().BorderBottom(1).Padding(4).AlignRight().Text("Target").Bold();
                                        h.Cell().BorderBottom(1).Padding(4).AlignRight().Text("Actual").Bold();
                                        h.Cell().BorderBottom(1).Padding(4).AlignRight().Text("Variance").Bold();
                                        h.Cell().BorderBottom(1).Padding(4).AlignRight().Text("Status").Bold();
                                    });
                                    AddKpiRow(kpiTable, "Clearing Rate", clearingVar, isArabic);
                                    AddKpiRow(kpiTable, "Throughput/day", throughputVar, isArabic);
                                    AddKpiRow(kpiTable, "Issue Rate", issueVar, isArabic);
                                });
                                col.Item().PaddingTop(12);
                            }

                            if (qualityMetricsService != null)
                            {
                                var qm = qualityMetricsService.GetMetrics(store, from, to, string.IsNullOrEmpty(branch) ? null : branch, string.IsNullOrEmpty(section) ? null : section);
                                col.Item().Text("Quality Metrics & SLA").Bold().FontSize(10);
                                col.Item().PaddingTop(4).Text($"  OCR accuracy (>90%): {ArabicFormattingService.FormatDecimal(qm.OcrAccuracyPercent, 1)}%  |  Classification accuracy (>90%): {ArabicFormattingService.FormatDecimal(qm.ClassificationAccuracyPercent, 1)}%  |  Avg processing time: {ArabicFormattingService.FormatDecimal(qm.AvgProcessingTimeHours, 1)}h  |  Backlog: {ArabicFormattingService.FormatNumber(qm.BacklogCount)} docs (avg age {ArabicFormattingService.FormatDecimal(qm.AvgBacklogAgeDays, 1)} days)  |  SLA (reviewed within 24h): {ArabicFormattingService.FormatDecimal(qm.SlaCompliancePercent, 1)}%").FontSize(9);
                                col.Item().PaddingTop(8);
                            }
                        }

                        col.Item().Text(pageIdx == 0 ? (byBranch ? "By Branch" : "By Section") : (byBranch ? "By Branch (continued)" : "By Section (continued)")).Bold().FontSize(10);
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.2f);
                                columns.ConstantColumn(55);
                                if (hasPriorOrYoy) { columns.ConstantColumn(55); columns.ConstantColumn(55); }
                                columns.ConstantColumn(60);
                                columns.ConstantColumn(60);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(55);
                            });
                            table.Header(header =>
                            {
                                header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6)
                                    .Text(byBranch ? (isArabic ? "الفرع" : "Branch") : (isArabic ? "القسم" : "Section"))
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                                header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6).AlignRight()
                                    .Text(isArabic ? "الحجم" : "Volume")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                                if (hasPriorOrYoy)
                                {
                                    header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6).AlignRight()
                                        .Text(isArabic ? "السابق" : "Prior")
                                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                                    header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6).AlignRight()
                                        .Text("YoY")
                                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                                }
                                header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6).AlignRight()
                                    .Text(isArabic ? "الإنتاجية" : "Throughput")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                                header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6).AlignRight()
                                    .Text(isArabic ? "معدل التصفية" : "Clearing %")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                                header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6).AlignRight()
                                    .Text(isArabic ? "معدل المشكلات" : "Issue %")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                            });
                            foreach (var r in chunk)
                            {
                                table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                    .Text(r.Name).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                                table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                    .AlignRight().Text(ArabicFormattingService.FormatNumber(r.Volume))
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                                if (hasPriorOrYoy)
                                {
                                    var priorVol = priorByName.GetValueOrDefault(r.Name, 0);
                                    var yoyVol = yoyByName.GetValueOrDefault(r.Name, 0);
                                    var priorPct = priorVol > 0 ? (decimal)(r.Volume - priorVol) / priorVol * 100 : 0;
                                    var yoyPct = yoyVol > 0 ? (decimal)(r.Volume - yoyVol) / yoyVol * 100 : 0;
                                    table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                        .AlignRight().Text(priorTotal > 0 ? $"{(priorPct >= 0 ? "+" : "")}{ArabicFormattingService.FormatDecimal(priorPct, 1)}%" : "-")
                                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                                    table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                        .AlignRight().Text(yoyTotal > 0 ? $"{(yoyPct >= 0 ? "+" : "")}{ArabicFormattingService.FormatDecimal(yoyPct, 1)}%" : "-")
                                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                                }
                                table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                    .AlignRight().Text(ArabicFormattingService.FormatDecimal(r.Throughput, 1) + "/d")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                                table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                    .AlignRight().Text(ArabicFormattingService.FormatPercentage(r.ClearingRate))
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                                table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                    .AlignRight().Text(ArabicFormattingService.FormatPercentage(r.IssueRate))
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                            }
                        });

                        if (pageIdx == 0)
                        {
                            var chartData = rows.Take(15).Select(r => (r.Name, r.Volume)).ToList();
                            col.Item().PaddingTop(16).Element(c => ProfessionalChartService.RenderBarChart(c, chartData, 
                                "Volume by " + (byBranch ? "Branch" : "Section") + " (top 15)", 
                                (byBranch ? "الحجم حسب الفرع" : "الحجم حسب القسم") + " (أعلى 15)", 
                                isArabic, includeCharts, 15));

                            // Risk indicators section
                            if (riskScoringService != null)
                            {
                                var risks = riskScoringService.GetRiskIndicators(store, assignmentStore, from, to);
                                if (risks.Count > 0)
                                {
                                    col.Item().PaddingTop(16).Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, 
                                        "Risk Indicators (Critical & High)", "مؤشرات المخاطر (حرجة وعالية)", isArabic, "⚠️"));
                                    
                                    foreach (var risk in risks.Where(r => r.Level is RiskLevel.Critical or RiskLevel.High).Take(10))
                                    {
                                        var icon = risk.Level == RiskLevel.Critical ? "🔴" : "🟠";
                                        col.Item().PaddingTop(4).Text($"  {icon} {risk.EntityName} — {risk.Reason}").FontSize(9).FontColor(risk.Level == RiskLevel.Critical ? Colors.Red.Medium : Colors.Orange.Medium);
                                    }
                                }
                            }

                            if (assignmentStore != null)
                            {
                                var allAssignments = assignmentStore.ListAll(null, null);
                                var overdueCount = allAssignments.Count(a => a.Status is AssignmentStatus.Pending or AssignmentStatus.InProgress
                                    && !string.IsNullOrEmpty(a.DueDate) && DateTime.TryParse(a.DueDate, out var d) && d.Date < DateTime.Today);
                                if (overdueCount > 0)
                                {
                                    col.Item().PaddingTop(16).Text("Overdue Assignments").Bold().FontSize(11);
                                    col.Item().PaddingTop(4).Text($"  {ArabicFormattingService.FormatNumber(overdueCount)} assignment(s) overdue").FontSize(10).FontColor(Colors.Red.Medium);
                                }
                            }
                        }
                    });

                    page.Footer().Element(c => ReportHeaderFooter.ComposeFooter(c, retentionYears, isArabic));
                    if (watermark != ReportWatermark.None)
                        page.Foreground().Element(c => ReportHeaderFooter.ComposeWatermark(c, watermark, isArabic));
                });
            }
        });
        document.GeneratePdf(path);
        return path;
    }

    private static void AddKpiRow(QuestPDF.Fluent.TableDescriptor table, string name, KpiVarianceResult v, bool isArabic)
    {
        var statusIcon = v.Status switch { "OnTarget" => "✅", "Warning" => "⚠️", "Critical" => "🔴", _ => "" };
        table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
            .Text(name).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
        table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
            .AlignRight().Text(ArabicFormattingService.FormatDecimal(v.Target, 1))
            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
        table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
            .AlignRight().Text(ArabicFormattingService.FormatDecimal(v.Actual, 1))
            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
        table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
            .AlignRight().Text((v.Variance >= 0 ? "+" : "") + ArabicFormattingService.FormatDecimal(v.VariancePercent, 1) + "%")
            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
        table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
            .AlignRight().Text(v.Status + " " + statusIcon)
            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
    }
}

public class PerformanceRow
{
    public string Name { get; set; } = "";
    public int Volume { get; set; }
    public decimal Throughput { get; set; }
    public decimal ClearingRate { get; set; }
    public decimal IssueRate { get; set; }
    public int Draft { get; set; }
    public int Reviewed { get; set; }
    public int ReadyForAudit { get; set; }
    public int Issue { get; set; }
    public int Cleared { get; set; }
    public int Archived { get; set; }
}
