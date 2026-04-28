namespace WorkAudit.Domain;

/// <summary>
/// KPI target for variance analysis. Stored as JSON in app_settings (kpi_targets_json).
/// </summary>
public class KpiTarget
{
    /// <summary>Branch filter; null = bank-wide.</summary>
    public string? Branch { get; set; }
    /// <summary>Section filter; null = all sections.</summary>
    public string? Section { get; set; }
    /// <summary>KPI name: ClearingRate, Throughput, IssueRate, DocumentsProcessed.</summary>
    public string KpiName { get; set; } = "";
    /// <summary>Target value (e.g. 80 for 80%, 50 for docs/day).</summary>
    public decimal Target { get; set; }
    /// <summary>Warning threshold (below/above target).</summary>
    public decimal Warning { get; set; }
    /// <summary>Critical threshold.</summary>
    public decimal Critical { get; set; }
    /// <summary>Period: Daily, Weekly, Monthly.</summary>
    public string Period { get; set; } = "Monthly";
}

/// <summary>KPI names for targets.</summary>
public static class KpiNames
{
    public const string ClearingRate = "ClearingRate";
    public const string Throughput = "Throughput";
    public const string IssueRate = "IssueRate";
    public const string DocumentsProcessed = "DocumentsProcessed";
}
