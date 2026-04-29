using System.IO;
using System.Linq;
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
/// Executive Summary: Professional, boardroom-quality report for senior management and shareholders.
/// Includes cover page, TOC, strategic insights, comparative analysis, and full RTL/Arabic support.
/// </summary>
public static class ExecutiveSummaryReport
{
    private const int MaxDocuments = 50_000;

    public static string GeneratePdf(IDocumentStore store, IAuditLogStore auditStore, DateTime from, DateTime to,
        string? branch = null, string? section = null, string? filePath = null, bool includeCharts = true, 
        int retentionYears = 7, IConfigStore? configStore = null, ReportWatermark watermark = ReportWatermark.None, 
        string? engagement = null, string language = "en",
        bool includeTableOfContents = true, bool includeBranding = true, bool includeDisclaimer = true)
    {
        var isArabic = language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        var fromStr = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toStr = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59";
        var docs = store.ListDocuments(dateFrom: fromStr, dateTo: toStr, branch: branch, section: section, engagement: engagement, limit: MaxDocuments);

        // Load notes for all documents
        var notesStore = ServiceContainer.GetOptionalService<INotesStore>();
        var docIds = docs.Select(d => d.Id).ToList();
        var notesByDoc = new Dictionary<int, List<Note>>();

        if (notesStore != null)
        {
            foreach (var docId in docIds)
            {
                var notes = notesStore.GetByDocumentId(docId);
                if (notes.Any())
                    notesByDoc[docId] = notes;
            }
        }

        var comparative = ServiceContainer.GetOptionalService<IComparativeAnalysisService>();
        ComparisonResult? yoy = null, qoq = null, mom = null;
        if (comparative != null)
        {
            yoy = comparative.CompareYearOverYear(from, to, branch, section, engagement);
            qoq = comparative.CompareQuarterOverQuarter(from, to, branch, section, engagement);
            mom = comparative.CompareMonthOverMonth(from, to, branch, section, engagement);
        }

        var branchDict = docs
            .GroupBy(d => string.IsNullOrEmpty(d.Branch) ? "(No Branch)" : d.Branch)
            .ToDictionary(g => g.Key, g => g.Count());

        var criticalNotes = notesByDoc.Values.SelectMany(n => n)
            .Where(n => n.Severity == NoteSeverity.Critical || n.Severity == NoteSeverity.High)
            .ToList();

        // Calculate metrics
        var total = docs.Count;
        var byBranch = docs.GroupBy(d => string.IsNullOrEmpty(d.Branch) ? "(No Branch)" : d.Branch)
            .OrderByDescending(g => g.Count()).Take(5).Select(g => (g.Key, g.Count())).ToList();
        var bySection = docs.GroupBy(d => string.IsNullOrEmpty(d.Section) ? "(No Section)" : d.Section)
            .OrderByDescending(g => g.Count()).Take(5).Select(g => (g.Key, g.Count())).ToList();
        var cleared = docs.Count(d => d.Status == Enums.Status.Cleared);
        var issue = docs.Count(d => d.Status == Enums.Status.Issue);
        var active = docs.Count(d => d.Status != Enums.Status.Archived);
        var clearingRate = active > 0 ? (decimal)cleared / active * 100 : 0;
        var issueRate = total > 0 ? (decimal)issue / total * 100 : 0;
        var days = Math.Max(1, (to - from).Days + 1);
        var throughput = (decimal)total / days;

        var auditEntries = auditStore.Query(from, to, null, AuditAction.DocumentStatusChanged, null, false, 5000, 0);
        var issuesFixed = auditEntries.Count(e => e.NewValue?.Contains(Enums.Status.Cleared) == true && e.OldValue?.Contains(Enums.Status.Issue) == true);
        var issuesStill = docs.Count(d => d.Status == Enums.Status.Issue);
        var issuesByBranch = docs.Where(d => d.Status == Enums.Status.Issue)
            .GroupBy(d => string.IsNullOrEmpty(d.Branch) ? "(No Branch)" : d.Branch)
            .OrderByDescending(g => g.Count()).Take(3).Select(g => (Name: g.Key, Count: g.Count())).ToList();

        // Status distribution for pie chart
        var statusDist = new List<(string Label, int Value, string Color)>
        {
            ("Cleared", cleared, ProfessionalReportTemplate.Colors.Success),
            ("Issue", issue, ProfessionalReportTemplate.Colors.Error),
            ("In Review", docs.Count(d => d.Status == Enums.Status.Reviewed || d.Status == Enums.Status.ReadyForAudit), ProfessionalReportTemplate.Colors.Warning),
            ("Draft", docs.Count(d => d.Status == Enums.Status.Draft), ProfessionalReportTemplate.Colors.TextSecondary)
        };

        // Prepare metadata
        var currentUser = ServiceContainer.GetService<AppConfiguration>();
        var reportId = $"EXEC-{from:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var metadata = new ProfessionalReportTemplate.ReportMetadata
        {
            ReportId = reportId,
            Title = "Executive Summary Report",
            TitleAr = "تقرير الملخص التنفيذي",
            Type = "Executive Summary",
            DateFrom = from,
            DateTo = to,
            GeneratedAt = DateTime.UtcNow,
            GeneratedBy = currentUser.CurrentUserName ?? "System",
            OrganizationName = ReportHeaderFooter.GetOrganizationName(configStore, false),
            OrganizationNameAr = ReportHeaderFooter.GetOrganizationName(configStore, true),
            ConfidentialityLevel = "CONFIDENTIAL",
            Version = "v1.0"
        };
        metadata.DistributionListSummary = configStore?.GetSettingValue(ReportBrandingConfiguration.ReportDefaultDistributionList, "") ?? "";
        metadata.SupersedesReportId = configStore?.GetSettingValue(ReportBrandingConfiguration.ReportSupersedesReportId, "") ?? "";

        // Get intelligence service for executive summary
        var intelligenceService = ServiceContainer.GetOptionalService<IIntelligenceService>();
        var issueObjects = docs.Where(d => d.Status == Enums.Status.Issue).Cast<object>().ToList();
        var executiveSummaryText = intelligenceService?.GenerateExecutiveSummary(docs, issueObjects, language)
            ?? GenerateFallbackSummary(total, clearingRate, issue, isArabic);

        var strategicNarrative = intelligenceService?.GenerateStrategicNarrative(docs, yoy, criticalNotes, language) ?? "";
        var strategicInsights = intelligenceService?.IdentifyStrategicInsights(docs, branchDict, language) ?? new List<StrategicInsight>();

        // Recommendations with priority color for left bar (action tracking)
        var recommendationLines = new List<(string Text, string BarColor)>();
        if (intelligenceService != null)
        {
            foreach (var r in intelligenceService.GenerateRecommendations(issueObjects, language))
                recommendationLines.Add((r, ProfessionalReportTemplate.Colors.Accent));

            foreach (var a in intelligenceService.GenerateExecutiveActions(issueObjects, language)
                         .OrderByDescending(x => x.Priority))
            {
                var c = a.Priority switch
                {
                    ExecutiveActionPriority.Critical => ProfessionalReportTemplate.Colors.Error,
                    ExecutiveActionPriority.High => ProfessionalReportTemplate.Colors.Warning,
                    ExecutiveActionPriority.Medium => ProfessionalReportTemplate.Colors.Accent,
                    _ => ProfessionalReportTemplate.Colors.Secondary
                };
                recommendationLines.Add(($"{a.Title} — {a.Description}", c));
            }
        }
        else
        {
            recommendationLines.Add((
                isArabic
                    ? "راجع وثائقك ذات المشكلات بأولوية"
                    : "Triage and resolve high-severity open items first; align owners to close gaps.",
                ProfessionalReportTemplate.Colors.Warning));
        }
        foreach (var s in strategicInsights.Take(3))
        {
            recommendationLines.Add((
                $"{s.Title} — {s.Description}",
                ProfessionalReportTemplate.Colors.Secondary));
        }

        // Get logo path (branding)
        var logoPath = includeBranding ? ReportHeaderFooter.GetLogoPath(configStore) : null;

        // Get disclaimer
        var disclaimerText = !includeDisclaimer
            ? ""
            : (configStore?.GetSettingValue(isArabic ? "report_disclaimer_text_ar" : "report_disclaimer_text_en", null)
               ?? ProfessionalReportTemplate.GetDefaultDisclaimer(isArabic, metadata.OrganizationName));

        var path = filePath ?? Path.Combine(Path.GetTempPath(), $"WorkAudit_ExecutiveSummary_{from:yyyyMMdd}_{to:yyyyMMdd}.pdf");

        QuestPDF.Settings.License = LicenseType.Community;

        var L = (string key) => ReportLocalizationService.GetString(key, configStore);

        // Define sections for TOC
        var sections = new List<ProfessionalReportTemplate.Section>
        {
            new() { Title = "Executive overview & strategic context", TitleAr = "نظرة تنفيذية وسياق استراتيجي", PageNumber = 3 },
            new() { Title = "Key Performance Indicators", TitleAr = "المؤشرات الرئيسية", PageNumber = 4 },
            new() { Title = "Branch Analysis", TitleAr = "تحليل الفروع", PageNumber = 5 },
            new() { Title = "Section Analysis", TitleAr = "تحليل الأقسام", PageNumber = 6 },
            new() { Title = "Issues Overview", TitleAr = "نظرة عامة على المشكلات", PageNumber = 7 },
            new() { Title = "Notes & Observations", TitleAr = "الملاحظات والمشاهدات", PageNumber = 8 },
            new() { Title = "Recommendations", TitleAr = "التوصيات", PageNumber = 9 }
        };

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(QuestPDF.Helpers.Colors.White);
                
                // Apply language settings
                ProfessionalReportTemplate.ApplyLanguageSettings(page, isArabic);
                page.DefaultTextStyle(ProfessionalReportTemplate.GetTextStyle(isArabic, 10));

                // Header
                page.Header().Element(c => ReportHeaderFooter.ComposeHeader(c,
                    L("ExecutiveSummary"),
                    $"{L("Period")}: {from:yyyy-MM-dd} {L("To")} {to:yyyy-MM-dd}  |  {L("TotalDocuments")}: {ArabicFormattingService.FormatNumber(total)}",
                    retentionYears,
                    isArabic,
                    logoPath));

                // Footer
                page.Footer().Element(c => ReportHeaderFooter.ComposeFooter(c, retentionYears, isArabic, reportId));

                // Watermark
                if (watermark != ReportWatermark.None)
                    page.Foreground().Element(c => ReportHeaderFooter.ComposeWatermark(c, watermark, isArabic));

                // Content
                page.Content().Column(col =>
                {
                    // PAGE 1: COVER PAGE
                    col.Item().PageBreak();
                    col.Item().Element(c => ProfessionalReportTemplate.RenderCoverPage(c, metadata, isArabic, logoPath));

                    // PAGE 2: TABLE OF CONTENTS (optional)
                    if (includeTableOfContents)
                    {
                        col.Item().PageBreak();
                        col.Item().Element(c => ProfessionalReportTemplate.RenderTableOfContents(c, sections, isArabic));
                    }

                    // PAGE: EXECUTIVE OVERVIEW
                    col.Item().PageBreak();
                    col.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, "Executive Overview", "نظرة تنفيذية عامة", isArabic, "📋"));
                    col.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border)
                        .Padding(20).Background(ProfessionalReportTemplate.Colors.Background)
                        .Column(overviewCol =>
                        {
                            overviewCol.Item().Text(executiveSummaryText)
                                .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 11))
                                .LineHeight(1.6f)
                                .FontColor(ProfessionalReportTemplate.Colors.TextPrimary);
                            if (!string.IsNullOrWhiteSpace(strategicNarrative))
                            {
                                overviewCol.Item().PaddingTop(10).Text(strategicNarrative)
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                    .LineHeight(1.55f)
                                    .FontColor(ProfessionalReportTemplate.Colors.TextPrimary);
                            }
                            var compText = ComparativePeriodSummaryText.Format(yoy, qoq, mom, isArabic);
                            if (!string.IsNullOrEmpty(compText))
                            {
                                overviewCol.Item().PaddingTop(8).Text(compText)
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                    .LineHeight(1.5f)
                                    .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                            }
                            if (strategicInsights.Count > 0)
                            {
                                overviewCol.Item().PaddingTop(10)
                                    .Text(isArabic ? "رؤى استراتيجية" : "Strategic insights")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10)).Bold();
                                foreach (var si in strategicInsights)
                                {
                                    overviewCol.Item().PaddingTop(4).Text($"\u2022 {si.Title}: {si.Description}")
                                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9))
                                        .LineHeight(1.45f)
                                        .FontColor(ProfessionalReportTemplate.Colors.TextPrimary);
                                }
                            }
                        });

                    // PAGE 4: KEY PERFORMANCE INDICATORS
                    col.Item().PageBreak();
                    col.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, "Key Performance Indicators", "المؤشرات الرئيسية", isArabic, "📊"));
                    
                    // First row: 2 KPI cards
                    col.Item().PaddingTop(12).Row(row =>
                    {
                        row.RelativeItem().PaddingRight(8).Element(c => 
                            ProfessionalReportTemplate.RenderKpiCard(c, "Total Documents", "إجمالي المستندات", 
                                ArabicFormattingService.FormatNumber(total), "", isArabic, ProfessionalReportTemplate.Colors.Primary));
                        
                        row.RelativeItem().PaddingLeft(8).Element(c => 
                            ProfessionalReportTemplate.RenderKpiCard(c, "Throughput", "الإنتاجية", 
                                ArabicFormattingService.FormatDecimal(throughput, 1) + "/day", "", isArabic, ProfessionalReportTemplate.Colors.Secondary));
                    });
                    
                    // Second row: 2 KPI cards
                    col.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem().PaddingRight(8).Element(c => 
                            ProfessionalReportTemplate.RenderKpiCard(c, "Clearing Rate", "معدل التصفية", 
                                ArabicFormattingService.FormatPercentage(clearingRate), "", isArabic, 
                                clearingRate >= 80 ? ProfessionalReportTemplate.Colors.Success : ProfessionalReportTemplate.Colors.Warning));
                        
                        row.RelativeItem().PaddingLeft(8).Element(c => 
                            ProfessionalReportTemplate.RenderKpiCard(c, "Issue Rate", "معدل المشكلات", 
                                ArabicFormattingService.FormatPercentage(issueRate), $"{issue} issues", isArabic, 
                                issueRate <= 5 ? ProfessionalReportTemplate.Colors.Success : ProfessionalReportTemplate.Colors.Error));
                    });

                    // Status Distribution Chart
                    if (includeCharts)
                    {
                        col.Item().PaddingTop(20).Element(c => ProfessionalChartService.RenderPieChart(c, statusDist, 
                            "Status Distribution", "توزيع الحالة", isArabic, includeCharts));
                    }

                    // PAGE 5: BRANCH ANALYSIS
                    if (byBranch.Count > 0)
                    {
                        col.Item().PageBreak();
                        col.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, "Branch Analysis", "تحليل الفروع", isArabic, "🏢"));
                        
                        if (includeCharts)
                        {
                            col.Item().PaddingTop(12).Element(c => ProfessionalChartService.RenderBarChart(c, byBranch, 
                                "Documents by Branch", "المستندات حسب الفرع", isArabic, includeCharts, 10));
                        }
                        else
                        {
                            RenderSimpleTable(col.Item(), byBranch, "Branch", "Documents", isArabic);
                        }
                    }

                    // PAGE 6: SECTION ANALYSIS
                    if (bySection.Count > 0)
                    {
                        col.Item().PageBreak();
                        col.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, "Section Analysis", "تحليل الأقسام", isArabic, "📑"));
                        
                        if (includeCharts)
                        {
                            col.Item().PaddingTop(12).Element(c => ProfessionalChartService.RenderBarChart(c, bySection, 
                                "Documents by Section", "المستندات حسب القسم", isArabic, includeCharts, 10));
                        }
                        else
                        {
                            RenderSimpleTable(col.Item(), bySection, "Section", "Documents", isArabic);
                        }
                    }

                    // PAGE 7: ISSUES OVERVIEW
                    col.Item().PageBreak();
                    col.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, "Issues Overview", "نظرة عامة على المشكلات", isArabic, "⚠️"));
                    
                    col.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border)
                        .Padding(15).Column(issueCol =>
                        {
                            issueCol.Item().Row(row =>
                            {
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text(L("FixedThisPeriod")).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                        .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                                    c.Item().PaddingTop(4).Text(ArabicFormattingService.FormatNumber(issuesFixed))
                                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 20))
                                        .Bold().FontColor(ProfessionalReportTemplate.Colors.Success);
                                });
                                
                                row.RelativeItem().PaddingLeft(20).Column(c =>
                                {
                                    c.Item().Text(L("StillOutstanding")).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                        .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                                    c.Item().PaddingTop(4).Text(ArabicFormattingService.FormatNumber(issuesStill))
                                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 20))
                                        .Bold().FontColor(issuesStill > 0 ? ProfessionalReportTemplate.Colors.Error : ProfessionalReportTemplate.Colors.Success);
                                });
                            });

                            if (issuesByBranch.Count > 0)
                            {
                                issueCol.Item().PaddingTop(16).Text(L("TopBranchesWithIssues"))
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 11))
                                    .Bold().FontColor(ProfessionalReportTemplate.Colors.TextPrimary);
                                
                                foreach (var (name, count) in issuesByBranch)
                                {
                                    issueCol.Item().PaddingTop(6).Row(row =>
                                    {
                                        row.AutoItem().Text("• ").FontSize(10);
                                        row.RelativeItem().Text($"{name}: {ArabicFormattingService.FormatNumber(count)}")
                                            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                            .FontColor(ProfessionalReportTemplate.Colors.TextPrimary);
                                    });
                                }
                            }
                        });

                    // PAGE 8: NOTES & OBSERVATIONS
                    col.Item().PageBreak();
                    col.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, "Notes & Observations", "الملاحظات والمشاهدات", isArabic, "📝"));
                    
                    if (notesByDoc.Any())
                    {
                        var totalNotes = notesByDoc.Sum(kvp => kvp.Value.Count);
                        col.Item().PaddingTop(12).Text($"{L("TotalNotesRecorded")}: {ArabicFormattingService.FormatNumber(totalNotes)}")
                            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 11))
                            .Bold().FontColor(ProfessionalReportTemplate.Colors.Primary);

                        // Show up to 15 documents with notes
                        foreach (var (docId, notes) in notesByDoc.Take(15))
                        {
                            var doc = docs.First(d => d.Id == docId);
                            col.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border)
                                .Padding(12).Column(nc =>
                                {
                                    // Document header
                                    nc.Item().Row(nr =>
                                    {
                                        nr.AutoItem().Text("📄 ").FontSize(10);
                                        nr.RelativeItem().Text(Path.GetFileName(doc.FilePath)).Bold()
                                            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10));
                                        nr.AutoItem().Text($" ({ArabicFormattingService.FormatNumber(notes.Count)} {L("Notes")})")
                                            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 8))
                                            .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                                    });

                                    // Notes (max 3 per document, ordered by severity)
                                    foreach (var note in notes.OrderByDescending(n => GetSeverityPriority(n.Severity)).Take(3))
                                    {
                                        nc.Item().PaddingTop(6).PaddingLeft(isArabic ? 0 : 16).PaddingRight(isArabic ? 16 : 0)
                                            .Column(noteItem =>
                                            {
                                                noteItem.Item().Row(nr =>
                                                {
                                                    nr.AutoItem().Text(note.TypeIcon + " ").FontSize(9);
                                                    nr.AutoItem()
                                                        .Text($"[{note.Severity}]")
                                                        .FontSize(8).FontColor(GetSeverityQuestPdfColor(note.Severity)).Bold();
                                                    nr.RelativeItem().Text(note.Content.Length > 120
                                                        ? note.Content.Substring(0, 120) + "..."
                                                        : note.Content)
                                                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                                                });

                                                noteItem.Item().PaddingTop(2).Text(
                                                    $"{note.CreatedBy} • {FormatDate(note.CreatedAt)}"
                                                ).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 7))
                                                .FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                                            });
                                    }

                                    if (notes.Count > 3)
                                    {
                                        nc.Item().PaddingLeft(16).PaddingTop(4).Text(
                                            $"... {ArabicFormattingService.FormatNumber(notes.Count - 3)} {L("AdditionalNotes")}"
                                        ).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 8))
                                        .Italic().FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                                    }
                                });
                        }

                        if (notesByDoc.Count > 15)
                        {
                            col.Item().PaddingTop(8).Text(
                                $"... {ArabicFormattingService.FormatNumber(notesByDoc.Count - 15)} {L("AdditionalDocuments")} with notes not shown"
                            ).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9))
                            .Italic().FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                        }
                    }
                    else
                    {
                        col.Item().PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border)
                            .Background(ProfessionalReportTemplate.Colors.Background).Padding(15)
                            .Text(L("NoNotesRecorded"))
                            .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                            .Italic().FontColor(ProfessionalReportTemplate.Colors.TextSecondary);
                    }

                    // PAGE 9: RECOMMENDATIONS (priority via bar color)
                    if (recommendationLines.Count > 0)
                    {
                        col.Item().PageBreak();
                        col.Item().Element(c => ProfessionalReportTemplate.RenderSectionDivider(c, "Recommendations", "التوصيات", isArabic, "💡"));
                        
                        foreach (var (text, barColor) in recommendationLines)
                        {
                            col.Item().PaddingTop(10).Row(row =>
                            {
                                row.ConstantItem(4).Height(20).Background(barColor);
                                row.RelativeItem()
                                    .PaddingLeft(isArabic ? 0 : 12)
                                    .PaddingRight(isArabic ? 12 : 0)
                                    .Text($"• {text}")
                                    .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                                    .LineHeight(1.5f)
                                    .FontColor(ProfessionalReportTemplate.Colors.TextPrimary);
                            });
                        }
                    }

                    // LAST PAGE: DISCLAIMER
                    if (includeDisclaimer)
                    {
                        col.Item().PageBreak();
                        col.Item().Element(c => ProfessionalReportTemplate.RenderDisclaimerSection(c, disclaimerText, isArabic));
                    }
                });
            });
        });

        document.GeneratePdf(path);

        AppendAttestationPageAndSyncHash(
            path,
            from, to, branch, section, isArabic, currentUser);

        return path;
    }

    /// <summary>Creates attestation record, appends a one-page attestation PDF, then refreshes stored hash for the final file.</summary>
    private static void AppendAttestationPageAndSyncHash(
        string path, DateTime from, DateTime to, string? branch, string? section, bool isArabic, AppConfiguration app)
    {
        if (!File.Exists(path) || !ServiceContainer.IsInitialized) return;
        IReportAttestationService? attestationService = null;
        try
        {
            attestationService = ServiceContainer.GetOptionalService<IReportAttestationService>();
        }
        catch
        {
            return;
        }
        if (attestationService == null) return;
        var reportType = nameof(ReportType.ExecutiveSummary);
        ReportAttestation? att;
        try
        {
            att = attestationService.CreateAttestation(
                reportType,
                path,
                from,
                to,
                branch,
                section,
                app.CurrentUserId,
                app.CurrentUserName);
        }
        catch
        {
            return;
        }
        if (string.IsNullOrEmpty(att.Sha256Hash)) return;
        var appendixPath = Path.Combine(Path.GetTempPath(), $"WorkAudit_ExecAttest_{Guid.NewGuid():N}.pdf");
        try
        {
            QuestPDF.Fluent.Document.Create(c =>
            {
                c.Page(p =>
                {
                    p.Size(PageSizes.A4);
                    p.Margin(1.5f, Unit.Centimetre);
                    p.PageColor(QuestPDF.Helpers.Colors.White);
                    ProfessionalReportTemplate.ApplyLanguageSettings(p, isArabic);
                    p.DefaultTextStyle(ProfessionalReportTemplate.GetTextStyle(isArabic, 10));
                    p.Content().Element(x => ProfessionalReportTemplate.RenderAttestationSection(x, att, isArabic, true));
                });
            }).GeneratePdf(appendixPath);
            ReportPdfMergeHelper.AppendInPlace(path, appendixPath);
        }
        finally
        {
            try { if (File.Exists(appendixPath)) File.Delete(appendixPath); } catch { /* best-effort */ }
        }
        try
        {
            attestationService.RefreshFileHash(path);
        }
        catch { /* attestation log still useful */ }
    }

    private static void RenderSimpleTable(IContainer container, List<(string Key, int Value)> data, string header1, string header2, bool isArabic)
    {
        container.PaddingTop(12).Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border)
            .Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(100);
                });

                // Header
                table.Header(header =>
                {
                    header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(8)
                        .Text(header1).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                        .Bold().FontColor(QuestPDF.Helpers.Colors.White);
                    
                    header.Cell().Background(ProfessionalReportTemplate.Colors.Primary).Padding(8)
                        .Text(header2).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 10))
                        .Bold().FontColor(QuestPDF.Helpers.Colors.White);
                });

                // Rows
                foreach (var (key, value) in data)
                {
                    table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(8)
                        .Text(key).Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                    
                    table.Cell().Border(1).BorderColor(ProfessionalReportTemplate.Colors.Border).Padding(8)
                        .AlignRight().Text(ArabicFormattingService.FormatNumber(value))
                        .Style(ProfessionalReportTemplate.GetTextStyle(isArabic, 9));
                }
            });
    }

    private static string GenerateFallbackSummary(int total, decimal clearingRate, int issues, bool isArabic)
    {
        if (isArabic)
        {
            return $"تم معالجة {ArabicFormattingService.FormatNumber(total)} مستند خلال الفترة المحددة. " +
                   $"معدل التصفية {ArabicFormattingService.FormatPercentage(clearingRate)}. " +
                   $"تم تحديد {ArabicFormattingService.FormatNumber(issues)} مشكلة تتطلب المتابعة.";
        }
        else
        {
            return $"Processed {ArabicFormattingService.FormatNumber(total)} documents during the specified period. " +
                   $"Clearing rate is {ArabicFormattingService.FormatPercentage(clearingRate)}. " +
                   $"Identified {ArabicFormattingService.FormatNumber(issues)} issues requiring follow-up.";
        }
    }

    private static string FormatDate(string isoDate)
    {
        if (DateTime.TryParse(isoDate, out var dt))
            return dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return isoDate;
    }

    private static int GetSeverityPriority(string severity)
    {
        return severity switch
        {
            NoteSeverity.Critical => 5,
            NoteSeverity.High => 4,
            NoteSeverity.Medium => 3,
            NoteSeverity.Low => 2,
            NoteSeverity.Info => 1,
            _ => 0
        };
    }

    private static string GetSeverityQuestPdfColor(string severity)
    {
        return severity switch
        {
            NoteSeverity.Critical => ProfessionalReportTemplate.Colors.Error,
            NoteSeverity.High => ProfessionalReportTemplate.Colors.Warning,
            NoteSeverity.Medium => ProfessionalReportTemplate.Colors.Accent,
            NoteSeverity.Low => ProfessionalReportTemplate.Colors.Success,
            NoteSeverity.Info => ProfessionalReportTemplate.Colors.TextSecondary,
            _ => ProfessionalReportTemplate.Colors.TextSecondary
        };
    }
}

