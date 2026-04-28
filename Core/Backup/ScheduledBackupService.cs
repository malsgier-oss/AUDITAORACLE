using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Storage;

namespace WorkAudit.Core.Backup;

/// <summary>
/// Runs automatic backups on a schedule (e.g. daily).
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
    private readonly TimeSpan _interval;
    private System.Threading.Timer? _timer;
    private DateTime? _lastBackupAt;

    public ScheduledBackupService(IBackupService backupService)
    {
        _backupService = backupService;
        _interval = TimeSpan.FromHours(24);
    }

    public bool IsRunning => _timer != null;
    public DateTime? LastBackupAt => _lastBackupAt;

    public void Start()
    {
        if (_timer != null) return;

        _log.Information("Scheduled backup service started (interval: {Hours}h)", _interval.TotalHours);
        _timer = new System.Threading.Timer(
            _ => _ = RunBackupAsync(),
            null,
            TimeSpan.FromMinutes(5),
            _interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _log.Information("Scheduled backup service stopped");
    }

    private async Task RunBackupAsync()
    {
        try
        {
            _log.Debug("Running scheduled backup");
            var result = await _backupService.CreateBackupAsync(null, includeDocuments: true);
            if (result.Success)
            {
                _lastBackupAt = DateTime.UtcNow;
                _log.Information("Scheduled backup completed: {Path}", result.BackupPath);
                await _backupService.CleanupOldBackupsAsync(keepCount: 10);
            }
            else
            {
                _log.Warning("Scheduled backup failed: {Error}", result.Error);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Scheduled backup error");
        }
    }
}
