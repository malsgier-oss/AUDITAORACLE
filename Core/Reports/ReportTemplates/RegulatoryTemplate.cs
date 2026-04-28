namespace WorkAudit.Core.Reports.ReportTemplates;

/// <summary>Central bank, regulators — SOX/IFRS compliance, retention, data lineage, certifications.</summary>
public static class RegulatoryTemplate
{
    public static ReportTemplateConfig GetConfig() => new()
    {
        Name = "Regulatory",
        IncludeKpis = true,
        IncludeBranchBreakdown = true,
        IncludeSectionBreakdown = true,
        IncludeAuditTrail = true,
        IncludeRiskHeatmap = true,
        IncludeCompliance = true,
        IncludeQualityMetrics = true,
        IncludeIssues = true,
        IncludeCharts = false
    };
}
