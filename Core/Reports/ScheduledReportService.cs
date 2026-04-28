using System.IO;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Generates reports on a schedule when the app is running (e.g. daily at 08:00).
/// </summary>
public interface IScheduledReportService
{
    void Start();
    void Stop();
    bool IsRunning { get; }
    DateTime? LastReportAt { get; }
}

public class ScheduledReportService : IScheduledReportService
{
    private readonly ILogger _log = LoggingService.ForContext<ScheduledReportService>();
    private readonly IConfigStore _configStore;
    private readonly IReportService _reportService;
    private readonly IReportEmailService _emailService;
    private System.Threading.Timer? _timer;
    private DateTime? _lastReportAt;
    private DateTime _lastRunDate = DateTime.MinValue;

    public ScheduledReportService(IConfigStore configStore, IReportService reportService, IReportEmailService emailService)
    {
        _configStore = configStore;
        _reportService = reportService;
        _emailService = emailService;
    }

    public bool IsRunning => _timer != null;
    public DateTime? LastReportAt => _lastReportAt;

    public void Start()
    {
        if (_timer != null) return;

        _log.Information("Scheduled report service started (checks every minute)");
        _timer = new System.Threading.Timer(
            _ => _ = CheckAndRunAsync(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _log.Information("Scheduled report service stopped");
    }

    private async Task CheckAndRunAsync()
    {
        try
        {
            if (!_configStore.GetSettingBool("scheduled_reports_enabled", false))
                return;

            var timeStr = _configStore.GetSettingValue("scheduled_report_time", "08:00") ?? "08:00";
            if (!TimeSpan.TryParse(timeStr, out var targetTime))
            {
                _log.Warning("Invalid scheduled_report_time format: {Time}", timeStr);
                return;
            }

            var now = DateTime.Now;
            var targetToday = DateTime.Today.Add(targetTime);

            // Run on first tick after we've reached the target time today (haven't run yet)
            if (now.Date != _lastRunDate.Date && now >= targetToday)
            {
                _lastRunDate = now.Date;
                await RunScheduledReportAsync();
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Scheduled report check error");
        }
    }

    private async Task RunScheduledReportAsync()
    {
        try
        {
            var reportTypeStr = _configStore.GetSettingValue("scheduled_report_type", "Performance") ?? "Performance";
            var reportType = ParseReportType(reportTypeStr);

            var outputDir = _configStore.GetSettingValue("scheduled_report_output_dir", "")?.Trim();
            var config = new ReportConfig
            {
                DateFrom = DateTime.Today.AddMonths(-1),
                DateTo = DateTime.Today,
                Preset = ReportPeriod.Monthly,
                Format = ReportFormat.Pdf,
                IncludeCharts = true,
                ReportType = reportType,
                ReportTemplate = ReportTemplate.Executive,
                OutputPath = BuildScheduledOutputPath(reportType, outputDir)
            };

            _log.Information("Running scheduled report: {ReportType}", reportType);
            var path = await _reportService.GenerateAsync(config);

            if (!string.IsNullOrEmpty(path))
            {
                _lastReportAt = DateTime.UtcNow;
                _log.Information("Scheduled report completed: {Path}", path);

                var emailRecipients = _configStore.GetSettingValue("scheduled_report_email_recipients", "")?.Trim();
                if (!string.IsNullOrEmpty(emailRecipients))
                {
                    try
                    {
                        if (_emailService.SendReport(path, reportType.ToString()))
                            _log.Information("Scheduled report emailed successfully");
                        else
                            _log.Warning("Scheduled report email failed or not configured");
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "Scheduled report email failed");
                    }
                }
            }
            else
            {
                _log.Warning("Scheduled report generation returned no path");
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Scheduled report generation failed");
        }
    }

    private static string? BuildScheduledOutputPath(ReportType reportType, string? outputDir)
    {
        if (string.IsNullOrWhiteSpace(outputDir) || !Directory.Exists(outputDir)) return null;
        var today = DateTime.Today;
        var filename = $"WorkAudit_{reportType}_{today:yyyyMMdd}.pdf";
        return Path.Combine(outputDir, filename);
    }

    private static ReportType ParseReportType(string value)
    {
        return value switch
        {
            "Performance" => ReportType.Performance,
            "ExecutiveSummary" => ReportType.ExecutiveSummary,
            "BranchSummary" => ReportType.BranchSummary,
            "IssuesAndFocus" => ReportType.IssuesAndFocus,
            _ => ReportType.Performance
        };
    }
}
