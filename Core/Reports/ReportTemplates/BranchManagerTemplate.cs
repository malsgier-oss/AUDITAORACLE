namespace WorkAudit.Core.Reports.ReportTemplates;

/// <summary>Branch managers — Branch-specific performance, user activity, outstanding items.</summary>
public static class BranchManagerTemplate
{
    public static ReportTemplateConfig GetConfig() => new()
    {
        Name = "Branch Manager",
        IncludeKpis = true,
        IncludeBranchBreakdown = true,
        IncludeSectionBreakdown = true,
        IncludeAuditTrail = false,
        IncludeRiskHeatmap = true,
        IncludeCompliance = false,
        IncludeQualityMetrics = true,
        IncludeIssues = true,
        IncludeCharts = true
    };
}
