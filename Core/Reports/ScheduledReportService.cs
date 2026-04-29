using System.IO;
using System.Globalization;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;
using WorkAudit.Storage.Oracle;

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
    private readonly ISchedulerLockStore? _lockStore;
    private readonly string _holderId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private System.Threading.Timer? _timer;
    private DateTime? _lastReportAt;

    public ScheduledReportService(
        IConfigStore configStore,
        IReportService reportService,
        IReportEmailService emailService,
        ISchedulerLockStore? lockStore = null)
    {
        _configStore = configStore;
        _reportService = reportService;
        _emailService = emailService;
        _lockStore = lockStore;
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

            // Cluster-wide: only one successful run per calendar day across all PCs sharing the DB.
            var todayKey = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var lastRunDate = _configStore.GetSettingValue("scheduled_report_last_run_date", "") ?? "";
            if (string.Equals(lastRunDate, todayKey, StringComparison.Ordinal))
                return;

            if (now < targetToday)
                return;

            var leaderElection = _configStore.GetSettingBool("scheduler_leader_election_enabled", true);
            var leaseMinutes = Math.Max(1, _configStore.GetSettingInt("scheduler_lock_lease_minutes", 15));
            var acquired = true;
            if (leaderElection && _lockStore != null)
            {
                acquired = _lockStore.TryAcquireOrRenew(
                    "scheduled_report",
                    _holderId,
                    TimeSpan.FromMinutes(leaseMinutes));
                if (!acquired)
                {
                    _log.Debug("Scheduled report skipped: another instance holds the scheduler lock");
                    return;
                }
            }

            try
            {
                lastRunDate = _configStore.GetSettingValue("scheduled_report_last_run_date", "") ?? "";
                if (string.Equals(lastRunDate, todayKey, StringComparison.Ordinal))
                    return;

                await RunScheduledReportAsync(todayKey).ConfigureAwait(false);
            }
            finally
            {
                if (leaderElection && _lockStore != null && acquired)
                    _lockStore.ReleaseIfHolder("scheduled_report", _holderId);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Scheduled report check error");
        }
    }

    private async Task RunScheduledReportAsync(string todayKey)
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
            var path = await _reportService.GenerateAsync(config).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(path))
            {
                _lastReportAt = DateTime.UtcNow;
                _log.Information("Scheduled report completed: {Path}", path);
                _ = _configStore.SetSetting("scheduled_report_last_run_date", todayKey, "ScheduledReportService");

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
