// ============================================================================
// Report Dashboard - Enhanced Data Models
// ============================================================================
// This file contains the complete data model architecture for the
// Audit Manager's Intelligence Dashboard with full notes integration
// and bilingual report generation capabilities.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkAudit.Domain.Reporting
{
    // ========================================================================
    // CORE AUDIT REPORT MODEL
    // ========================================================================

    /// <summary>
    /// Master report model aggregating all audit data, findings, and notes
    /// for comprehensive report generation with bilingual support.
    /// </summary>
    public class AuditReportModel
    {
        public ReportMetadata Metadata { get; set; } = new();
        public List<ReportSection> Sections { get; set; } = new();

        /// <summary>
        /// Maps FileID → List of Notes for that file.
        /// CRITICAL: This ensures NO note is lost in report generation.
        /// </summary>
        public Dictionary<string, List<AuditNote>> FileNotes { get; set; } = new();

        /// <summary>
        /// Unattached notes (audit-level observations not tied to specific files)
        /// </summary>
        public List<AuditNote> GeneralNotes { get; set; } = new();

        public BilingualSettings LanguageSettings { get; set; } = new();
        public ExecutiveSummary Summary { get; set; } = new();
        public List<SmartRecommendation> Recommendations { get; set; } = new();
        public RiskAssessmentMatrix RiskMatrix { get; set; } = new();

        /// <summary>
        /// Total note count across all files and general notes
        /// </summary>
        public int TotalNoteCount =>
            FileNotes.Values.Sum(notes => notes.Count) + GeneralNotes.Count;
    }

    // ========================================================================
    // REPORT METADATA
    // ========================================================================

    public class ReportMetadata
    {
        public string ReportId { get; set; } = Guid.NewGuid().ToString();
        public string ReportTitle { get; set; } = "";
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public string GeneratedBy { get; set; } = "";
        public string GeneratedByRole { get; set; } = "";

        public DateTime AuditPeriodStart { get; set; }
        public DateTime AuditPeriodEnd { get; set; }

        public string OrganizationName { get; set; } = "";
        public string AuditScope { get; set; } = ""; // e.g., "All Branches" or "Main Street Branch"

        // Bilingual support
        public string LanguageCode { get; set; } = "en"; // "en" or "ar"
        public bool IsRightToLeft => LanguageCode == "ar";

        // Report classification
        public string ConfidentialityLevel { get; set; } = "Internal"; // Internal, Confidential, Restricted
        public string ReportVersion { get; set; } = "1.0";

        // Branding
        public string CompanyLogoPath { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string CompanyAddress { get; set; } = "";
    }

    // ========================================================================
    // REPORT SECTIONS
    // ========================================================================

    public class ReportSection
    {
        public string SectionId { get; set; } = Guid.NewGuid().ToString();
        public string SectionTitle { get; set; } = "";
        public int SectionOrder { get; set; }
        public SectionType Type { get; set; }

        /// <summary>
        /// Section content (can be HTML, markdown, or plain text)
        /// </summary>
        public string Content { get; set; } = "";

        /// <summary>
        /// Findings specific to this section (if applicable)
        /// </summary>
        public List<AuditFinding> Findings { get; set; } = new();

        /// <summary>
        /// Charts/visualizations for this section
        /// </summary>
        public List<ChartData> Charts { get; set; } = new();

        public bool IncludeInExport { get; set; } = true;
    }

    public enum SectionType
    {
        ExecutiveSummary,
        KpiDashboard,
        FindingsTable,
        NotesAndObservations,
        RiskAssessment,
        Recommendations,
        ChartsAndVisualizations,
        RawDataAppendix,
        Custom
    }

    // ========================================================================
    // AUDIT NOTES - CORE MODEL
    // ========================================================================

    /// <summary>
    /// Represents a single note/observation/issue/evidence entry.
    /// CRITICAL: Every note must be preserved and included in reports.
    /// </summary>
    public class AuditNote
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// FileID this note belongs to. Null if general audit note.
        /// </summary>
        public string? FileId { get; set; }

        /// <summary>
        /// User's original note content - PRESERVE ORIGINAL LANGUAGE
        /// Do NOT auto-translate. If user wrote in Arabic, keep Arabic.
        /// If in English, keep English.
        /// </summary>
        public string Content { get; set; } = "";

        public NoteType Type { get; set; }
        public NoteSeverity Severity { get; set; }
        public string Category { get; set; } = ""; // e.g., "Compliance", "Data Quality", "Process"

        // Metadata
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = "";
        public string CreatedByRole { get; set; } = "";

        public DateTime? LastModifiedAt { get; set; }
        public string? LastModifiedBy { get; set; }

        /// <summary>
        /// Attachments (screenshots, references, supporting documents)
        /// </summary>
        public List<NoteAttachment> Attachments { get; set; } = new();

        /// <summary>
        /// Tags for categorization and filtering
        /// </summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// True if note should be highlighted in report
        /// </summary>
        public bool IsFlagged { get; set; }

        /// <summary>
        /// Resolution status (for issues)
        /// </summary>
        public NoteStatus Status { get; set; } = NoteStatus.Open;
        public DateTime? ResolvedAt { get; set; }
        public string? ResolvedBy { get; set; }
        public string? ResolutionComment { get; set; }

        // Display helpers
        public string TypeIcon => Type switch
        {
            NoteType.Issue => "🔴",
            NoteType.Observation => "📋",
            NoteType.Evidence => "✅",
            NoteType.Recommendation => "💡",
            _ => "📝"
        };

        public string SeverityColor => Severity switch
        {
            NoteSeverity.Critical => "#DC3545",
            NoteSeverity.High => "#FFC107",
            NoteSeverity.Medium => "#17A2B8",
            NoteSeverity.Low => "#28A745",
            _ => "#6C757D"
        };

        public string DisplayHeader =>
            $"{TypeIcon} {Type} | {CreatedBy} | {FormatTimeAgo(CreatedAt)}";

        private static string FormatTimeAgo(DateTime dt)
        {
            var diff = DateTime.UtcNow - dt;
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            return dt.ToString("MMM d, yyyy");
        }
    }

    public enum NoteType
    {
        Issue,           // Problem found, requires action
        Observation,     // Notable finding, may or may not require action
        Evidence,        // Supporting documentation or verification
        Recommendation,  // Suggested improvement or action
        General          // General comment or note
    }

    public enum NoteSeverity
    {
        Critical,  // Requires immediate action
        High,      // Important, address soon
        Medium,    // Moderate priority
        Low,       // Minor or informational
        Info       // Informational only, no action needed
    }

    public enum NoteStatus
    {
        Open,        // Active, not yet addressed
        InProgress,  // Being worked on
        Resolved,    // Issue fixed or observation addressed
        Deferred,    // Postponed for future action
        NotApplicable // Determined to be not applicable
    }

    public class NoteAttachment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public long FileSize { get; set; }
        public string ContentType { get; set; } = ""; // MIME type
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public string FileSizeDisplay
        {
            get
            {
                if (FileSize < 1024) return $"{FileSize} B";
                if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
                return $"{FileSize / (1024.0 * 1024):F1} MB";
            }
        }
    }

    // ========================================================================
    // AUDIT FINDINGS
    // ========================================================================

    /// <summary>
    /// Represents a finding in the audit (aggregation of file + notes)
    /// </summary>
    public class AuditFinding
    {
        public string FindingId { get; set; } = Guid.NewGuid().ToString();
        public string FileId { get; set; } = "";
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";

        public string DocumentType { get; set; } = "";
        public string Branch { get; set; } = "";
        public string Section { get; set; } = "";

        public FindingSeverity Severity { get; set; }
        public string Status { get; set; } = "Open";

        /// <summary>
        /// Notes associated with this finding
        /// </summary>
        public List<AuditNote> Notes { get; set; } = new();

        /// <summary>
        /// Count of notes by type
        /// </summary>
        public int IssueCount => Notes.Count(n => n.Type == NoteType.Issue);
        public int ObservationCount => Notes.Count(n => n.Type == NoteType.Observation);
        public int EvidenceCount => Notes.Count(n => n.Type == NoteType.Evidence);
        public int RecommendationCount => Notes.Count(n => n.Type == NoteType.Recommendation);

        /// <summary>
        /// Primary issue summary (from highest severity note)
        /// </summary>
        public string PrimaryIssueSummary
        {
            get
            {
                var criticalNote = Notes
                    .Where(n => n.Type == NoteType.Issue)
                    .OrderByDescending(n => n.Severity)
                    .FirstOrDefault();

                return criticalNote?.Content ?? "";
            }
        }

        /// <summary>
        /// Display text for findings table
        /// </summary>
        public string FindingDisplay =>
            $"{Severity} | {FileName} | {Branch} | {Notes.Count} notes";
    }

    public enum FindingSeverity
    {
        Critical,
        High,
        Medium,
        Low,
        Info
    }

    // ========================================================================
    // BILINGUAL SUPPORT
    // ========================================================================

    public class BilingualSettings
    {
        public string CurrentLanguage { get; set; } = "en"; // "en" or "ar"
        public bool IsRightToLeft => CurrentLanguage == "ar";

        public FontSettings FontSettings { get; set; } = new();
        public NumberFormatSettings NumberFormat { get; set; } = new();

        /// <summary>
        /// Get localized string for a key
        /// </summary>
        public string GetString(string key) =>
            LocalizationDictionary.TryGetValue(key, out var pair)
                ? (CurrentLanguage == "ar" ? pair.Ar : pair.En)
                : key;

        // Simplified localization dictionary (full version in ReportLocalizationService)
        private static readonly Dictionary<string, (string En, string Ar)> LocalizationDictionary = new()
        {
            ["ExecutiveSummary"] = ("Executive Summary", "الملخص التنفيذي"),
            ["KeyFindings"] = ("Key Findings", "النتائج الرئيسية"),
            ["Recommendations"] = ("Recommendations", "التوصيات"),
            ["RiskAssessment"] = ("Risk Assessment", "تقييم المخاطر"),
            ["Notes"] = ("Notes", "الملاحظات"),
            ["Severity"] = ("Severity", "الخطورة"),
            ["Status"] = ("Status", "الحالة"),
            ["Branch"] = ("Branch", "الفرع"),
            ["Section"] = ("Section", "القسم"),
            ["Critical"] = ("Critical", "حرج"),
            ["High"] = ("High", "عالي"),
            ["Medium"] = ("Medium", "متوسط"),
            ["Low"] = ("Low", "منخفض")
        };
    }

    public class FontSettings
    {
        public string PrimaryFontFamily { get; set; } = "Segoe UI";
        public string ArabicFontFamily { get; set; } = "Arabic Typesetting";

        public int BaseFontSize { get; set; } = 12;
        public int HeadingFontSize { get; set; } = 18;
        public int SubheadingFontSize { get; set; } = 14;

        public double LineHeight { get; set; } = 1.2;
        public double ArabicLineHeight { get; set; } = 1.5;

        /// <summary>
        /// Get font family based on language
        /// </summary>
        public string GetFontFamily(string languageCode) =>
            languageCode == "ar" ? ArabicFontFamily : PrimaryFontFamily;

        /// <summary>
        /// Get line height based on language
        /// </summary>
        public double GetLineHeight(string languageCode) =>
            languageCode == "ar" ? ArabicLineHeight : LineHeight;
    }

    public class NumberFormatSettings
    {
        /// <summary>
        /// Use Arabic-Indic numerals (١٢٣) instead of Western (123)
        /// </summary>
        public bool UseArabicIndicNumerals { get; set; } = false;

        public string DecimalSeparator { get; set; } = ".";
        public string ThousandsSeparator { get; set; } = ",";

        /// <summary>
        /// Format number according to current settings
        /// </summary>
        public string FormatNumber(int number)
        {
            var formatted = number.ToString($"N0", GetCultureInfo());
            return UseArabicIndicNumerals ? ToArabicIndic(formatted) : formatted;
        }

        public string FormatDecimal(decimal number, int decimals = 1)
        {
            var formatted = number.ToString($"N{decimals}", GetCultureInfo());
            return UseArabicIndicNumerals ? ToArabicIndic(formatted) : formatted;
        }

        private System.Globalization.CultureInfo GetCultureInfo()
        {
            var culture = new System.Globalization.CultureInfo("en-US");
            culture.NumberFormat.NumberDecimalSeparator = DecimalSeparator;
            culture.NumberFormat.NumberGroupSeparator = ThousandsSeparator;
            return culture;
        }

        private static string ToArabicIndic(string western)
        {
            var mapping = new Dictionary<char, char>
            {
                ['0'] = '٠', ['1'] = '١', ['2'] = '٢', ['3'] = '٣', ['4'] = '٤',
                ['5'] = '٥', ['6'] = '٦', ['7'] = '٧', ['8'] = '٨', ['9'] = '٩'
            };

            return new string(western.Select(c => mapping.TryGetValue(c, out var arabic) ? arabic : c).ToArray());
        }
    }

    // ========================================================================
    // EXECUTIVE SUMMARY
    // ========================================================================

    public class ExecutiveSummary
    {
        /// <summary>
        /// Auto-generated summary paragraph (AI or template-based)
        /// </summary>
        public string SummaryText { get; set; } = "";

        /// <summary>
        /// Key metrics for dashboard
        /// </summary>
        public SummaryMetrics Metrics { get; set; } = new();

        /// <summary>
        /// Top findings highlighted in summary
        /// </summary>
        public List<string> HighlightedFindings { get; set; } = new();

        /// <summary>
        /// Overall risk posture
        /// </summary>
        public RiskPosture OverallRiskPosture { get; set; } = RiskPosture.Moderate;

        /// <summary>
        /// True if summary has been manually edited by user
        /// </summary>
        public bool IsCustomized { get; set; }

        /// <summary>
        /// When summary was last generated/updated
        /// </summary>
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class SummaryMetrics
    {
        public int TotalDocuments { get; set; }
        public int CriticalIssues { get; set; }
        public decimal ComplianceRate { get; set; }
        public decimal CoverageRate { get; set; }
        public decimal ThroughputPerDay { get; set; }

        // Trend indicators (positive = improvement)
        public decimal DocumentsTrend { get; set; }
        public decimal CriticalIssuesTrend { get; set; }
        public decimal ComplianceRateTrend { get; set; }
        public decimal CoverageRateTrend { get; set; }
    }

    public enum RiskPosture
    {
        Low,
        Moderate,
        High,
        Critical
    }

    // ========================================================================
    // SMART RECOMMENDATIONS
    // ========================================================================

    public class SmartRecommendation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";

        public RecommendationPriority Priority { get; set; }
        public RecommendationCategory Category { get; set; }

        /// <summary>
        /// Estimated impact of implementing this recommendation
        /// </summary>
        public string EstimatedImpact { get; set; } = "";

        /// <summary>
        /// Estimated effort required
        /// </summary>
        public string EstimatedEffort { get; set; } = "";

        /// <summary>
        /// Findings/notes that led to this recommendation
        /// </summary>
        public List<string> RelatedFindingIds { get; set; } = new();

        /// <summary>
        /// Suggested actions
        /// </summary>
        public List<string> SuggestedActions { get; set; } = new();

        /// <summary>
        /// User acknowledgment status
        /// </summary>
        public bool IsAcknowledged { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public string? AcknowledgedBy { get; set; }
    }

    public enum RecommendationPriority
    {
        Critical,
        High,
        Medium,
        Low
    }

    public enum RecommendationCategory
    {
        Compliance,
        ProcessImprovement,
        DataQuality,
        Training,
        SystemEnhancement,
        RiskMitigation
    }

    // ========================================================================
    // RISK ASSESSMENT MATRIX
    // ========================================================================

    public class RiskAssessmentMatrix
    {
        /// <summary>
        /// Matrix cells: [Likelihood, Impact] → Finding Count
        /// Likelihood: 0=Low, 1=Med, 2=High
        /// Impact: 0=Low, 1=Med, 2=High, 3=Critical
        /// </summary>
        public Dictionary<(int Likelihood, int Impact), List<AuditFinding>> Matrix { get; set; } = new();

        public void AddFinding(AuditFinding finding, int likelihood, int impact)
        {
            var key = (likelihood, impact);
            if (!Matrix.ContainsKey(key))
                Matrix[key] = new List<AuditFinding>();

            Matrix[key].Add(finding);
        }

        public int GetCount(int likelihood, int impact)
        {
            return Matrix.TryGetValue((likelihood, impact), out var findings)
                ? findings.Count
                : 0;
        }

        public string GetCellColor(int likelihood, int impact)
        {
            var riskScore = likelihood + impact;
            return riskScore switch
            {
                >= 4 => "#DC3545", // Red (high risk)
                >= 2 => "#FFC107", // Yellow (medium risk)
                _ => "#28A745"     // Green (low risk)
            };
        }
    }

    // ========================================================================
    // CHART DATA
    // ========================================================================

    public class ChartData
    {
        public string ChartId { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "";
        public ChartType Type { get; set; }

        public List<ChartDataPoint> DataPoints { get; set; } = new();

        /// <summary>
        /// Chart configuration (colors, axes, etc.)
        /// </summary>
        public ChartConfiguration Configuration { get; set; } = new();
    }

    public enum ChartType
    {
        Bar,
        Line,
        Pie,
        Area,
        HeatMap
    }

    public class ChartDataPoint
    {
        public string Label { get; set; } = "";
        public double Value { get; set; }
        public string Color { get; set; } = "#0078D4";
    }

    public class ChartConfiguration
    {
        public string XAxisLabel { get; set; } = "";
        public string YAxisLabel { get; set; } = "";
        public bool ShowLegend { get; set; } = true;
        public bool ShowGrid { get; set; } = true;
        public int Width { get; set; } = 600;
        public int Height { get; set; } = 400;
    }

    // ========================================================================
    // EXPORT CONFIGURATION
    // ========================================================================

    public class ReportExportConfig
    {
        public ExportTemplate Template { get; set; } = ExportTemplate.ExecutiveSummary;
        public ExportFormat Format { get; set; } = ExportFormat.PDF;

        /// <summary>
        /// Sections to include in export
        /// </summary>
        public HashSet<SectionType> IncludedSections { get; set; } = new()
        {
            SectionType.ExecutiveSummary,
            SectionType.KpiDashboard,
            SectionType.FindingsTable,
            SectionType.NotesAndObservations,
            SectionType.RiskAssessment,
            SectionType.Recommendations,
            SectionType.ChartsAndVisualizations
        };

        /// <summary>
        /// Note detail level in export
        /// </summary>
        public NoteDetailLevel NoteDetailLevel { get; set; } = NoteDetailLevel.Full;

        /// <summary>
        /// PDF-specific options
        /// </summary>
        public PdfExportOptions PdfOptions { get; set; } = new();

        /// <summary>
        /// Language for export
        /// </summary>
        public string ExportLanguage { get; set; } = "en";
    }

    public enum ExportTemplate
    {
        ExecutiveSummary,     // Comprehensive, all sections
        FindingsOnly,         // Detailed findings table with notes
        ManagementBrief,      // KPIs + summary + recommendations
        Custom                // User selects sections
    }

    public enum ExportFormat
    {
        PDF,
        Excel,
        Word,
        HTML
    }

    public enum NoteDetailLevel
    {
        Full,      // All note content, attachments, metadata
        Summary,   // Note count and critical notes only
        None       // Exclude notes from export
    }

    public class PdfExportOptions
    {
        public bool IncludeBranding { get; set; } = true;
        public bool IncludeTableOfContents { get; set; } = true;
        public bool IncludePageNumbers { get; set; } = true;
        public bool IncludeDigitalSignaturePlaceholder { get; set; } = true;

        public string PageSize { get; set; } = "A4"; // A4, Letter, Legal
        public string Orientation { get; set; } = "Portrait"; // Portrait, Landscape

        public string HeaderText { get; set; } = "";
        public string FooterText { get; set; } = "";

        public bool EnableRtlSupport { get; set; } = true;
    }
}
