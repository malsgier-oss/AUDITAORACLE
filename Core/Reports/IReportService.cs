using WorkAudit.Domain;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Service for generating bank-grade reports.
/// </summary>
public interface IReportService
{
    /// <summary>Generate report and return path to output file.</summary>
    string Generate(ReportConfig config);

    /// <summary>Generate report asynchronously with progress reporting and cancellation support.</summary>
    Task<string> GenerateAsync(ReportConfig config, IProgress<ReportProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Get date range from period preset.</summary>
    (DateTime From, DateTime To) GetDateRangeFromPreset(ReportPeriod preset);
}
