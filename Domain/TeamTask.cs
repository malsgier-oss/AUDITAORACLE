namespace WorkAudit.Domain;

/// <summary>
/// Admin-defined recurring checklist task assigned to a user (not tied to a document).
/// </summary>
public class TeamTask
{
    public int Id { get; set; }
    public string Uuid { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int AssignedToUserId { get; set; }
    public string AssignedToUsername { get; set; } = "";
    public int AssignedByUserId { get; set; }
    public string AssignedByUsername { get; set; } = "";
    /// <see cref="TeamTaskRecurrence"/>
    public string Recurrence { get; set; } = TeamTaskRecurrence.Daily;
    /// <summary>ISO date yyyy-MM-dd (local calendar day when task becomes active).</summary>
    public string StartDate { get; set; } = "";
    /// <summary>Optional ISO date yyyy-MM-dd; null means no end.</summary>
    public string? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public static class TeamTaskRecurrence
{
    public const string Daily = "Daily";
    public const string Weekly = "Weekly";
    public const string Monthly = "Monthly";

    public static readonly string[] All = { Daily, Weekly, Monthly };
}

/// <summary>Assignment plus completion for the current period (for dashboard UI).</summary>
public class TeamTaskWithState
{
    public TeamTask Task { get; set; } = null!;
    public string PeriodKey { get; set; } = "";
    public bool IsCompletedForCurrentPeriod { get; set; }
    public bool HasNoteForCurrentPeriod { get; set; }
}
