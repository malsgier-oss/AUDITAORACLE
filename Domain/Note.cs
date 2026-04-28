namespace WorkAudit.Domain;

/// <summary>
/// Enhanced note entity with categorization, severity, and full metadata.
/// Replaces single-string Document.Notes field with structured, queryable notes.
/// </summary>
public class Note
{
    // Core identity
    public int Id { get; set; }
    public string Uuid { get; set; } = Guid.NewGuid().ToString();

    // Association with document
    public int DocumentId { get; set; }
    public string DocumentUuid { get; set; } = "";

    // Content (PRESERVE ORIGINAL LANGUAGE - do not auto-translate user content)
    public string Content { get; set; } = "";

    // Classification
    public string Type { get; set; } = NoteType.Observation;
    public string Severity { get; set; } = NoteSeverity.Info;
    public string Category { get; set; } = "";

    // Metadata - creation tracking
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string CreatedBy { get; set; } = "";
    public int CreatedByUserId { get; set; }

    // Metadata - update tracking
    public string? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // Status tracking for issue resolution workflow
    public string Status { get; set; } = NoteStatus.Open;
    public string? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionComment { get; set; }

    // Attachments (JSON-serialized list of file paths or references)
    public string? Attachments { get; set; }

    // Tags for filtering (comma-separated string)
    public string? Tags { get; set; }

    // Display helpers for UI binding
    public string TypeIcon => Type switch
    {
        NoteType.Issue => "🔴",
        NoteType.Evidence => "✅",
        NoteType.Recommendation => "💡",
        NoteType.Journal => "📓",
        _ => "📋" // Default for Observation and others
    };

    public string SeverityColor => Severity switch
    {
        NoteSeverity.Critical => "#DC3545",
        NoteSeverity.High => "#FFC107",
        NoteSeverity.Medium => "#17A2B8",
        NoteSeverity.Low => "#28A745",
        _ => "#6C757D" // Default for Info and others
    };

    // Resolution metadata helpers for UI
    public bool HasResolution => !string.IsNullOrEmpty(ResolvedAt);

    public string FormattedResolvedAt
    {
        get
        {
            if (string.IsNullOrEmpty(ResolvedAt)) return "";

            if (DateTime.TryParse(ResolvedAt, out var resolvedDate))
            {
                var timeSpan = DateTime.UtcNow - resolvedDate;
                if (timeSpan.TotalMinutes < 1) return "just now";
                if (timeSpan.TotalMinutes < 60) return $"{(int)timeSpan.TotalMinutes} minute(s) ago";
                if (timeSpan.TotalHours < 24) return $"{(int)timeSpan.TotalHours} hour(s) ago";
                if (timeSpan.TotalDays < 7) return $"{(int)timeSpan.TotalDays} day(s) ago";
                if (timeSpan.TotalDays < 30) return $"{(int)(timeSpan.TotalDays / 7)} week(s) ago";

                return resolvedDate.ToString("MMM dd, yyyy");
            }

            return ResolvedAt;
        }
    }

    public string ResolutionCommentPreview
    {
        get
        {
            if (string.IsNullOrEmpty(ResolutionComment)) return "";
            return ResolutionComment.Length > 50
                ? ResolutionComment.Substring(0, 50) + "..."
                : ResolutionComment;
        }
    }
}

/// <summary>
/// Note type constants for categorizing notes by purpose.
/// </summary>
public static class NoteType
{
    public const string Observation = "Observation";
    public const string Issue = "Issue";
    public const string Evidence = "Evidence";
    public const string Recommendation = "Recommendation";
    public const string Journal = "Journal";

    public static readonly string[] Values = { Observation, Issue, Evidence, Recommendation, Journal };
}

/// <summary>
/// Note severity constants for risk/priority classification.
/// </summary>
public static class NoteSeverity
{
    public const string Critical = "Critical";
    public const string High = "High";
    public const string Medium = "Medium";
    public const string Low = "Low";
    public const string Info = "Info";

    public static readonly string[] Values = { Critical, High, Medium, Low, Info };
}

/// <summary>
/// Note status constants for tracking resolution workflow.
/// </summary>
public static class NoteStatus
{
    public const string Open = "Open";
    public const string InProgress = "InProgress";
    public const string Resolved = "Resolved";
    public const string Deferred = "Deferred";

    public static readonly string[] Values = { Open, InProgress, Resolved, Deferred };
}
