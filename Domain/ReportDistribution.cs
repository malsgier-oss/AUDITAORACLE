namespace WorkAudit.Domain;

/// <summary>
/// Report distribution tracking — who viewed/exported/printed reports.
/// Access audit trail for compliance.
/// </summary>
public class ReportDistribution
{
    public long Id { get; set; }
    public string Uuid { get; set; } = "";
    public string ReportPath { get; set; } = "";
    public string ReportType { get; set; } = "";
    public string EventType { get; set; } = ""; // View, Export, Print
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public string? Details { get; set; }
}
