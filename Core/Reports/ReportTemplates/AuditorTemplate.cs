namespace WorkAudit.Core.Reports.ReportTemplates;

/// <summary>Internal/external auditors — Audit trail, compliance, chain of custody, exceptions.</summary>
public static class AuditorTemplate
{
    public static ReportTemplateConfig GetConfig() => new()
    {
        Name = "Auditor",
        IncludeKpis = true,
        IncludeBranchBreakdown = true,
        IncludeSectionBreakdown = true,
        IncludeAuditTrail = true,
        IncludeRiskHeatmap = true,
        IncludeCompliance = true,
        IncludeQualityMetrics = true,
        IncludeIssues = true,
        IncludeCharts = true
    };
}
