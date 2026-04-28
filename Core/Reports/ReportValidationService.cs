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

    public async Task<int> GetDocumentCountAsync(ReportConfig config)
    {
        return await Task.Run(() =>
        {
            try
            {
                var docs = _documentStore.ListDocuments(
                    dateFrom: config.DateFrom.ToString("yyyy-MM-dd"),
                    dateTo: config.DateTo.ToString("yyyy-MM-dd") + "T23:59:59",
                    branch: config.Branch,
                    section: config.Section,
                    status: config.Status,
                    documentType: config.DocumentType,
                    engagement: config.Engagement,
                    limit: 100000
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
