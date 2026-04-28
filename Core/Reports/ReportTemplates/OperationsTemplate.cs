namespace WorkAudit.Core.Reports.ReportTemplates;

/// <summary>Operations team — Throughput, backlog, SLA compliance, quality metrics.</summary>
public static class OperationsTemplate
{
    public static ReportTemplateConfig GetConfig() => new()
    {
        Name = "Operations",
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
