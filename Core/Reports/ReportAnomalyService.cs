using System.Globalization;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Statistical anomaly detection for reports (throughput drops, issue spikes, user outliers).
/// </summary>
public interface IReportAnomalyService
{
    IReadOnlyList<ReportAnomaly> GetAnomalies(IDocumentStore store, DateTime from, DateTime to, string? branch = null, string? section = null, string? engagement = null);
}

public class ReportAnomaly
{
    public string EntityType { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string Metric { get; set; } = "";
    public decimal Current { get; set; }
    public decimal Average { get; set; }
    public decimal ChangePercent { get; set; }
    public string Reason { get; set; } = "";
}

public class ReportAnomalyService : IReportAnomalyService
{
    private const int MaxDocuments = 50_000;
    private const int PeriodsForAverage = 4;
    private const decimal ThroughputDropThreshold = 0.30m;
    private const decimal StdDevThreshold = 2.0m;

    public IReadOnlyList<ReportAnomaly> GetAnomalies(IDocumentStore store, DateTime from, DateTime to, string? branch = null, string? section = null, string? engagement = null)
    {
        var anomalies = new List<ReportAnomaly>();
        var periodDays = (to - from).Days + 1;

        var fromStr = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toStr = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59";
        var docs = store.ListDocuments(dateFrom: fromStr, dateTo: toStr, branch: branch, section: section, engagement: engagement, limit: MaxDocuments);

        var priorPeriods = new List<(DateTime f, DateTime t, List<Document> d)>();
        for (var i = 1; i <= PeriodsForAverage; i++)
        {
            var pf = from.AddDays(-periodDays * i);
            var pt = to.AddDays(-periodDays * i);
            var priorDocs = store.ListDocuments(dateFrom: pf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), dateTo: pt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59", branch: branch, section: section, engagement: engagement, limit: MaxDocuments);
            priorPeriods.Add((pf, pt, priorDocs));
        }

        var byBranch = docs.GroupBy(d => string.IsNullOrEmpty(d.Branch) ? "(No Branch)" : d.Branch).ToList();
        foreach (var g in byBranch)
        {
            var branchName = g.Key;
            var currentDocs = g.ToList();
            var currentTotal = currentDocs.Count;
            var currentThroughput = periodDays > 0 ? (decimal)currentTotal / periodDays : 0;
            var currentIssues = currentDocs.Count(d => d.Status == Enums.Status.Issue);

            var priorTotals = priorPeriods.Select(p => p.d.Count(d => (string.IsNullOrEmpty(d.Branch) ? "(No Branch)" : d.Branch) == branchName)).ToList();
            var priorThroughputs = priorPeriods.Select((p, i) => periodDays > 0 ? (decimal)priorTotals[i] / periodDays : 0).ToList();
            var priorIssues = priorPeriods.Select(p => p.d.Count(d => (string.IsNullOrEmpty(d.Branch) ? "(No Branch)" : d.Branch) == branchName && d.Status == Enums.Status.Issue)).ToList();

            if (priorThroughputs.Count > 0 && priorThroughputs.Average() > 0)
            {
                var avgThroughput = (decimal)priorThroughputs.Average();
                var changePct = (currentThroughput - avgThroughput) / avgThroughput * 100;
                if (changePct <= -ThroughputDropThreshold * 100)
                    anomalies.Add(new ReportAnomaly
                    {
                        EntityType = "Branch",
                        EntityName = branchName,
                        Metric = "Throughput",
                        Current = currentThroughput,
                        Average = avgThroughput,
                        ChangePercent = changePct,
                        Reason = $"Throughput {currentThroughput:F1} docs/day (avg {avgThroughput:F1}, {changePct:F0}%)"
                    });
            }

            if (priorIssues.Count > 0)
            {
                var avgIssues = (decimal)priorIssues.Average();
                var stdIssues = StdDev(priorIssues.Select(x => (decimal)x).ToList());
                if (stdIssues > 0 && currentIssues > avgIssues + StdDevThreshold * stdIssues)
                    anomalies.Add(new ReportAnomaly
                    {
                        EntityType = "Branch",
                        EntityName = branchName,
                        Metric = "Issues",
                        Current = currentIssues,
                        Average = avgIssues,
                        ChangePercent = avgIssues > 0 ? (decimal)(currentIssues - avgIssues) / avgIssues * 100 : 0,
                        Reason = $"{currentIssues} issues (avg {avgIssues:F0}, +{((currentIssues - avgIssues) / avgIssues * 100):F0}%)"
                    });
            }
        }

        var byUser = docs.Where(d => !string.IsNullOrEmpty(d.CreatedBy)).GroupBy(d => d.CreatedBy!).ToList();
        foreach (var g in byUser)
        {
            var username = g.Key;
            var currentCount = g.Count();
            var priorCounts = priorPeriods.Select(p => p.d.Count(d => d.CreatedBy == username)).ToList();
            if (priorCounts.Count > 0 && priorCounts.Average() > 0)
            {
                var avg = (decimal)priorCounts.Average();
                var std = StdDev(priorCounts.Select(x => (decimal)x).ToList());
                if (std > 0)
                {
                    var zScore = (currentCount - avg) / std;
                    if (zScore <= -StdDevThreshold || zScore >= StdDevThreshold)
                        anomalies.Add(new ReportAnomaly
                        {
                            EntityType = "User",
                            EntityName = username,
                            Metric = "Productivity",
                            Current = currentCount,
                            Average = avg,
                            ChangePercent = avg > 0 ? (decimal)(currentCount - avg) / avg * 100 : 0,
                            Reason = $"{currentCount} docs (avg {avg:F0}, {(currentCount - avg) / avg * 100:F0}%)"
                        });
                }
            }
        }

        return anomalies.OrderByDescending(a => Math.Abs(a.ChangePercent)).ToList();
    }

    private static decimal StdDev(List<decimal> values)
    {
        if (values.Count < 2) return 0;
        var avg = values.Average();
        var sumSq = values.Sum(v => (v - avg) * (v - avg));
        return (decimal)Math.Sqrt((double)(sumSq / (values.Count - 1)));
    }
}
