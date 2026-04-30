using FluentAssertions;
using WorkAudit.Core.Reports;
using WorkAudit.Domain;
using Xunit;

namespace WorkAudit.Tests.Reports;

/// <summary>
/// Guards the "Issues Fixed (this period)" counter that report consumers ask for. Two regressions
/// are captured here:
///   (a) the audit logger used to leave OldValue / NewValue NULL, so the old report code that read
///       only those columns reported zero forever (Phase 1a fix).
///   (b) earlier reports used <c>Contains</c> instead of exact equality, so any status containing
///       the substring "Cleared" or "Issue" would flip the counter.
/// </summary>
public class AuditLogIssueAnalyzerTests
{
    [Fact]
    public void CountIssuesFixed_NewContract_CountsOnlyExactIssueToCleared()
    {
        var entries = new List<AuditLogEntry>
        {
            new() { Action = AuditAction.DocumentStatusChanged, OldValue = Enums.Status.Issue, NewValue = Enums.Status.Cleared },
            new() { Action = AuditAction.DocumentStatusChanged, OldValue = Enums.Status.Issue, NewValue = Enums.Status.Cleared },
            new() { Action = AuditAction.DocumentStatusChanged, OldValue = Enums.Status.Reviewed, NewValue = Enums.Status.Cleared },
            new() { Action = AuditAction.DocumentStatusChanged, OldValue = Enums.Status.Issue, NewValue = Enums.Status.Reviewed }
        };

        AuditLogIssueAnalyzer.CountIssuesFixed(entries).Should().Be(2);
    }

    [Fact]
    public void CountIssuesFixed_LegacyEntries_FallsBackToDetailsString()
    {
        var entries = new List<AuditLogEntry>
        {
            new()
            {
                Action = AuditAction.DocumentStatusChanged,
                OldValue = null,
                NewValue = null,
                Details = $"Status set to {Enums.Status.Cleared}"
            },
            new()
            {
                Action = AuditAction.DocumentStatusChanged,
                OldValue = null,
                NewValue = null,
                Details = $"Status set to {Enums.Status.Reviewed}"
            }
        };

        AuditLogIssueAnalyzer.CountIssuesFixed(entries).Should().Be(1);
    }

    [Fact]
    public void CountIssuesFixed_NeverMatchesOnSubstring()
    {
        var entries = new List<AuditLogEntry>
        {
            new() { OldValue = "ReadyForAudit", NewValue = "AlmostCleared" },
            new() { OldValue = "MajorIssueResolved", NewValue = Enums.Status.Cleared }
        };

        AuditLogIssueAnalyzer.CountIssuesFixed(entries).Should().Be(0);
    }

    [Fact]
    public void CountIssuesFixed_EmptyOrNull_ReturnsZero()
    {
        AuditLogIssueAnalyzer.CountIssuesFixed(new List<AuditLogEntry>()).Should().Be(0);
        AuditLogIssueAnalyzer.CountIssuesFixed(null!).Should().Be(0);
    }

    [Fact]
    public void IsIssueToClearedTransition_PartialOldValue_DoesNotMatch()
    {
        // Defends against "OldValue.Contains('Issue')" regressions: an Old/New that happens to
        // contain the literal status word should not be misclassified as a fix.
        var entry = new AuditLogEntry
        {
            OldValue = "Some narrative referencing Issue",
            NewValue = Enums.Status.Cleared
        };

        AuditLogIssueAnalyzer.IsIssueToClearedTransition(entry).Should().BeFalse();
    }
}
