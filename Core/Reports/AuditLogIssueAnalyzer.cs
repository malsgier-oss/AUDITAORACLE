using WorkAudit.Domain;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Helpers for interpreting <see cref="AuditLogEntry"/> rows produced by
/// <see cref="WorkAudit.Core.Security.IAuditTrailService"/> in the context of report KPIs.
/// </summary>
/// <remarks>
/// "Issues fixed" used to be computed inline in two report generators with two different bugs:
/// (a) reading <c>OldValue</c> / <c>NewValue</c> columns that the audit logger never populated, and
/// (b) using <c>Contains</c> instead of exact equality (so "ReadyForAudit" matched "Audit").
/// Centralising the rule here gives us a single place to test and to evolve when the audit-log
/// contract changes.
/// </remarks>
public static class AuditLogIssueAnalyzer
{
    /// <summary>
    /// Counts <see cref="AuditAction.DocumentStatusChanged"/> events that represent a transition
    /// from <see cref="Enums.Status.Issue"/> to <see cref="Enums.Status.Cleared"/>.
    /// </summary>
    /// <remarks>
    /// New events populate <see cref="AuditLogEntry.OldValue"/> / <see cref="AuditLogEntry.NewValue"/>;
    /// historical events created before that contract was fixed have NULLs and are matched by parsing
    /// the legacy "Status set to Cleared" details string.
    /// </remarks>
    public static int CountIssuesFixed(IEnumerable<AuditLogEntry> statusChangeEntries)
    {
        if (statusChangeEntries == null) return 0;
        var count = 0;
        foreach (var e in statusChangeEntries)
        {
            if (IsIssueToClearedTransition(e))
                count++;
        }
        return count;
    }

    /// <summary>True when this entry represents an Issue → Cleared status change.</summary>
    public static bool IsIssueToClearedTransition(AuditLogEntry entry)
    {
        if (entry == null) return false;
        if (string.Equals(entry.OldValue, Enums.Status.Issue, StringComparison.Ordinal)
            && string.Equals(entry.NewValue, Enums.Status.Cleared, StringComparison.Ordinal))
        {
            return true;
        }
        // Legacy fallback: the audit logger before the Phase 1a fix only wrote a freeform Details
        // string ("Status set to Cleared") and left old/new NULL.
        if (string.IsNullOrEmpty(entry.OldValue)
            && string.IsNullOrEmpty(entry.NewValue)
            && !string.IsNullOrEmpty(entry.Details)
            && entry.Details.Contains("Status set to " + Enums.Status.Cleared, StringComparison.Ordinal))
        {
            return true;
        }
        return false;
    }
}
