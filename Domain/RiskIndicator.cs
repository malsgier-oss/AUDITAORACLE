namespace WorkAudit.Domain;

/// <summary>
/// Risk level for branches, sections, or users.
/// </summary>
public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Risk indicator for a branch, section, or user.
/// </summary>
public class RiskIndicator
{
    /// <summary>Entity type: Branch, Section, User.</summary>
    public string EntityType { get; set; } = "";
    /// <summary>Entity name (branch name, section name, username).</summary>
    public string EntityName { get; set; } = "";
    public RiskLevel Level { get; set; }
    /// <summary>Human-readable reason (e.g. "Issue rate 12% (threshold 10%)").</summary>
    public string Reason { get; set; } = "";
    /// <summary>Risk score 0-100.</summary>
    public decimal Score { get; set; }
}
