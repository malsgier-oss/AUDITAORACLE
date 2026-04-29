using System.IO;
using System.Globalization;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkAudit.Core.Reports.ReportTemplates;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// User activity report: documents created/reviewed per user, productivity metrics.
/// </summary>
public static class UserActivityReport
{
    private const int MaxDocuments = 50_000;

    public static List<UserActivityRow> GetData(IDocumentStore documentStore, IUserStore userStore, IDocumentAssignmentStore? assignmentStore, DateTime from, DateTime to,
        string? branch = null, string? section = null, string? engagement = null)
    {
        var fromStr = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toStr = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59";
        var users = userStore.ListUsers(isActive: true);
        var days = Math.Max(1, (to - from).Days + 1);

        var rows = new List<UserActivityRow>();

        foreach (var user in users)
        {
            var created = documentStore.ListDocuments(dateFrom: fromStr, dateTo: toStr, branch: branch, section: section, engagement: engagement,
                createdBy: user.Username, limit: MaxDocuments);
            var reviewed = documentStore.ListDocuments(dateFrom: fromStr, dateTo: toStr, branch: branch, section: section, engagement: engagement,
                reviewedBy: user.Username, limit: MaxDocuments);

            var createdIds = created.Select(d => d.Id).ToHashSet();
            var reviewedIds = reviewed.Select(d => d.Id).ToHashSet();
            var reviewedOnly = reviewed.Count(d => !createdIds.Contains(d.Id));
            var cleared = created.Count(d => d.Status == Enums.Status.Cleared) + reviewed.Count(d => d.Status == Enums.Status.Cleared && !createdIds.Contains(d.Id));
            var totalTouched = created.Count + reviewedOnly;

            var assigned = 0; var completed = 0; var pending = 0; var overdue = 0;
            if (assignmentStore != null)
            {
                var userAssignments = assignmentStore.ListByUser(user.Id, null, false);
                foreach (var a in userAssignments)
                {
                    if (string.Compare(a.AssignedAt, fromStr, StringComparison.Ordinal) >= 0 && string.Compare(a.AssignedAt, toStr, StringComparison.Ordinal) <= 0)
                        assigned++;
                    if (a.Status == AssignmentStatus.Completed && !string.IsNullOrEmpty(a.CompletedAt) && string.Compare(a.CompletedAt, fromStr, StringComparison.Ordinal) >= 0 && string.Compare(a.CompletedAt, toStr, StringComparison.Ordinal) <= 0)
                        completed++;
                    if (a.Status == AssignmentStatus.Pending) pending++;
                    if (a.Status is AssignmentStatus.Pending or AssignmentStatus.InProgress && !string.IsNullOrEmpty(a.DueDate) && DateTime.TryParse(a.DueDate, out var due) && due.Date < DateTime.Today)
                        overdue++;
                }
            }

            rows.Add(new UserActivityRow
            {
                Username = user.Username,
                DisplayName = user.DisplayName,
                Branch = user.Branch ?? "-",
                Created = created.Count,
                Reviewed = reviewedOnly,
                TotalTouched = totalTouched,
                Cleared = cleared,
                Throughput = totalTouched > 0 ? (decimal)totalTouched / days : 0,
                ClearingRate = totalTouched > 0 ? (decimal)cleared / totalTouched * 100 : 0,
                Assigned = assigned,
                Completed = completed,
                Pending = pending,
                Overdue = overdue
            });
        }

        return rows.OrderByDescending(r => r.TotalTouched).ToList();
    }

    public static string GeneratePdf(IDocumentStore documentStore, IUserStore userStore, IDocumentAssignmentStore? assignmentStore, DateTime from, DateTime to,
        string? branch = null, string? section = null, string? username = null, string? filePath = null, bool includeCharts = true, int retentionYears = 7, ReportWatermark watermark = ReportWatermark.None, string? engagement = null, IConfigStore? configStore = null, string language = "en")
    {
        var isArabic = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var rows = GetData(documentStore, userStore, assignmentStore, from, to, branch, section, engagement);
        if (!string.IsNullOrEmpty(username))
            rows = rows.Where(r => r.Username == username).ToList();

        var total = rows.Sum(r => r.TotalTouched);
        var path = filePath ?? Path.Combine(Path.GetTempPath(), $"WorkAudit_UserActivity_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf");

        QuestPDF.Settings.License = LicenseType.Community;

        var L = (string key) => ReportLocalizationService.GetString(key, configStore);
        var logoPath = ReportHeaderFooter.GetLogoPath(configStore);

        var rowChunks = new List<List<UserActivityRow>>();
        for (var i = 0; i < rows.Count; i += ReportConstants.MaxTableRowsPerPage)
            rowChunks.Add(rows.Skip(i).Take(ReportConstants.MaxTableRowsPerPage).ToList());
        if (rowChunks.Count == 0)
            rowChunks.Add(new List<UserActivityRow>());

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
                        isArabic ? "تقرير نشاط المستخدم" : "User Activity Report",
                        $"{L("Period")}: {from:yyyy-MM-dd} {L("To")} {to:yyyy-MM-dd}  |  {L("TotalDocuments")}: {ArabicFormattingService.FormatNumber(total)}" +
                        (rowChunks.Count > 1 ? $"  |  Page {pageIdx + 1}/{rowChunks.Count}" : ""),
                        retentionYears,
                        isArabic,
                        logoPath));

                    page.Content().PaddingTop(10).Column(col =>
                    {
                        col.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, 
                            pageIdx == 0 ? "By User" : "By User (continued)",
                            pageIdx == 0 ? "حسب المستخدم" : "حسب المستخدم (تابع)",
                            isArabic, "👤"));
                        
                        col.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.2f);
                                columns.RelativeColumn(0.8f);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(60);
                            });
                            table.Header(header =>
                            {
                                header.Cell().BorderBottom(1).Padding(4).Text("User").Bold();
                                header.Cell().BorderBottom(1).Padding(4).Text("Branch").Bold();
                                header.Cell().BorderBottom(1).Padding(4).AlignRight().Text("Created").Bold();
                                header.Cell().BorderBottom(1).Padding(4).AlignRight().Text("Reviewed").Bold();
                                header.Cell().BorderBottom(1).Padding(4).AlignRight().Text("Total").Bold();
                                header.Cell().BorderBottom(1).Padding(4).AlignRight().Text("Assigned").Bold();
                                header.Cell().BorderBottom(1).Padding(4).AlignRight().Text("Completed").Bold();
                                header.Cell().BorderBottom(1).Padding(4).AlignRight().Text("Overdue").Bold();
                                header.Cell().BorderBottom(1).Padding(4).AlignRight().Text("Throughput").Bold();
                            });
                            foreach (var r in chunk)
                            {
                                table.Cell().BorderBottom(0.5f).Padding(4).Text(r.DisplayName ?? r.Username);
                                table.Cell().BorderBottom(0.5f).Padding(4).Text(r.Branch);
                                table.Cell().BorderBottom(0.5f).Padding(4).AlignRight().Text(ArabicFormattingService.FormatNumber(r.Created));
                                table.Cell().BorderBottom(0.5f).Padding(4).AlignRight().Text(ArabicFormattingService.FormatNumber(r.Reviewed));
                                table.Cell().BorderBottom(0.5f).Padding(4).AlignRight().Text(ArabicFormattingService.FormatNumber(r.TotalTouched));
                                table.Cell().BorderBottom(0.5f).Padding(4).AlignRight().Text(ArabicFormattingService.FormatNumber(r.Assigned));
                                table.Cell().BorderBottom(0.5f).Padding(4).AlignRight().Text(ArabicFormattingService.FormatNumber(r.Completed));
                                table.Cell().BorderBottom(0.5f).Padding(4).AlignRight().Text(ArabicFormattingService.FormatNumber(r.Overdue));
                                table.Cell().BorderBottom(0.5f).Padding(4).AlignRight().Text(ArabicFormattingService.FormatDecimal(r.Throughput, 1) + "/day");
                            }
                        });
                        if (pageIdx == 0)
                            col.Item().Element(c => ReportCharts.ComposeSummaryBarChart(c, rows.Select(r => (r.DisplayName ?? r.Username, r.TotalTouched)).ToList(), "Documents Touched by User", includeCharts));
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

    public static string GenerateExcel(IDocumentStore documentStore, IUserStore userStore, IDocumentAssignmentStore? assignmentStore, DateTime from, DateTime to,
        string? branch = null, string? section = null, string? username = null, string? filePath = null, string? engagement = null)
    {
        var rows = GetData(documentStore, userStore, assignmentStore, from, to, branch, section, engagement);
        if (!string.IsNullOrEmpty(username))
            rows = rows.Where(r => r.Username == username).ToList();

        var path = filePath ?? Path.Combine(Path.GetTempPath(), $"WorkAudit_UserActivity_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("User Activity");

        ws.Cell(1, 1).Value = "User Activity Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, 9).Merge();
        ws.Cell(2, 1).Value = $"Period: {from:yyyy-MM-dd} to {to:yyyy-MM-dd}";
        ws.Cell(3, 1).Value = "Generated: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

        ws.Cell(5, 1).Value = "User";
        ws.Cell(5, 2).Value = "Branch";
        ws.Cell(5, 3).Value = "Created";
        ws.Cell(5, 4).Value = "Reviewed";
        ws.Cell(5, 5).Value = "Total";
        ws.Cell(5, 6).Value = "Assigned";
        ws.Cell(5, 7).Value = "Completed";
        ws.Cell(5, 8).Value = "Overdue";
        ws.Cell(5, 9).Value = "Throughput/day";
        ws.Range(5, 1, 5, 9).Style.Font.Bold = true;

        var rowNum = 6;
        foreach (var r in rows)
        {
            ws.Cell(rowNum, 1).Value = r.DisplayName ?? r.Username;
            ws.Cell(rowNum, 2).Value = r.Branch;
            ws.Cell(rowNum, 3).Value = r.Created;
            ws.Cell(rowNum, 4).Value = r.Reviewed;
            ws.Cell(rowNum, 5).Value = r.TotalTouched;
            ws.Cell(rowNum, 6).Value = r.Assigned;
            ws.Cell(rowNum, 7).Value = r.Completed;
            ws.Cell(rowNum, 8).Value = r.Overdue;
            ws.Cell(rowNum, 9).Value = (double)r.Throughput;
            rowNum++;
        }

        ws.Columns().AdjustToContents();
        workbook.SaveAs(path);
        return path;
    }
}

public class UserActivityRow
{
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Branch { get; set; } = "";
    public int Created { get; set; }
    public int Reviewed { get; set; }
    public int TotalTouched { get; set; }
    public int Cleared { get; set; }
    public decimal Throughput { get; set; }
    public decimal ClearingRate { get; set; }
    public int Assigned { get; set; }
    public int Completed { get; set; }
    public int Pending { get; set; }
    public int Overdue { get; set; }
}
