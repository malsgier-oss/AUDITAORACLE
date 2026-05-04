using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

public interface IDiagnosticsService
{
    Task<DiagnosticsSnapshot> GetSnapshotAsync(bool bypassCache = false, CancellationToken cancellationToken = default);
    Task<HealthCheckResultSummary?> RunFullHealthCheckAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<LogEntryModel> GetFilteredLogs(LogFilter filter);
    IReadOnlyList<PerformanceMetricModel> GetPerformanceMetrics(DateTime sinceUtc, long minDurationMs);
    string ExportReportJson(DiagnosticsSnapshot snapshot);
    string ExportReportText(DiagnosticsSnapshot snapshot);
}

public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly IHealthCheckService _healthCheckService;
    private readonly IErrorLogAnalyzer _errorLogAnalyzer;
    private readonly IWorkflowMonitor _workflowMonitor;
    private readonly IServiceStatusMonitor _serviceStatusMonitor;
    private readonly IDatabaseMonitor _databaseMonitor;
    private readonly IConfigurationValidator _configurationValidator;
    private readonly IActivityTracker _activityTracker;
    private readonly ISessionMonitor _sessionMonitor;
    private readonly AppConfiguration _appConfiguration;
    private readonly IMigrationService _migrationService;
    private readonly IDocumentStore _documentStore;
    private readonly IDocumentAssignmentStore _assignmentStore;
    private readonly IConfigStore _configStore;
    private readonly IUserStore _userStore;

    private readonly ConcurrentDictionary<string, (DiagnosticsSnapshot Snapshot, DateTime AtUtc)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public DiagnosticsService(
        IHealthCheckService healthCheckService,
        IErrorLogAnalyzer errorLogAnalyzer,
        IWorkflowMonitor workflowMonitor,
        IServiceStatusMonitor serviceStatusMonitor,
        IDatabaseMonitor databaseMonitor,
        IConfigurationValidator configurationValidator,
        IActivityTracker activityTracker,
        ISessionMonitor sessionMonitor,
        AppConfiguration appConfiguration,
        IMigrationService migrationService,
        IDocumentStore documentStore,
        IDocumentAssignmentStore assignmentStore,
        IConfigStore configStore,
        IUserStore userStore)
    {
        _healthCheckService = healthCheckService;
        _errorLogAnalyzer = errorLogAnalyzer;
        _workflowMonitor = workflowMonitor;
        _serviceStatusMonitor = serviceStatusMonitor;
        _databaseMonitor = databaseMonitor;
        _configurationValidator = configurationValidator;
        _activityTracker = activityTracker;
        _sessionMonitor = sessionMonitor;
        _appConfiguration = appConfiguration;
        _migrationService = migrationService;
        _documentStore = documentStore;
        _assignmentStore = assignmentStore;
        _configStore = configStore;
        _userStore = userStore;
    }

    public async Task<DiagnosticsSnapshot> GetSnapshotAsync(bool bypassCache = false, CancellationToken cancellationToken = default)
    {
        var key = "default";
        if (!bypassCache && _cache.TryGetValue(key, out var e) && DateTime.UtcNow - e.AtUtc < CacheTtl)
            return e.Snapshot;

        var snap = await BuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
        _cache[key] = (snap, DateTime.UtcNow);
        return snap;
    }

    private async Task<DiagnosticsSnapshot> BuildSnapshotAsync(CancellationToken cancellationToken)
    {
        var snap = new DiagnosticsSnapshot();

        var logDir = LoggingService.LogDirectory;
        var now = DateTime.UtcNow;
        var since24 = now.AddHours(-24);
        var since7 = now.AddDays(-7);

        var entries = _errorLogAnalyzer.ParseMainLogs(logDir, since7);
        var last24 = entries.Where(x => x.TimestampUtc >= since24).ToList();
        snap.ErrorSummary.ErrorCount24h = last24.Count(x => x.Level is "ERR" or "FTL");
        snap.ErrorSummary.WarningCount24h = last24.Count(x => x.Level == "WRN");
        snap.ErrorSummary.ErrorCount7d = entries.Count(x => x.Level is "ERR" or "FTL");
        snap.ErrorSummary.ImportRelatedErrorCount24h = CountImportRelatedErrors(last24);
        snap.ErrorSummary.ErrorsByComponent = _errorLogAnalyzer.GetErrorCategoryCounts(entries, since7);
        snap.ErrorSummary.RecentErrors = last24
            .Where(x => x.Level is "ERR" or "FTL" or "WRN")
            .Take(500)
            .ToList();
        snap.ErrorSummary.TrendData = _errorLogAnalyzer.BuildHourlyTrend(entries, since24).ToList();

        try
        {
            var health = await _healthCheckService.PerformHealthCheckAsync().ConfigureAwait(false);
            snap.HealthChecks = MapHealth(health);
        }
        catch (Exception ex)
        {
            snap.HealthChecks = new HealthCheckResultSummary
            {
                TimestampUtc = now,
                IsHealthy = false,
                Checks =
                {
                    new HealthCheckSummary
                    {
                        Name = "HealthCheckService",
                        Category = "System",
                        IsHealthy = false,
                        Details = ex.Message
                    }
                }
            };
        }

        snap.WorkflowIssues = _workflowMonitor.DetectIssues(_appConfiguration);
        snap.ServiceStatuses = _serviceStatusMonitor.CheckAllServices();
        snap.DatabaseMetrics = _databaseMonitor.GetDatabaseMetrics(_migrationService);
        EnrichDatabaseMetricsFromLogs(snap.DatabaseMetrics, last24);
        snap.ConfigValidations = _configurationValidator.ValidateAll(_appConfiguration);
        snap.RecentActivity = _activityTracker.GetRecentActivity();
        snap.SessionMetrics = _sessionMonitor.GetSessionMetrics();
        snap.SystemStats = _workflowMonitor.BuildSystemStats(_documentStore, _assignmentStore, _configStore, _userStore);
        snap.SystemStats.MissingFiles = snap.WorkflowIssues.LongCount(w => w.Type == "MissingFile");
        snap.SystemStats.OrphanedFiles = snap.WorkflowIssues.LongCount(w => w.Type == "OrphanedFile");
        snap.SystemStats.FailedOcrCount = CountFailedOcrErrors(entries, since7);

        snap.OverallHealthStatus = ComputeOverallStatus(snap);

        await Task.CompletedTask.ConfigureAwait(false);
        return snap;
    }

    public async Task<HealthCheckResultSummary?> RunFullHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var r = await _healthCheckService.PerformHealthCheckAsync().ConfigureAwait(false);
        return MapHealth(r);
    }

    public IReadOnlyList<LogEntryModel> GetFilteredLogs(LogFilter filter)
    {
        var since = filter.SinceUtc ?? DateTime.UtcNow.AddDays(-7);
        var entries = _errorLogAnalyzer.ParseMainLogs(LoggingService.LogDirectory, since);
        return _errorLogAnalyzer.FilterLogs(entries, filter);
    }

    public IReadOnlyList<PerformanceMetricModel> GetPerformanceMetrics(DateTime sinceUtc, long minDurationMs)
    {
        var list = _errorLogAnalyzer.ParsePerformanceLogs(LoggingService.LogDirectory, sinceUtc);
        if (minDurationMs <= 0)
            return list;
        return list.Where(p => p.DurationMs >= minDurationMs).ToList();
    }

    public string ExportReportJson(DiagnosticsSnapshot snapshot) =>
        JsonConvert.SerializeObject(snapshot, Formatting.Indented);

    public string ExportReportText(DiagnosticsSnapshot snapshot)
    {
        var ic = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("AUDITA Diagnostics Report");
        sb.AppendLine(ic, $"Generated (UTC): {DateTime.UtcNow:O}");
        sb.AppendLine(ic, $"Overall: {snapshot.OverallHealthStatus}");
        sb.AppendLine();
        sb.AppendLine("=== Health ===");
        if (snapshot.HealthChecks != null)
        {
            foreach (var c in snapshot.HealthChecks.Checks)
                sb.AppendLine(ic, $"[{c.Category}] {c.Name}: {(c.IsHealthy ? "OK" : "ISSUE")} — {c.Details}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Errors (24h) ===");
        sb.AppendLine(ic, $"Errors: {snapshot.ErrorSummary.ErrorCount24h}, Warnings: {snapshot.ErrorSummary.WarningCount24h}");
        sb.AppendLine(ic, $"Import-related (24h, heuristic): {snapshot.ErrorSummary.ImportRelatedErrorCount24h}");

        sb.AppendLine();
        sb.AppendLine("=== Workflow issues ===");
        foreach (var w in snapshot.WorkflowIssues.Take(200))
            sb.AppendLine(ic, $"[{w.Severity}] {w.Type}: {w.Description}");

        sb.AppendLine();
        sb.AppendLine("=== Services ===");
        foreach (var s in snapshot.ServiceStatuses)
            sb.AppendLine(ic, $"{s.ServiceName}: {s.Status} — {s.Details}");

        sb.AppendLine();
        sb.AppendLine("=== Database ===");
        sb.AppendLine(ic, $"Connected: {snapshot.DatabaseMetrics.IsConnected}; Schema: {snapshot.DatabaseMetrics.SchemaVersion}");
        sb.AppendLine(ic, $"Log hints (DB-related ERR in 24h): {snapshot.DatabaseMetrics.LogDatabaseIssueCount24h}");
        sb.AppendLine(ic, $"v$session total (if available): {snapshot.DatabaseMetrics.OracleVSessionTotal?.ToString(ic) ?? "n/a"}");
        sb.AppendLine(ic, $"v$session ACTIVE (if available): {snapshot.DatabaseMetrics.OracleVSessionActive?.ToString(ic) ?? "n/a"}");

        return sb.ToString();
    }

    private static int CountImportRelatedErrors(IEnumerable<LogEntryModel> last24h)
    {
        return last24h.Count(e =>
        {
            if (e.Level is not ("ERR" or "FTL")) return false;
            var m = e.Message;
            if (m.Contains("import", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.Contains("folder watch", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.Contains("FolderWatch", StringComparison.OrdinalIgnoreCase)) return true;
            if (m.Contains("merge queue", StringComparison.OrdinalIgnoreCase)) return true;
            return m.Contains("merge", StringComparison.OrdinalIgnoreCase)
                   && m.Contains("fail", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static int CountFailedOcrErrors(IEnumerable<LogEntryModel> entries, DateTime sinceUtc)
    {
        return entries.Count(e =>
            e.TimestampUtc >= sinceUtc
            && e.Level is "ERR" or "FTL"
            && (e.Message.Contains("OCR", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("Tesseract", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("ocr", StringComparison.OrdinalIgnoreCase)));
    }

    private static void EnrichDatabaseMetricsFromLogs(DatabaseMetrics metrics, IReadOnlyList<LogEntryModel> last24h)
    {
        var n = last24h.Count(IsDatabaseRelatedLogLine);
        metrics.LogDatabaseIssueCount24h = n;
        if (n > 0)
            metrics.Warnings.Add($"Logs show {n} database-related error line(s) in the last 24h (ORA- / timeout / Oracle).");
    }

    private static bool IsDatabaseRelatedLogLine(LogEntryModel e)
    {
        if (e.Level is not ("ERR" or "FTL")) return false;
        var m = e.Message;
        if (m.Contains("ORA-", StringComparison.OrdinalIgnoreCase)) return true;
        if (m.Contains("Oracle", StringComparison.OrdinalIgnoreCase)) return true;
        if (m.Contains("timeout", StringComparison.OrdinalIgnoreCase)) return true;
        if (m.Contains("TNS", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string ComputeOverallStatus(DiagnosticsSnapshot s)
    {
        if (!s.DatabaseMetrics.IsConnected)
            return "Critical";
        if (s.WorkflowIssues.Any(x => x.Severity == "Error"))
            return "Critical";
        if (s.ErrorSummary.ErrorCount24h > 20 || s.WorkflowIssues.Count > 25)
            return "Warning";
        if (!s.ConfigValidations.All(x => x.IsValid || x.Severity != "Error"))
            return "Warning";
        if (s.HealthChecks != null && !s.HealthChecks.IsHealthy)
            return "Warning";
        return "Healthy";
    }

    private static HealthCheckResultSummary MapHealth(HealthCheckResult r) =>
        new()
        {
            TimestampUtc = r.Timestamp.ToUniversalTime(),
            IsHealthy = r.IsHealthy,
            CheckDurationMs = r.CheckDurationMs,
            Checks = r.Checks.Select(c => new HealthCheckSummary
            {
                Name = c.Name,
                Category = c.Category,
                IsHealthy = c.IsHealthy,
                Details = c.Details
            }).ToList()
        };
}
