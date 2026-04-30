using System.Globalization;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Validates report configuration before generation.
/// </summary>
public interface IReportValidationService
{
    ValidationResult ValidateConfig(ReportConfig config);
    Task<int> GetDocumentCountAsync(ReportConfig config);
}

public class ReportValidationService : IReportValidationService
{
    private readonly IDocumentStore _documentStore;

    public ReportValidationService(IDocumentStore documentStore)
    {
        _documentStore = documentStore;
    }

    public ValidationResult ValidateConfig(ReportConfig config)
    {
        var result = new ValidationResult();

        // Validate date range
        var dateRangeResult = ValidateDateRange(config.DateFrom, config.DateTo);
        result.AddMessages(dateRangeResult);

        // Validate format compatibility
        var formatResult = ValidateFormatCompatibility(config.ReportType, config.Format);
        result.AddMessages(formatResult);

        return result;
    }

    private ValidationResult ValidateDateRange(DateTime from, DateTime to)
    {
        var result = new ValidationResult();

        if (from > to)
        {
            result.AddError("Start date must be before end date.");
            return result;
        }

        var days = (to - from).TotalDays;

        if (days < 1)
        {
            result.AddWarning("Date range is less than 1 day. This may produce an empty report.");
        }

        if (days > 730)
        {
            result.AddError("Date range cannot exceed 2 years (730 days). Please narrow the range.");
        }

        return result;
    }

    private ValidationResult ValidateFormatCompatibility(ReportType reportType, ReportFormat format)
    {
        var result = new ValidationResult();

        // CSV only supported for specific report types
        if (format == ReportFormat.Csv)
        {
            if (reportType is not (ReportType.DailySummary or ReportType.BranchSummary or ReportType.SectionSummary))
            {
                result.AddError($"CSV export is not supported for {reportType}. Available for: Daily Summary, Branch Summary, Section Summary.");
            }
        }

        // Excel supported for specific types
        if (format == ReportFormat.Excel)
        {
            if (reportType is not (ReportType.UserActivity or ReportType.AssignmentSummary or 
                ReportType.BranchSummary or ReportType.SectionSummary or ReportType.StatusSummary or 
                ReportType.DocumentTypeSummary))
            {
                result.AddError($"Excel export is not supported for {reportType}.");
            }
        }

        // Executive Summary and Audit Trail: PDF only
        if (reportType is ReportType.ExecutiveSummary or ReportType.AuditTrail && format != ReportFormat.Pdf)
        {
            result.AddError($"{reportType} is available in PDF format only.");
        }

        return result;
    }

    /// <summary>True when the named report type actually narrows by status (matches the generator).</summary>
    private static bool ReportSupportsStatusFilter(ReportType type) => type is
        ReportType.BranchSummary or
        ReportType.SectionSummary or
        ReportType.DocumentTypeSummary;

    /// <summary>True when the named report type actually narrows by document type (matches the generator).</summary>
    private static bool ReportSupportsDocumentTypeFilter(ReportType type) => type is
        ReportType.DocumentTypeSummary;

    /// <summary>True when the named report type narrows by user (matches the generator).</summary>
    private static bool ReportSupportsUserFilter(ReportType type) => type is
        ReportType.UserActivity;

    public async Task<int> GetDocumentCountAsync(ReportConfig config)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Filters here must mirror what the actual report generators apply (see Phase 2c plan
                // entry). When the validation count and the generator output disagree, the user sees
                // "preview ~N documents" but a report with very different totals.
                var docs = _documentStore.ListDocuments(
                    dateFrom: config.DateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    dateTo: config.DateTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59",
                    branch: config.Branch,
                    section: config.Section,
                    status: ReportSupportsStatusFilter(config.ReportType) ? config.Status : null,
                    documentType: ReportSupportsDocumentTypeFilter(config.ReportType) ? config.DocumentType : null,
                    engagement: config.Engagement,
                    createdOrReviewedBy: ReportSupportsUserFilter(config.ReportType) ? config.UserFilter : null,
                    limit: 100000,
                    newestFirst: true
                );
                return docs.Count;
            }
            catch
            {
                return -1; // Error getting count
            }
        });
    }
}

/// <summary>
/// Result of validation with errors and warnings.
/// </summary>
public class ValidationResult
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    public bool IsValid => Errors.Count == 0;
    public bool HasWarnings => Warnings.Count > 0;

    public void AddError(string message) => Errors.Add(message);
    public void AddWarning(string message) => Warnings.Add(message);

    public void AddMessages(ValidationResult other)
    {
        Errors.AddRange(other.Errors);
        Warnings.AddRange(other.Warnings);
    }

    public string GetErrorMessage() => string.Join("\n", Errors);
    public string GetWarningMessage() => string.Join("\n", Warnings);
}
