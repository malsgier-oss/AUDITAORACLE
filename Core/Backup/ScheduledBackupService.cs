using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Storage;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Core.Backup;

/// <summary>
/// Runs automatic backups on a schedule based on <c>app_settings</c> (backup_enabled, backup_interval_hours, etc.).
/// </summary>
public interface IScheduledBackupService
{
    void Start();
    void Stop();
    bool IsRunning { get; }
    DateTime? LastBackupAt { get; }
}

public class ScheduledBackupService : IScheduledBackupService
{
    private readonly ILogger _log = LoggingService.ForContext<ScheduledBackupService>();
    private readonly IBackupService _backupService;
    private readonly IConfigStore _configStore;
    private readonly ISchedulerLockStore? _lockStore;
    private readonly string _holderId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private System.Threading.Timer? _timer;
    private DateTime? _lastBackupAt;
    private DateTime _nextBackupDueUtc;
    private readonly TimeSpan _pollInterval = TimeSpan.FromMinutes(1);

    public ScheduledBackupService(
        IBackupService backupService,
        IConfigStore configStore,
        ISchedulerLockStore? lockStore = null)
    {
        _backupService = backupService;
        _configStore = configStore;
        _lockStore = lockStore;
        _nextBackupDueUtc = DateTime.UtcNow.AddMinutes(5);
    }

    public bool IsRunning => _timer != null;
    public DateTime? LastBackupAt => _lastBackupAt;

    public void Start()
    {
        if (_timer != null) return;

        _log.Information("Scheduled backup service started (first check after ~5 min, then every {Minutes} min)",
            _pollInterval.TotalMinutes);
        _timer = new System.Threading.Timer(
            _ => _ = OnTimerTickAsync(),
            null,
            dueTime: TimeSpan.FromMinutes(5),
            period: _pollInterval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _log.Information("Scheduled backup service stopped");
    }

    private async Task OnTimerTickAsync()
    {
        try
        {
            if (!_configStore.GetSettingBool("backup_enabled", true))
                return;

            var now = DateTime.UtcNow;
            if (now < _nextBackupDueUtc)
                return;

            var intervalHours = Math.Max(1, _configStore.GetSettingInt("backup_interval_hours", 24));
            var interval = TimeSpan.FromHours(intervalHours);

            _log.Debug("Running scheduled backup check");
            var leaderElection = _configStore.GetSettingBool("scheduler_leader_election_enabled", true);
            var leaseMinutes = Math.Max(1, _configStore.GetSettingInt("scheduler_lock_lease_minutes", 15));
            var acquired = true;
            if (leaderElection && _lockStore != null)
            {
                acquired = _lockStore.TryAcquireOrRenew(
                    "scheduled_backup",
                    _holderId,
                    TimeSpan.FromMinutes(leaseMinutes));
                if (!acquired)
                {
                    _log.Debug("Scheduled backup skipped: another instance holds the scheduler lock");
                    return;
                }
            }

            try
            {
                var includeDocs = _configStore.GetSettingBool("backup_include_documents", true);
                var includeOracle = _configStore.GetSettingBool("include_oracle_data", false);
                var result = await _backupService.CreateBackupAsync(null, includeDocs, null, includeOracle)
                    .ConfigureAwait(false);
                if (result.Success)
                {
                    _lastBackupAt = DateTime.UtcNow;
                    _nextBackupDueUtc = DateTime.UtcNow.Add(interval);
                    _log.Information("Scheduled backup completed: {Path}", result.BackupPath);
                    var keep = Math.Max(1, _configStore.GetSettingInt("backup_retention_count", 10));
                    await _backupService.CleanupOldBackupsAsync(keep).ConfigureAwait(false);
                }
                else
                {
                    _log.Warning("Scheduled backup failed: {Error}", result.Error);
                    _nextBackupDueUtc = DateTime.UtcNow.Add(TimeSpan.FromMinutes(15));
                }
            }
            finally
            {
                if (leaderElection && _lockStore != null && acquired)
                    _lockStore.ReleaseIfHolder("scheduled_backup", _holderId);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Scheduled backup error");
            _nextBackupDueUtc = DateTime.UtcNow.Add(TimeSpan.FromMinutes(15));
        }
    }
}
