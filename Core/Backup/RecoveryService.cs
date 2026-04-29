using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Storage;

namespace WorkAudit.Core.Backup;

/// <summary>
/// Point-in-time recovery: restore from the most recent backup on or before a given date.
/// Phase 7.3 Backup & Recovery.
/// </summary>
public interface IRecoveryService
{
    /// <summary>Returns the most recent backup created on or before the target date, or null if none.</summary>
    BackupInfo? GetBackupForPointInTime(DateTime targetDate);

    /// <summary>Restores from the backup that best matches the target date. Returns restore result.</summary>
    Task<RestoreResult> RestoreToPointInTimeAsync(DateTime targetDate, CancellationToken ct = default);
}

public class RecoveryService : IRecoveryService
{
    private readonly ILogger _log = LoggingService.ForContext<RecoveryService>();
    private readonly IBackupService _backupService;

    public RecoveryService(IBackupService backupService)
    {
        _backupService = backupService;
    }

    public BackupInfo? GetBackupForPointInTime(DateTime targetDate)
    {
        var history = _backupService.GetBackupHistory();
        var cutoff = targetDate.Date.AddDays(1); // end of target day
        return history
            .Where(b => b.CreatedAt < cutoff)
            .OrderByDescending(b => b.CreatedAt)
            .FirstOrDefault();
    }

    public async Task<RestoreResult> RestoreToPointInTimeAsync(DateTime targetDate, CancellationToken ct = default)
    {
        var backup = GetBackupForPointInTime(targetDate);
        if (backup == null)
        {
            _log.Warning("No backup found for point-in-time: {TargetDate:yyyy-MM-dd}", targetDate);
            return new RestoreResult
            {
                Success = false,
                Error = $"No backup found on or before {targetDate:yyyy-MM-dd}. Create a backup first."
            };
        }

        _log.Information("Restoring to point-in-time {TargetDate:yyyy-MM-dd} using backup: {Name} ({CreatedAt})",
            targetDate, backup.Name, backup.CreatedAt);
        return await _backupService.RestoreBackupAsync(backup.Path, null, null, ct).ConfigureAwait(false);
    }
}
