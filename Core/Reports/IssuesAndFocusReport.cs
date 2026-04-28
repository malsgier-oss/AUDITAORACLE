using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkAudit.Core.Reports.ReportTemplates;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Issues and focus report: issues fixed, issues still outstanding, problem areas, and suggestions.
/// Professional template with RTL support and bilingual capabilities.
/// </summary>
public static class IssuesAndFocusReport
{
    private const int MaxDocuments = 50_000;

    public static string GeneratePdf(IDocumentStore store, IAuditLogStore auditStore, IDocumentAssignmentStore? assignmentStore, DateTime from, DateTime to,
        string? branch = null, string? section = null, string? filePath = null, int retentionYears = 7, IReportAnomalyService? anomalyService = null, ReportWatermark watermark = ReportWatermark.None, string? engagement = null, IConfigStore? configStore = null, string language = "en")
    {
        var isArabic = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd") + "T23:59:59";

        // Issues still (documents currently in Issue status)
        var docsInPeriod = store.ListDocuments(dateFrom: fromStr, dateTo: toStr, branch: branch, section: section, status: null, engagement: engagement, limit: MaxDocuments);
        var issuesStill = docsInPeriod.Where(d => d.Status == Enums.Status.Issue).ToList();
        var issuesStillByBranch = issuesStill.GroupBy(d => string.IsNullOrEmpty(d.Branch) ? "(No Branch)" : d.Branch).ToDictionary(g => g.Key, g => g.Count());
        var issuesStillBySection = issuesStill.GroupBy(d => string.IsNullOrEmpty(d.Section) ? "(No Section)" : d.Section).ToDictionary(g => g.Key, g => g.Count());
        var branchDisplay = issuesStillByBranch.OrderByDescending(x => x.Value).Take(ReportConstants.MaxIssuesByBranchSectionItems).ToList();
        var sectionDisplay = issuesStillBySection.OrderByDescending(x => x.Value).Take(ReportConstants.MaxIssuesByBranchSectionItems).ToList();
        var listTruncated = issuesStillByBranch.Count > ReportConstants.MaxIssuesByBranchSectionItems || issuesStillBySection.Count > ReportConstants.MaxIssuesByBranchSectionItems;

        // Issues fixed: documents that moved from Issue to Cleared (from audit log)
        var auditEntries = auditStore.Query(from, to, null, AuditAction.DocumentStatusChanged, null, false, 5000, 0);
        var issuesFixed = auditEntries.Count(e => e.NewValue?.Contains(Enums.Status.Cleared) == true && e.OldValue?.Contains(Enums.Status.Issue) == true);

        // Cleared in period
        var clearedInPeriod = docsInPeriod.Count(d => d.Status == Enums.Status.Cleared);
        var totalActive = docsInPeriod.Count(d => d.Status != Enums.Status.Archived);
        var clearingRate = totalActive > 0 ? (decimal)clearedInPeriod / totalActive * 100 : 0;

        var path = filePath ?? Path.Combine(Path.GetTempPath(), $"WorkAudit_IssuesAndFocus_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf");

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
                    isArabic ? "تقرير المشكلات والتركيز" : "Issues & Focus Report",
                    $"{L("Period")}: {from:yyyy-MM-dd} {L("To")} {to:yyyy-MM-dd}",
                    retentionYears,
                    isArabic,
                    logoPath));

                page.Content().PaddingTop(10).Column(column =>
                {
                    // Issues Fixed KPI
                    column.Item().Element(c => ProfessionalReportTemplate.RenderKpiCard(c, 
                        "Issues Fixed (this period)", "المشكلات المحلولة (هذه الفترة)", 
                        ArabicFormattingService.FormatNumber(issuesFixed), "✓", isArabic, ProfessionalReportTemplate.Colors.Success));

                    column.Item().PaddingTop(12);

                    // Issues Still Outstanding KPI
                    column.Item().Element(c => ProfessionalReportTemplate.RenderKpiCard(c, 
                        "Issues Still Outstanding", "المشكلات المعلقة", 
                        ArabicFormattingService.FormatNumber(issuesStill.Count), 
                        issuesStill.Count > 10 ? "⚠" : "✓", isArabic, 
                        issuesStill.Count > 10 ? ProfessionalReportTemplate.Colors.Warning : ProfessionalReportTemplate.Colors.Success));

                    column.Item().PaddingTop(16);

                    // Issues by Branch section
                    if (branchDisplay.Count > 0)
                    {
                        column.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, 
                            "Issues by Branch", "المشكلات حسب الفرع", isArabic, "📊"));
                        
                        column.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border)
                            .Padding(15).Column(branchCol =>
                            {
                                branchCol.Spacing(6);
                                foreach (var kv in branchDisplay)
                                {
                                    branchCol.Item().Row(row =>
                                    {
                                        row.AutoItem().Width(4).Background(ProfessionalReportTemplate.Colors.Error);
                                        row.RelativeItem().PaddingLeft(8).Row(innerRow =>
                                        {
                                            innerRow.RelativeItem().Text(kv.Key)
                                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10)).Bold();
                                            innerRow.AutoItem().Text(ArabicFormattingService.FormatNumber(kv.Value))
                                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                                .FontColor(ProfessionalReportTemplate.Colors.Error).Bold();
                                        });
                                    });
                                }
                            });
                        column.Item().PaddingTop(12);
                    }

                    // Issues by Section
                    if (sectionDisplay.Count > 0)
                    {
                        column.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, 
                            "Issues by Section", "المشكلات حسب القسم", isArabic, "📊"));
                        
                        column.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border)
                            .Padding(15).Column(sectionCol =>
                            {
                                sectionCol.Spacing(6);
                                foreach (var kv in sectionDisplay)
                                {
                                    sectionCol.Item().Row(row =>
                                    {
                                        row.AutoItem().Width(4).Background(ProfessionalReportTemplate.Colors.Warning);
                                        row.RelativeItem().PaddingLeft(8).Row(innerRow =>
                                        {
                                            innerRow.RelativeItem().Text(kv.Key)
                                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10)).Bold();
                                            innerRow.AutoItem().Text(ArabicFormattingService.FormatNumber(kv.Value))
                                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                                .FontColor(ProfessionalReportTemplate.Colors.Warning).Bold();
                                        });
                                    });
                                }
                            });
                    }

                    if (listTruncated)
                    {
                        column.Item().PaddingTop(6).Text(isArabic 
                            ? $"عرض أعلى {ReportConstants.MaxIssuesByBranchSectionItems} فروع/أقسام حسب عدد المشكلات."
                            : $"Showing top {ReportConstants.MaxIssuesByBranchSectionItems} branches/sections by issue count.")
                            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9))
                            .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                    }

                    column.Item().PaddingTop(16).Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, 
                        "What's Better", "ما تحسن", isArabic, "✓"));
                    
                    column.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border)
                        .Padding(15).Column(betterCol =>
                        {
                            betterCol.Spacing(8);
                            betterCol.Item().Row(row =>
                            {
                                row.AutoItem().Width(4).Background(ProfessionalReportTemplate.Colors.Success);
                                row.RelativeItem().PaddingLeft(8).Text($"Clearing rate: {ArabicFormattingService.FormatPercentage(clearingRate)}")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10));
                            });
                            
                            if (issuesFixed > 0)
                            {
                                betterCol.Item().Row(row =>
                                {
                                    row.AutoItem().Width(4).Background(ProfessionalReportTemplate.Colors.Success);
                                    row.RelativeItem().PaddingLeft(8).Text(isArabic
                                        ? $"{ArabicFormattingService.FormatNumber(issuesFixed)} مشكلة تم حلها في هذه الفترة"
                                        : $"{ArabicFormattingService.FormatNumber(issuesFixed)} issues resolved this period")
                                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10));
                                });
                            }
                        });

                    // Anomalies section
                    if (anomalyService != null)
                    {
                        var anomalies = anomalyService.GetAnomalies(store, from, to, branch, section, engagement);
                        if (anomalies.Count > 0)
                        {
                            column.Item().PaddingTop(16).Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, 
                                "Anomalies Detected", "الشذوذات المكتشفة", isArabic, "⚠️"));
                            
                            column.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Warning)
                                .Padding(15).Column(anomalyCol =>
                                {
                                    anomalyCol.Spacing(6);
                                    foreach (var a in anomalies.Take(10))
                                    {
                                        anomalyCol.Item().Row(row =>
                                        {
                                            row.AutoItem().Width(4).Background(ProfessionalReportTemplate.Colors.Warning);
                                            row.RelativeItem().PaddingLeft(8).Text($"{a.EntityName} — {a.Reason}")
                                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9))
                                                .FontColor(ProfessionalReportTemplate.Colors.Warning);
                                        });
                                    }
                                });
                        }
                    }

                    column.Item().PaddingTop(16).Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, 
                        "What's a Problem", "ما هو المشكلة", isArabic, "🔴"));
                    
                    column.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border)
                        .Padding(15).Column(problemCol =>
                        {
                            problemCol.Spacing(8);
                            if (issuesStill.Count > 0)
                            {
                                problemCol.Item().Row(row =>
                                {
                                    row.AutoItem().Width(4).Background(ProfessionalReportTemplate.Colors.Error);
                                    row.RelativeItem().PaddingLeft(8).Text(isArabic
                                        ? $"{ArabicFormattingService.FormatNumber(issuesStill.Count)} وثائق لا تزال في حالة مشكلة"
                                        : $"{ArabicFormattingService.FormatNumber(issuesStill.Count)} documents still in Issue status")
                                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10));
                                });
                            }
                            else
                            {
                                problemCol.Item().Row(row =>
                                {
                                    row.AutoItem().Width(4).Background(ProfessionalReportTemplate.Colors.Success);
                                    row.RelativeItem().PaddingLeft(8).Text(isArabic ? "لا توجد مشكلات معلقة" : "No outstanding issues")
                                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10));
                                });
                            }
                            
                            if (assignmentStore != null)
                            {
                                var overdueAssignments = assignmentStore.ListAll(null, null)
                                    .Count(a => a.Status is AssignmentStatus.Pending or AssignmentStatus.InProgress
                                        && !string.IsNullOrEmpty(a.DueDate) && DateTime.TryParse(a.DueDate, out var d) && d.Date < DateTime.Today);
                                if (overdueAssignments > 0)
                                {
                                    problemCol.Item().Row(row =>
                                    {
                                        row.AutoItem().Width(4).Background(ProfessionalReportTemplate.Colors.Error);
                                        row.RelativeItem().PaddingLeft(8).Text(ArabicFormattingService.FormatNumber(overdueAssignments) + 
                                            (isArabic ? " تعيينات متأخرة — يتطلب الاهتمام" : " assignment(s) overdue — requires attention"))
                                            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                            .FontColor(ProfessionalReportTemplate.Colors.Error);
                                    });
                                }
                            }
                        });

                    column.Item().PaddingTop(16).Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, 
                        "Suggestions / Focus", "الاقتراحات / التركيز", isArabic, "💡"));
                    
                    column.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border)
                        .Padding(15).Column(suggestionCol =>
                        {
                            suggestionCol.Spacing(8);
                            if (issuesStill.Count > 0)
                            {
                                var topBranch = issuesStillByBranch.OrderByDescending(x => x.Value).FirstOrDefault();
                                var topSection = issuesStillBySection.OrderByDescending(x => x.Value).FirstOrDefault();
                                if (topBranch.Value > 0)
                                {
                                    suggestionCol.Item().Row(row =>
                                    {
                                        row.AutoItem().Width(4).Background(ProfessionalReportTemplate.Colors.Primary);
                                        row.RelativeItem().PaddingLeft(8).Text(isArabic
                                            ? $"إعطاء الأولوية لحل المشكلات في {topBranch.Key} ({ArabicFormattingService.FormatNumber(topBranch.Value)} معلقة)"
                                            : $"Prioritize issue resolution in {topBranch.Key} ({ArabicFormattingService.FormatNumber(topBranch.Value)} outstanding)")
                                            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10));
                                    });
                                }
                                if (topSection.Value > 0)
                                {
                                    suggestionCol.Item().Row(row =>
                                    {
                                        row.AutoItem().Width(4).Background(ProfessionalReportTemplate.Colors.Primary);
                                        row.RelativeItem().PaddingLeft(8).Text(isArabic
                                            ? $"التركيز على قسم {topSection.Key} ({ArabicFormattingService.FormatNumber(topSection.Value)} معلقة)"
                                            : $"Focus on {topSection.Key} section ({ArabicFormattingService.FormatNumber(topSection.Value)} outstanding)")
                                            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10));
                                    });
                                }
                            }
                            else
                            {
                                suggestionCol.Item().Row(row =>
                                {
                                    row.AutoItem().Width(4).Background(ProfessionalReportTemplate.Colors.Success);
                                    row.RelativeItem().PaddingLeft(8).Text(isArabic ? "الحفاظ على معدل التصفية الحالي" : "Maintain current clearing rate")
                                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10));
                                });
                            }
                        });
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
