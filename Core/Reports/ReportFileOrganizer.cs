using System.IO;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Organizes report files into a structured folder hierarchy.
/// Creates folders like: Reports/2026/02-February/ReportType_20260215_083045.pdf
/// </summary>
public interface IReportFileOrganizer
{
    string GetOrganizedPath(ReportConfig config, ReportFormat format);
    string EnsureFoldersExist(string filePath);
    string GetBaseReportsFolder();
}

public class ReportFileOrganizer : IReportFileOrganizer
{
    private readonly ILogger _log = LoggingService.ForContext<ReportFileOrganizer>();
    private readonly IConfigStore _configStore;
    private readonly string _defaultBasePath;

    public ReportFileOrganizer(IConfigStore configStore)
    {
        _configStore = configStore;
        _defaultBasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "WorkAudit",
            "Reports"
        );
    }

    public string GetBaseReportsFolder()
    {
        var basePath = _configStore.GetSettingValue("reports_base_folder", "")?.Trim();
        
        if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
        {
            basePath = _defaultBasePath;
        }

        if (!Directory.Exists(basePath))
        {
            try
            {
                Directory.CreateDirectory(basePath);
                _log.Information("Created reports base folder: {Path}", basePath);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Could not create reports base folder: {Path}", basePath);
                return Path.GetTempPath();
            }
        }

        return basePath;
    }

    public string GetOrganizedPath(ReportConfig config, ReportFormat format)
    {
        var basePath = GetBaseReportsFolder();
        var now = DateTime.Now;

        // Organize by year/month: 2026/02-February
        var organizationMode = _configStore.GetSettingValue("reports_organization_mode", "month") ?? "month";
        
        string subFolder;
        if (organizationMode == "quarter")
        {
            var quarter = (now.Month - 1) / 3 + 1;
            subFolder = Path.Combine(
                now.Year.ToString(),
                $"Q{quarter}"
            );
        }
        else
        {
            subFolder = Path.Combine(
                now.Year.ToString(),
                $"{now.Month:D2}-{now:MMMM}"
            );
        }

        // Build filename: ReportType_DateRange_Timestamp.ext
        var dateRange = $"{config.DateFrom:yyyyMMdd}_{config.DateTo:yyyyMMdd}";
        var timestamp = now.ToString("HHmmss");
        var extension = format switch
        {
            ReportFormat.Excel => ".xlsx",
            ReportFormat.Csv => ".csv",
            _ => ".pdf"
        };

        // Handle per-branch export (returns folder path instead of file path)
        if (config.ExportPerBranch)
        {
            if (config.ZipPerBranch)
            {
                var zipFileName = $"{config.ReportType}_PerBranch_{dateRange}_{timestamp}.zip";
                return Path.Combine(basePath, subFolder, zipFileName);
            }
            else
            {
                var folderName = $"{config.ReportType}_PerBranch_{dateRange}_{timestamp}";
                return Path.Combine(basePath, subFolder, folderName);
            }
        }

        var fileName = $"{config.ReportType}_{dateRange}_{timestamp}{extension}";
        var fullPath = Path.Combine(basePath, subFolder, fileName);

        return fullPath;
    }

    public string EnsureFoldersExist(string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _log.Debug("Created directory: {Directory}", directory);
            }
            return filePath;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Could not create directory for: {Path}", filePath);
            // Fallback to temp
            var fileName = Path.GetFileName(filePath);
            return Path.Combine(Path.GetTempPath(), fileName);
        }
    }
}
