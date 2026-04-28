namespace WorkAudit.Domain;

/// <summary>
/// Represents an editable draft of a report before final export.
/// </summary>
public class ReportDraft
{
    public int Id { get; set; }
    public string Uuid { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string ReportType { get; set; } = "";
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string? LastModifiedAt { get; set; }
    
    /// <summary>Full JSON of the ReportConfig used to generate this draft.</summary>
    public string ConfigJson { get; set; } = "";
    
    /// <summary>Path to the draft file (HTML or intermediate format).</summary>
    public string DraftFilePath { get; set; } = "";
    
    /// <summary>User-provided title for the draft.</summary>
    public string? Title { get; set; }
    
    /// <summary>User notes or comments about this draft.</summary>
    public string? Notes { get; set; }
    
    /// <summary>Tags for categorization (comma-separated).</summary>
    public string? Tags { get; set; }
    
    /// <summary>Whether this draft is marked as finalized (ready to export).</summary>
    public bool IsFinalized { get; set; }
    
    /// <summary>If exported, the UUID of the resulting ReportHistory entry.</summary>
    public string? ExportedReportHistoryId { get; set; }
}
