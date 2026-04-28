namespace WorkAudit.Domain;

/// <summary>
/// Progress information for report generation.
/// </summary>
public record ReportProgress
{
    public int PercentComplete { get; init; }  // 0-100
    public string Stage { get; init; } = "";   // e.g., "Loading documents", "Building PDF"
    public int ItemsProcessed { get; init; }   // Current item count
    public int TotalItems { get; init; }       // Total item count (if known)
    public TimeSpan Elapsed { get; init; }     // Time elapsed since start
    
    public string GetDisplayText()
    {
        if (TotalItems > 0 && ItemsProcessed > 0)
        {
            return $"{Stage} ({ItemsProcessed:N0}/{TotalItems:N0})...";
        }
        return $"{Stage}...";
    }
}
