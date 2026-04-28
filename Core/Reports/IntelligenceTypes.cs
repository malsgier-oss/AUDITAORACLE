namespace WorkAudit.Core.Reports;

/// <summary>Strategic finding for executive readers (pattern-based, no LLM).</summary>
public sealed class StrategicInsight
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
}

public enum ExecutiveActionPriority
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>Actionable item for management follow-up.</summary>
public sealed class ExecutiveAction
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public ExecutiveActionPriority Priority { get; set; } = ExecutiveActionPriority.Medium;
    public string? SuggestedOwner { get; set; }
}
