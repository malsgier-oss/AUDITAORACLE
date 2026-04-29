using System.Globalization;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>Uses document list counts to produce YoY, QoQ, and MoM comparisons for executive reports.</summary>
public sealed class ComparativeAnalysisService : IComparativeAnalysisService
{
    private const int MaxList = 100_000;
    private readonly IDocumentStore _store;

    public ComparativeAnalysisService(IDocumentStore store)
    {
        _store = store;
    }

    public ComparisonResult CompareYearOverYear(DateTime currentFrom, DateTime currentTo, string? branch = null, string? section = null, string? engagement = null)
    {
        var priorFrom = currentFrom.AddYears(-1);
        var priorTo = currentTo.AddYears(-1);
        return Compare(currentFrom, currentTo, priorFrom, priorTo, branch, section, engagement);
    }

    public ComparisonResult CompareQuarterOverQuarter(DateTime currentFrom, DateTime currentTo, string? branch = null, string? section = null, string? engagement = null)
    {
        var days = (currentTo - currentFrom).Days;
        if (days < 0) days = 0;
        var priorTo = currentFrom.AddDays(-1);
        var priorFrom = priorTo.AddDays(-days);
        return Compare(currentFrom, currentTo, priorFrom, priorTo, branch, section, engagement);
    }

    public ComparisonResult CompareMonthOverMonth(DateTime currentFrom, DateTime currentTo, string? branch = null, string? section = null, string? engagement = null)
    {
        var days = (currentTo - currentFrom).Days;
        if (days < 0) days = 0;
        var priorTo = currentFrom.AddDays(-1);
        var priorFrom = priorTo.AddDays(-Math.Max(0, days));
        return Compare(currentFrom, currentTo, priorFrom, priorTo, branch, section, engagement);
    }

    public TrendAnalysis AnalyzeTrend(decimal current, decimal previous)
    {
        if (previous <= 0)
            return new TrendAnalysis(TrendDirection.NoComparison, null);

        var pct = (current - previous) / previous * 100;
        if (Math.Abs(pct) < 1) return new TrendAnalysis(TrendDirection.Stable, pct, null);
        return pct > 0
            ? new TrendAnalysis(TrendDirection.Improving, pct, "up")
            : new TrendAnalysis(TrendDirection.Declining, pct, "down");
    }

    private ComparisonResult Compare(
        DateTime curFrom, DateTime curTo, DateTime prevFrom, DateTime prevTo,
        string? branch, string? section, string? engagement)
    {
        var current = CountDocuments(curFrom, curTo, branch, section, engagement);
        var previous = CountDocuments(prevFrom, prevTo, branch, section, engagement);
        var r = new ComparisonResult
        {
            CurrentPeriodTotal = current,
            PreviousPeriodTotal = previous
        };
        if (previous <= 0)
        {
            r.PercentChange = 0;
            r.Trend = current > 0 ? TrendDirection.Improving : TrendDirection.NoComparison;
            return r;
        }
        r.PercentChange = (decimal)(current - previous) / previous * 100;
        r.Trend = AnalyzeTrend(current, previous).Direction;
        return r;
    }

    private int CountDocuments(DateTime from, DateTime to, string? branch, string? section, string? engagement)
    {
        var fromStr = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toStr = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59";
        return _store.ListDocuments(
            dateFrom: fromStr,
            dateTo: toStr,
            branch: branch,
            section: section,
            engagement: engagement,
            status: null,
            limit: MaxList).Count;
    }
}
