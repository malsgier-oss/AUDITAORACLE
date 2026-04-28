namespace WorkAudit.Core.Reports.ReportTemplates;

/// <summary>
/// Configuration for report templates (audience-specific content).
/// </summary>
public class ReportTemplateConfig
{
    public string Name { get; set; } = "";
    public bool IncludeKpis { get; set; } = true;
    public bool IncludeBranchBreakdown { get; set; } = true;
    public bool IncludeSectionBreakdown { get; set; } = true;
    public bool IncludeAuditTrail { get; set; }
    public bool IncludeRiskHeatmap { get; set; } = true;
    public bool IncludeCompliance { get; set; }
    public bool IncludeQualityMetrics { get; set; } = true;
    public bool IncludeIssues { get; set; } = true;
    public bool IncludeCharts { get; set; } = true;
}
