using System.Globalization;
using System.Text.Json;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Service for comparing different versions of reports.
/// </summary>
public interface IReportComparisonService
{
    /// <summary>Get all versions of a report (parent and children).</summary>
    List<ReportHistory> GetReportVersions(string parentReportId);
    
    /// <summary>Compare two report configurations.</summary>
    ReportConfigComparison CompareConfigs(ReportHistory report1, ReportHistory report2);
    
    /// <summary>Get metadata differences between two reports.</summary>
    Dictionary<string, (string? oldValue, string? newValue)> GetMetadataDifferences(ReportHistory oldReport, ReportHistory newReport);
}

public class ReportConfigComparison
{
    public List<string> Differences { get; set; } = new();
    public bool AreIdentical => Differences.Count == 0;
    public ReportConfig? Config1 { get; set; }
    public ReportConfig? Config2 { get; set; }
}

public class ReportComparisonService : IReportComparisonService
{
    private readonly IReportHistoryStore _historyStore;

    public ReportComparisonService(IReportHistoryStore historyStore)
    {
        _historyStore = historyStore;
    }

    public List<ReportHistory> GetReportVersions(string parentReportId)
    {
        var allReports = _historyStore.List(limit: 1000);
        
        var versions = allReports
            .Where(r => r.Uuid == parentReportId || r.ParentReportId == parentReportId)
            .OrderBy(r => r.Version ?? 0)
            .ToList();

        return versions;
    }

    public ReportConfigComparison CompareConfigs(ReportHistory report1, ReportHistory report2)
    {
        var comparison = new ReportConfigComparison();

        if (string.IsNullOrEmpty(report1.ConfigJson) || string.IsNullOrEmpty(report2.ConfigJson))
        {
            comparison.Differences.Add("One or both reports have no configuration data");
            return comparison;
        }

        try
        {
            var config1 = JsonSerializer.Deserialize<ReportConfig>(report1.ConfigJson);
            var config2 = JsonSerializer.Deserialize<ReportConfig>(report2.ConfigJson);

            if (config1 == null || config2 == null)
            {
                comparison.Differences.Add("Failed to deserialize configurations");
                return comparison;
            }

            comparison.Config1 = config1;
            comparison.Config2 = config2;

            if (config1.ReportType != config2.ReportType)
                comparison.Differences.Add($"Report Type: {config1.ReportType} → {config2.ReportType}");

            if (config1.DateFrom != config2.DateFrom)
                comparison.Differences.Add($"Start Date: {config1.DateFrom:yyyy-MM-dd} → {config2.DateFrom:yyyy-MM-dd}");

            if (config1.DateTo != config2.DateTo)
                comparison.Differences.Add($"End Date: {config1.DateTo:yyyy-MM-dd} → {config2.DateTo:yyyy-MM-dd}");

            if (config1.Branch != config2.Branch)
                comparison.Differences.Add($"Branch: '{config1.Branch}' → '{config2.Branch}'");

            if (config1.Section != config2.Section)
                comparison.Differences.Add($"Section: '{config1.Section}' → '{config2.Section}'");

            if (config1.Status != config2.Status)
                comparison.Differences.Add($"Status: '{config1.Status}' → '{config2.Status}'");

            if (config1.Format != config2.Format)
                comparison.Differences.Add($"Format: {config1.Format} → {config2.Format}");

            if (config1.IncludeCharts != config2.IncludeCharts)
                comparison.Differences.Add($"Include Charts: {config1.IncludeCharts} → {config2.IncludeCharts}");

            if (config1.ExportPerBranch != config2.ExportPerBranch)
                comparison.Differences.Add($"Export Per Branch: {config1.ExportPerBranch} → {config2.ExportPerBranch}");

            if (config1.Watermark != config2.Watermark)
                comparison.Differences.Add($"Watermark: {config1.Watermark} → {config2.Watermark}");
        }
        catch (Exception ex)
        {
            comparison.Differences.Add($"Error comparing configurations: {ex.Message}");
        }

        return comparison;
    }

    public Dictionary<string, (string? oldValue, string? newValue)> GetMetadataDifferences(ReportHistory oldReport, ReportHistory newReport)
    {
        var differences = new Dictionary<string, (string? oldValue, string? newValue)>();

        if (oldReport.ReportType != newReport.ReportType)
            differences["ReportType"] = (oldReport.ReportType, newReport.ReportType);

        if (oldReport.Username != newReport.Username)
            differences["Username"] = (oldReport.Username, newReport.Username);

        if (oldReport.Tags != newReport.Tags)
            differences["Tags"] = (oldReport.Tags, newReport.Tags);

        if (oldReport.Purpose != newReport.Purpose)
            differences["Purpose"] = (oldReport.Purpose, newReport.Purpose);

        if (oldReport.Description != newReport.Description)
            differences["Description"] = (oldReport.Description, newReport.Description);

        if ((oldReport.Version ?? 0) != (newReport.Version ?? 0))
            differences["Version"] = (oldReport.Version?.ToString(CultureInfo.InvariantCulture), newReport.Version?.ToString(CultureInfo.InvariantCulture));

        return differences;
    }
}
