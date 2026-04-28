using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>Period-over-period document volume analysis for executive reporting.</summary>
public interface IComparativeAnalysisService
{
    /// <summary>Current period vs same calendar window one year prior.</summary>
    ComparisonResult CompareYearOverYear(DateTime currentFrom, DateTime currentTo, string? branch = null, string? section = null, string? engagement = null);

    /// <summary>Current period vs immediately preceding period of same length (aligned to prior quarter when possible).</summary>
    ComparisonResult CompareQuarterOverQuarter(DateTime currentFrom, DateTime currentTo, string? branch = null, string? section = null, string? engagement = null);

    /// <summary>Current period vs prior month window of the same length.</summary>
    ComparisonResult CompareMonthOverMonth(DateTime currentFrom, DateTime currentTo, string? branch = null, string? section = null, string? engagement = null);

    /// <summary>Map numeric change to trend and optional percentage change.</summary>
    TrendAnalysis AnalyzeTrend(decimal current, decimal previous);
}
