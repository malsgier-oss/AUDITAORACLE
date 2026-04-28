namespace WorkAudit.Core.Reports;

/// <summary>Shared YoY / QoQ / MoM one-line summaries for executive and performance reports.</summary>
public static class ComparativePeriodSummaryText
{
    public static string Format(ComparisonResult? yoy, ComparisonResult? qoq, ComparisonResult? mom, bool isArabic)
    {
        var parts = new List<string>();
        if (yoy != null && (yoy.PreviousPeriodTotal > 0 || yoy.CurrentPeriodTotal > 0))
        {
            if (isArabic)
                parts.Add($"مقارنة سنوية: {yoy.CurrentPeriodTotal} سابق {yoy.PreviousPeriodTotal} (Δ {yoy.PercentChange:0.0}٪).");
            else
                parts.Add($"Year over year: current {yoy.CurrentPeriodTotal}, prior {yoy.PreviousPeriodTotal} (Δ {(yoy.PercentChange >= 0 ? "+" : "")}{yoy.PercentChange:0.0}%).");
        }
        if (qoq != null && (qoq.PreviousPeriodTotal > 0 || qoq.CurrentPeriodTotal > 0))
        {
            if (isArabic)
                parts.Add($"ربع على ربع: {qoq.CurrentPeriodTotal} مسبق {qoq.PreviousPeriodTotal} (Δ {qoq.PercentChange:0.0}٪).");
            else
                parts.Add($"Quarter over quarter: {qoq.CurrentPeriodTotal} vs prior {qoq.PreviousPeriodTotal} (Δ {(qoq.PercentChange >= 0 ? "+" : "")}{qoq.PercentChange:0.0}%).");
        }
        if (mom != null && (mom.PreviousPeriodTotal > 0 || mom.CurrentPeriodTotal > 0))
        {
            if (isArabic)
                parts.Add($"شهريًا: {mom.CurrentPeriodTotal} مسبق {mom.PreviousPeriodTotal} (Δ {mom.PercentChange:0.0}٪).");
            else
                parts.Add($"Month over month: {mom.CurrentPeriodTotal} vs prior {mom.PreviousPeriodTotal} (Δ {(mom.PercentChange >= 0 ? "+" : "")}{mom.PercentChange:0.0}%).");
        }
        return string.Join(" ", parts);
    }
}
