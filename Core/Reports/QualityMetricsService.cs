using System.Globalization;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Quality metrics and SLA tracking for reports.
/// </summary>
public interface IQualityMetricsService
{
    QualityMetricsResult GetMetrics(IDocumentStore store, DateTime from, DateTime to, string? branch = null, string? section = null);
}

public class QualityMetricsResult
{
    public int TotalProcessed { get; set; }
    public int WithOcrConfidenceAbove90 { get; set; }
    public decimal OcrAccuracyPercent { get; set; }
    public int WithClassificationConfidenceAbove90 { get; set; }
    public decimal ClassificationAccuracyPercent { get; set; }
    public decimal AvgProcessingTimeHours { get; set; }
    public int BacklogCount { get; set; }
    public decimal AvgBacklogAgeDays { get; set; }
    public int ReviewedWithin24h { get; set; }
    public int TotalReviewed { get; set; }
    public decimal SlaCompliancePercent { get; set; }
}

public class QualityMetricsService : IQualityMetricsService
{
    private const int MaxDocuments = 50_000;
    private const double OcrThreshold = 0.9;
    private const double ClassThreshold = 0.9;
    private const double SlaHours = 24;

    public QualityMetricsResult GetMetrics(IDocumentStore store, DateTime from, DateTime to, string? branch = null, string? section = null)
    {
        var fromStr = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toStr = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59";
        var docs = store.ListDocuments(dateFrom: fromStr, dateTo: toStr, limit: MaxDocuments, newestFirst: true);
        var list = docs.AsEnumerable();
        if (!string.IsNullOrEmpty(branch)) list = list.Where(d => d.Branch == branch);
        if (!string.IsNullOrEmpty(section)) list = list.Where(d => d.Section == section);
        var docList = list.ToList();

        var result = new QualityMetricsResult { TotalProcessed = docList.Count };

        var withOcr = docList.Where(d => d.Confidence.HasValue).ToList();
        result.WithOcrConfidenceAbove90 = withOcr.Count(d => d.Confidence >= OcrThreshold);
        result.OcrAccuracyPercent = withOcr.Count > 0 ? (decimal)result.WithOcrConfidenceAbove90 / withOcr.Count * 100 : 0;

        var withClass = docList.Where(d => d.ClassificationConfidence.HasValue).ToList();
        result.WithClassificationConfidenceAbove90 = withClass.Count(d => d.ClassificationConfidence >= ClassThreshold);
        result.ClassificationAccuracyPercent = withClass.Count > 0 ? (decimal)result.WithClassificationConfidenceAbove90 / withClass.Count * 100 : 0;

        var cleared = docList.Where(d => d.Status == Enums.Status.Cleared && !string.IsNullOrEmpty(d.CaptureTime)).ToList();
        var processingTimes = new List<double>();
        foreach (var d in cleared)
        {
            if (!DateTime.TryParse(d.CaptureTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var capture))
                continue;
            var endStr = d.ReviewedAt ?? d.UpdatedAt ?? d.CaptureTime;
            if (string.IsNullOrEmpty(endStr)) continue;
            if (!DateTime.TryParse(endStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var end))
                continue;
            var hours = (end - capture).TotalHours;
            if (hours >= 0 && hours < 365 * 24) processingTimes.Add(hours);
        }
        result.AvgProcessingTimeHours = processingTimes.Count > 0 ? (decimal)processingTimes.Average() : 0;

        var draft = docList.Where(d => d.Status == Enums.Status.Draft).ToList();
        result.BacklogCount = draft.Count;
        var backlogAges = new List<double>();
        var now = DateTime.UtcNow;
        foreach (var d in draft.Where(d => !string.IsNullOrEmpty(d.CaptureTime)))
        {
            if (DateTime.TryParse(d.CaptureTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var capture))
                backlogAges.Add((now - capture).TotalDays);
        }
        result.AvgBacklogAgeDays = backlogAges.Count > 0 ? (decimal)backlogAges.Average() : 0;

        var reviewed = docList.Where(d => !string.IsNullOrEmpty(d.ReviewedAt) && !string.IsNullOrEmpty(d.CaptureTime)).ToList();
        result.TotalReviewed = reviewed.Count;
        result.ReviewedWithin24h = reviewed.Count(d =>
        {
            if (!DateTime.TryParse(d.CaptureTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var capture))
                return false;
            if (!DateTime.TryParse(d.ReviewedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var reviewedAt))
                return false;
            return (reviewedAt - capture).TotalHours <= SlaHours;
        });
        result.SlaCompliancePercent = result.TotalReviewed > 0 ? (decimal)result.ReviewedWithin24h / result.TotalReviewed * 100 : 0;

        return result;
    }
}
