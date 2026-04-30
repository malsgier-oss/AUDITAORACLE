using System.Globalization;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Risk scoring for branches and sections based on thresholds.
/// </summary>
public interface IRiskScoringService
{
    IReadOnlyList<RiskIndicator> GetRiskIndicators(IDocumentStore store, IDocumentAssignmentStore? assignmentStore, DateTime from, DateTime to);
}

public class RiskScoringService : IRiskScoringService
{
    private const decimal IssueRateThreshold = 10m;
    private const decimal ClearingRateMinThreshold = 70m;
    private const decimal ThroughputDeclineThreshold = 0.20m;
    private const int OutstandingIssuesThreshold = 50;
    private const int OverdueAssignmentsThreshold = 10;

    public IReadOnlyList<RiskIndicator> GetRiskIndicators(IDocumentStore store, IDocumentAssignmentStore? assignmentStore, DateTime from, DateTime to)
    {
        var indicators = new List<RiskIndicator>();
        var fromStr = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toStr = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59";
        var docs = store.ListDocuments(dateFrom: fromStr, dateTo: toStr, limit: 50_000, newestFirst: true);

        foreach (var branch in docs.Select(d => string.IsNullOrEmpty(d.Branch) ? "(No Branch)" : d.Branch).Distinct())
        {
            var branchDocs = docs.Where(d => (string.IsNullOrEmpty(d.Branch) ? "(No Branch)" : d.Branch) == branch).ToList();
            var total = branchDocs.Count;
            var issueCount = branchDocs.Count(d => d.Status == Enums.Status.Issue);
            var cleared = branchDocs.Count(d => d.Status == Enums.Status.Cleared);
            var active = branchDocs.Count(d => d.Status != Enums.Status.Archived);
            var clearingRate = active > 0 ? (decimal)cleared / active * 100 : 0;
            var issueRate = total > 0 ? (decimal)issueCount / total * 100 : 0;
            var days = Math.Max(1, (to - from).Days + 1);
            var throughput = (decimal)total / days;

            if (issueRate >= IssueRateThreshold)
            {
                var score = Math.Min(100, 50 + (decimal)(issueRate - IssueRateThreshold) * 5);
                indicators.Add(new RiskIndicator
                {
                    EntityType = "Branch",
                    EntityName = branch,
                    Level = issueRate >= 15 ? RiskLevel.Critical : RiskLevel.High,
                    Reason = $"Issue rate {issueRate:F1}% (threshold {IssueRateThreshold}%)",
                    Score = score
                });
            }
            if (clearingRate < ClearingRateMinThreshold && active > 0)
            {
                var score = Math.Min(100, 50 + (ClearingRateMinThreshold - clearingRate));
                indicators.Add(new RiskIndicator
                {
                    EntityType = "Branch",
                    EntityName = branch,
                    Level = clearingRate < 50 ? RiskLevel.Critical : RiskLevel.High,
                    Reason = $"Clearing rate {clearingRate:F1}% (min threshold {ClearingRateMinThreshold}%)",
                    Score = score
                });
            }
            if (issueCount >= OutstandingIssuesThreshold)
            {
                indicators.Add(new RiskIndicator
                {
                    EntityType = "Branch",
                    EntityName = branch,
                    Level = issueCount >= 100 ? RiskLevel.Critical : RiskLevel.High,
                    Reason = $"{issueCount} outstanding issues (threshold {OutstandingIssuesThreshold})",
                    Score = Math.Min(100, 40 + issueCount / 2m)
                });
            }
        }

        if (assignmentStore != null)
        {
            var allAssignments = assignmentStore.ListAll(null, null);
            var overdue = allAssignments.Count(a => a.Status is AssignmentStatus.Pending or AssignmentStatus.InProgress
                && !string.IsNullOrEmpty(a.DueDate) && DateTime.TryParse(a.DueDate, out var d) && d.Date < DateTime.Today);
            if (overdue >= OverdueAssignmentsThreshold)
            {
                indicators.Add(new RiskIndicator
                {
                    EntityType = "Assignment",
                    EntityName = "All",
                    Level = overdue >= 25 ? RiskLevel.Critical : RiskLevel.High,
                    Reason = $"{overdue} overdue assignments (threshold {OverdueAssignmentsThreshold})",
                    Score = Math.Min(100, 30 + overdue)
                });
            }
        }

        return indicators.OrderByDescending(i => i.Score).ToList();
    }
}
