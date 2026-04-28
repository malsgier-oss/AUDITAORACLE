namespace WorkAudit.Core.Reports;

/// <summary>
/// Shared constants for report generation to avoid QuestPDF stack overflow and ensure consistent pagination.
/// </summary>
public static class ReportConstants
{
    /// <summary>Max table rows per page to avoid QuestPDF stack overflow from deep layout trees.</summary>
    public const int MaxTableRowsPerPage = 100;

    /// <summary>Max table rows for reports that use a simple cap (e.g. Status Summary, Document Type Summary).</summary>
    public const int MaxTableRowsCap = 200;

    /// <summary>Max branch/section items to show in Issues and Focus report lists.</summary>
    public const int MaxIssuesByBranchSectionItems = 50;

    /// <summary>Max days per page for Daily Summary report (paginate by day chunks).</summary>
    public const int MaxDailySummaryDaysPerPage = 90;

    /// <summary>Bar rows per physical page for daily volume chart (Arabic/RTL needs more vertical space per row).</summary>
    public const int MaxDailyChartBarRowsPerPage = 12;
}
