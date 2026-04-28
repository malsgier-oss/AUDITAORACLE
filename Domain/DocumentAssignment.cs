namespace WorkAudit.Domain;

/// <summary>
/// Document assignment for audit workflow. P4: Document Assignment System.
/// </summary>
public class DocumentAssignment
{
    public int Id { get; set; }
    public string Uuid { get; set; } = Guid.NewGuid().ToString();
    public int DocumentId { get; set; }
    public string DocumentUuid { get; set; } = "";
    public int AssignedToUserId { get; set; }
    public string AssignedToUsername { get; set; } = "";
    public int AssignedByUserId { get; set; }
    public string AssignedByUsername { get; set; } = "";
    public string AssignedAt { get; set; } = "";
    public string? DueDate { get; set; }
    public string Priority { get; set; } = AssignmentPriority.Normal;
    public string Status { get; set; } = AssignmentStatus.Pending;
    public string? Notes { get; set; }
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
    public string? CompletionNotes { get; set; }
}

/// <summary>Assignment status constants.</summary>
public static class AssignmentStatus
{
    public const string Pending = "Pending";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static readonly string[] All = { Pending, InProgress, Completed, Cancelled };
}

/// <summary>Assignment priority constants.</summary>
public static class AssignmentPriority
{
    public const string Low = "Low";
    public const string Normal = "Normal";
    public const string High = "High";
    public const string Urgent = "Urgent";

    public static readonly string[] All = { Low, Normal, High, Urgent };
}
