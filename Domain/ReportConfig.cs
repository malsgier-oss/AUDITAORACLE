namespace WorkAudit.Domain;

/// <summary>
/// Configuration for report generation. Used by all report generators.
/// </summary>
public class ReportConfig
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public ReportPeriod Preset { get; set; } = ReportPeriod.Monthly;
    public string? Branch { get; set; }
    public string? Section { get; set; }
    public int? UserId { get; set; }
    public string? DocumentType { get; set; }
    public string? Status { get; set; }
    public string? Engagement { get; set; }
    /// <summary>Username for User Activity report filter (created/reviewed by).</summary>
    public string? UserFilter { get; set; }
    public ReportFormat Format { get; set; } = ReportFormat.Pdf;
    public bool IncludeCharts { get; set; } = true;

    /// <summary>Report type to generate.</summary>
    public ReportType ReportType { get; set; } = ReportType.BranchSummary;

    /// <summary>Audience template (affects which sections are included).</summary>
    public ReportTemplate ReportTemplate { get; set; } = ReportTemplate.Executive;

    /// <summary>Optional output path override (for scheduled reports, etc.).</summary>
    public string? OutputPath { get; set; }

    /// <summary>Report language: "en" for English, "ar" for Arabic. Affects text direction, fonts, and translations.</summary>
    public string Language { get; set; } = "en";

    /// <summary>When true, executive-style PDFs include table of contents (where supported).</summary>
    public bool IncludeTableOfContents { get; set; } = true;

    /// <summary>When true, include organization logo and full cover branding (where supported).</summary>
    public bool IncludeBranding { get; set; } = true;

    /// <summary>When true, include regulatory disclaimer block (where supported).</summary>
    public bool IncludeDisclaimer { get; set; } = true;

    /// <summary>Optional watermark: None, Draft, or Confidential.</summary>
    public ReportWatermark Watermark { get; set; } = ReportWatermark.None;

    /// <summary>When true, generate one PDF per branch (or section) instead of a single combined PDF. Supported for Branch Summary, Section Summary, Performance.</summary>
    public bool ExportPerBranch { get; set; }

    /// <summary>When ExportPerBranch is true, zip all generated PDFs into one archive.</summary>
    public bool ZipPerBranch { get; set; }
}

/// <summary>Watermark option for reports.</summary>
public enum ReportWatermark
{
    None,
    Draft,
    Confidential
}

/// <summary>Report template for different audiences.</summary>
public enum ReportTemplate
{
    Executive,
    BranchManager,
    Auditor,
    Regulatory,
    Operations
}

/// <summary>Period preset for date range.</summary>
public enum ReportPeriod
{
    Weekly,
    Monthly,
    Quarter,
    HalfYear,
    Yearly
}

/// <summary>Output format for reports.</summary>
public enum ReportFormat
{
    Pdf,
    Excel,
    Csv
}

/// <summary>Report type identifier.</summary>
public enum ReportType
{
    DailySummary,
    AuditTrail,
    BranchSummary,
    SectionSummary,
    StatusSummary,
    DocumentTypeSummary,
    UserActivity,
    Performance,
    IssuesAndFocus,
    AssignmentSummary,
    ExecutiveSummary
}
