namespace WorkAudit.Core.Services;

/// <summary>
/// Request to drill down from report/dashboard to document view with filters.
/// </summary>
public class DrillDownRequest
{
    public string? Branch { get; set; }
    public string? Section { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    /// <summary>When set, Workspace selects this document after filters are applied.</summary>
    public int? DocumentId { get; set; }
}

/// <summary>
/// Static event for drill-down navigation from reports/dashboard to Workspace.
/// </summary>
public static class DrillDownRequested
{
    public static event Action<DrillDownRequest>? Requested;

    public static void Raise(DrillDownRequest request)
    {
        Requested?.Invoke(request);
    }
}
