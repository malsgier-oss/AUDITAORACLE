namespace WorkAudit.Core.Reports.ReportTemplates;

/// <summary>CEO, CFO — KPI cards, trends, risk heatmap, top issues (1 page).</summary>
public static class ExecutiveTemplate
{
    public static ReportTemplateConfig GetConfig() => new()
    {
        Name = "Executive",
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
