namespace WorkAudit.Core.Reports;

/// <summary>Period-over-period trend direction for comparative metrics.</summary>
public enum TrendDirection
{
    Improving,
    Declining,
    Stable,
    NoComparison
}

/// <summary>Result of comparing document volumes between two periods.</summary>
public sealed class ComparisonResult
{
    public int CurrentPeriodTotal { get; set; }
    public int PreviousPeriodTotal { get; set; }
    public decimal PercentChange { get; set; }
    public TrendDirection Trend { get; set; } = TrendDirection.NoComparison;
}

/// <summary>Output of <see cref="IComparativeAnalysisService.AnalyzeTrend"/>.</summary>
public readonly struct TrendAnalysis
{
    public TrendAnalysis(TrendDirection direction, decimal? percentChange, string? label = null)
    {
        Direction = direction;
        PercentChange = percentChange;
        Label = label;
    }

    public TrendDirection Direction { get; }
    public decimal? PercentChange { get; }
    public string? Label { get; }
}
