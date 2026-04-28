using System.IO;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkAudit.Core.Reports.ReportTemplates;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Assignment summary report: assignments by user, status breakdown, overdue counts, completion rate.
/// Uses bounded rows per page to avoid QuestPDF stack overflow.
/// </summary>
public static class AssignmentSummaryReport
{
    public static List<AssignmentSummaryRow> GetData(IDocumentAssignmentStore assignmentStore, IUserStore userStore, DateTime from, DateTime to)
    {
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd") + "T23:59:59";
        var users = userStore.ListUsers(isActive: true);
        var allAssignments = assignmentStore.ListAll(null, null);

        var rows = new List<AssignmentSummaryRow>();

        foreach (var user in users)
        {
            var userAssignments = allAssignments.Where(a => a.AssignedToUserId == user.Id).ToList();
            var inPeriod = userAssignments.Where(a =>
                string.Compare(a.AssignedAt, fromStr, StringComparison.Ordinal) >= 0 &&
                string.Compare(a.AssignedAt, toStr, StringComparison.Ordinal) <= 0).ToList();
            var completedInPeriod = userAssignments.Where(a =>
                a.Status == AssignmentStatus.Completed &&
                !string.IsNullOrEmpty(a.CompletedAt) &&
                string.Compare(a.CompletedAt, fromStr, StringComparison.Ordinal) >= 0 &&
                string.Compare(a.CompletedAt, toStr, StringComparison.Ordinal) <= 0).ToList();

            var pending = userAssignments.Count(a => a.Status == AssignmentStatus.Pending);
            var inProgress = userAssignments.Count(a => a.Status == AssignmentStatus.InProgress);
            var completed = userAssignments.Count(a => a.Status == AssignmentStatus.Completed);
            var cancelled = userAssignments.Count(a => a.Status == AssignmentStatus.Cancelled);
            var overdue = userAssignments.Count(a =>
                (a.Status == AssignmentStatus.Pending || a.Status == AssignmentStatus.InProgress) &&
                !string.IsNullOrEmpty(a.DueDate) &&
                DateTime.TryParse(a.DueDate, out var d) && d.Date < DateTime.Today);

            var totalActive = pending + inProgress + completed;
            var completionRate = totalActive > 0 ? (decimal)completed / totalActive * 100 : 0;

            rows.Add(new AssignmentSummaryRow
            {
                Username = user.Username,
                DisplayName = user.DisplayName,
                AssignedInPeriod = inPeriod.Count,
                CompletedInPeriod = completedInPeriod.Count,
                Pending = pending,
                InProgress = inProgress,
                Completed = completed,
                Cancelled = cancelled,
                Overdue = overdue,
                CompletionRate = completionRate
            });
        }

        return rows.Where(r => r.AssignedInPeriod > 0 || r.CompletedInPeriod > 0 || r.Pending > 0 || r.InProgress > 0)
            .OrderByDescending(r => r.AssignedInPeriod + r.CompletedInPeriod + r.Pending + r.InProgress).ToList();
    }

    public static string GeneratePdf(IDocumentAssignmentStore assignmentStore, IUserStore userStore, DateTime from, DateTime to,
        string? filePath = null, int retentionYears = 7, ReportWatermark watermark = ReportWatermark.None, IConfigStore? configStore = null, string language = "en")
    {
        var isArabic = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var rows = GetData(assignmentStore, userStore, from, to);
        var allAssignments = assignmentStore.ListAll(null, null);
        var totalAssignments = allAssignments.Count;
        var pending = allAssignments.Count(a => a.Status == AssignmentStatus.Pending);
        var inProgress = allAssignments.Count(a => a.Status == AssignmentStatus.InProgress);
        var completed = allAssignments.Count(a => a.Status == AssignmentStatus.Completed);
        var overdue = allAssignments.Count(a =>
            (a.Status == AssignmentStatus.Pending || a.Status == AssignmentStatus.InProgress) &&
            !string.IsNullOrEmpty(a.DueDate) &&
            DateTime.TryParse(a.DueDate, out var d) && d.Date < DateTime.Today);
        var completionRate = (pending + inProgress + completed) > 0
            ? (decimal)completed / (pending + inProgress + completed) * 100 : 0;

        var path = filePath ?? Path.Combine(Path.GetTempPath(), $"WorkAudit_AssignmentSummary_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf");

        QuestPDF.Settings.License = LicenseType.Community;

        var L = (string key) => ReportLocalizationService.GetString(key, configStore);
        var logoPath = ReportHeaderFooter.GetLogoPath(configStore);

        var rowChunks = new List<List<AssignmentSummaryRow>>();
        for (var i = 0; i < rows.Count; i += ReportConstants.MaxTableRowsPerPage)
            rowChunks.Add(rows.Skip(i).Take(ReportConstants.MaxTableRowsPerPage).ToList());
        if (rowChunks.Count == 0)
            rowChunks.Add(new List<AssignmentSummaryRow>());

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

                    page.Header().Element(c => ReportHeaderFooter.ComposeHeader(c,
                        isArabic ? "تقرير ملخص التعيينات" : "Assignment Summary Report",
                        $"{L("Period")}: {from:yyyy-MM-dd} {L("To")} {to:yyyy-MM-dd}  |  {L("TotalAssignments")}: {ArabicFormattingService.FormatNumber(totalAssignments)}" +
                        (rowChunks.Count > 1 ? $"  |  Page {pageIdx + 1}/{rowChunks.Count}" : ""),
                        retentionYears,
                        isArabic,
                        logoPath));

                    page.Content().PaddingTop(10).Column(col =>
                    {
                        if (pageIdx == 0)
                        {
                            col.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, 
                                "Status Breakdown", "تفصيل الحالة", isArabic, "📊"));
                            
                            // First row: 3 KPI cards
                            col.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border)
                                .Padding(15).Row(row =>
                                {
                                    row.RelativeItem().Column(c =>
                                    {
                                        c.Item().Element(el => ProfessionalReportTemplate.RenderKpiCard(el, 
                                            "Pending", "معلق", ArabicFormattingService.FormatNumber(pending), "", isArabic, ProfessionalReportTemplate.Colors.Warning));
                                    });
                                    row.RelativeItem().Column(c =>
                                    {
                                        c.Item().Element(el => ProfessionalReportTemplate.RenderKpiCard(el, 
                                            "In Progress", "قيد التقدم", ArabicFormattingService.FormatNumber(inProgress), "", isArabic, ProfessionalReportTemplate.Colors.Primary));
                                    });
                                    row.RelativeItem().Column(c =>
                                    {
                                        c.Item().Element(el => ProfessionalReportTemplate.RenderKpiCard(el, 
                                            "Completed", "مكتمل", ArabicFormattingService.FormatNumber(completed), "✓", isArabic, ProfessionalReportTemplate.Colors.Success));
                                    });
                                });
                            
                            // Second row: 2 KPI cards
                            col.Item().PaddingTop(8).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border)
                                .Padding(15).Row(row =>
                                {
                                    row.RelativeItem().Column(c =>
                                    {
                                        c.Item().Element(el => ProfessionalReportTemplate.RenderKpiCard(el, 
                                            "Overdue", "متأخر", ArabicFormattingService.FormatNumber(overdue), overdue > 0 ? "⚠" : "✓", 
                                            isArabic, overdue > 0 ? ProfessionalReportTemplate.Colors.Error : ProfessionalReportTemplate.Colors.Success));
                                    });
                                    row.RelativeItem().Column(c =>
                                    {
                                        c.Item().Element(el => ProfessionalReportTemplate.RenderKpiCard(el, 
                                            "Completion Rate", "معدل الإنجاز", ArabicFormattingService.FormatPercentage(completionRate), "", isArabic, ProfessionalReportTemplate.Colors.Primary));
                                    });
                                    // Empty space for visual balance
                                    row.RelativeItem().Column(c => { });
                                });
                            col.Item().PaddingTop(16);
                        }
                        col.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, 
                            pageIdx == 0 ? "By User" : "By User (continued)",
                            pageIdx == 0 ? "حسب المستخدم" : "حسب المستخدم (تابع)",
                            isArabic, "👤"));
                        
                        col.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.5f);
                                columns.ConstantColumn(60);
                                columns.ConstantColumn(60);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(60);
                                columns.ConstantColumn(55);
                            });
                            table.Header(header =>
                            {
                                header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6)
                                    .Text(isArabic ? "المستخدم" : "User")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                                header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6).AlignRight()
                                    .Text(isArabic ? "تم التعيين" : "Assigned")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                                header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6).AlignRight()
                                    .Text(isArabic ? "مكتمل" : "Completed")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                                header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6).AlignRight()
                                    .Text(isArabic ? "معلق" : "Pending")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                                header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6).AlignRight()
                                    .Text(isArabic ? "قيد التقدم" : "In Prog")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                                header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6).AlignRight()
                                    .Text(isArabic ? "تم" : "Done")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                                header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6).AlignRight()
                                    .Text(isArabic ? "متأخر" : "Overdue")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                                header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(6).AlignRight()
                                    .Text(isArabic ? "المعدل %" : "Rate %")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9)).Bold().FontColor(QuestPDF.Helpers.Colors.White);
                            });
                            foreach (var r in chunk)
                            {
                                table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                    .Text(r.DisplayName ?? r.Username).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                                table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                    .AlignRight().Text(ArabicFormattingService.FormatNumber(r.AssignedInPeriod))
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                                table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                    .AlignRight().Text(ArabicFormattingService.FormatNumber(r.CompletedInPeriod))
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                                table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                    .AlignRight().Text(ArabicFormattingService.FormatNumber(r.Pending))
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                                table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                    .AlignRight().Text(ArabicFormattingService.FormatNumber(r.InProgress))
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                                table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                    .AlignRight().Text(ArabicFormattingService.FormatNumber(r.Completed))
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                                table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                    .AlignRight().Text(r.Overdue > 0 ? ArabicFormattingService.FormatNumber(r.Overdue) : "-")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9))
                                    .FontColor(r.Overdue > 0 ? ProfessionalReportTemplate.Colors.Error : ProfessionalReportTemplate.Colors.TextPrimary);
                                table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(6)
                                    .AlignRight().Text(ArabicFormattingService.FormatPercentage(r.CompletionRate))
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                            }
                        });
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

    public static string GenerateExcel(IDocumentAssignmentStore assignmentStore, IUserStore userStore, DateTime from, DateTime to,
        string? filePath = null)
    {
        var rows = GetData(assignmentStore, userStore, from, to);
        var path = filePath ?? Path.Combine(Path.GetTempPath(), $"WorkAudit_AssignmentSummary_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Assignment Summary");

        ws.Cell(1, 1).Value = "Assignment Summary Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, 9).Merge();
        ws.Cell(2, 1).Value = $"Period: {from:yyyy-MM-dd} to {to:yyyy-MM-dd}";
        ws.Cell(3, 1).Value = "Generated: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC";

        ws.Cell(5, 1).Value = "User";
        ws.Cell(5, 2).Value = "Assigned (period)";
        ws.Cell(5, 3).Value = "Completed (period)";
        ws.Cell(5, 4).Value = "Pending";
        ws.Cell(5, 5).Value = "In Progress";
        ws.Cell(5, 6).Value = "Completed";
        ws.Cell(5, 7).Value = "Overdue";
        ws.Cell(5, 8).Value = "Completion %";
        ws.Range(5, 1, 5, 8).Style.Font.Bold = true;

        var rowNum = 6;
        foreach (var r in rows)
        {
            ws.Cell(rowNum, 1).Value = r.DisplayName ?? r.Username;
            ws.Cell(rowNum, 2).Value = r.AssignedInPeriod;
            ws.Cell(rowNum, 3).Value = r.CompletedInPeriod;
            ws.Cell(rowNum, 4).Value = r.Pending;
            ws.Cell(rowNum, 5).Value = r.InProgress;
            ws.Cell(rowNum, 6).Value = r.Completed;
            ws.Cell(rowNum, 7).Value = r.Overdue;
            ws.Cell(rowNum, 8).Value = (double)r.CompletionRate;
            rowNum++;
        }

        ws.Columns().AdjustToContents();
        workbook.SaveAs(path);
        return path;
    }
}

public class AssignmentSummaryRow
{
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public int AssignedInPeriod { get; set; }
    public int CompletedInPeriod { get; set; }
    public int Pending { get; set; }
    public int InProgress { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int Overdue { get; set; }
    public decimal CompletionRate { get; set; }
}
