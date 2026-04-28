namespace WorkAudit.Domain;

/// <summary>
/// Record of a generated report for history/audit.
/// </summary>
public class ReportHistory
{
    public int Id { get; set; }
    public string Uuid { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string ReportType { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string GeneratedAt { get; set; } = "";
    public string? ConfigJson { get; set; }
    
    // Enhanced metadata for better organization and discoverability
    public string? Tags { get; set; }  // Comma-separated tags
    public string? Purpose { get; set; }  // e.g., "Monthly audit", "Compliance submission"
    public string? Description { get; set; }  // User-provided description
    public int? Version { get; set; }  // Version number if this is a regeneration
    public string? ParentReportId { get; set; }  // UUID of parent report if this is a version
    public string? AppVersion { get; set; }  // Application version at time of generation
}
