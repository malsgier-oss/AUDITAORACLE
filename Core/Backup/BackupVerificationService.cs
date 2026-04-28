using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Storage;

namespace WorkAudit.Core.Backup;

/// <summary>
/// Verifies backup integrity. Phase 7.3 Backup & Recovery.
/// Delegates to IBackupService and can verify multiple backups (e.g. recent history).
/// </summary>
public interface IBackupVerificationService
{
    Task<BackupVerificationResult> VerifyAsync(string backupPath, CancellationToken ct = default);

    /// <summary>Verify the N most recent backups from history. Returns count valid / total.</summary>
    Task<(int Valid, int Total, List<BackupVerificationResult> Results)> VerifyRecentBackupsAsync(int count = 5, CancellationToken ct = default);
}

public class BackupVerificationService : IBackupVerificationService
{
    private readonly ILogger _log = LoggingService.ForContext<BackupVerificationService>();
    private readonly IBackupService _backupService;

    public BackupVerificationService(IBackupService backupService)
    {
        _backupService = backupService;
    }

    public async Task<BackupVerificationResult> VerifyAsync(string backupPath, CancellationToken ct = default)
    {
        return await _backupService.VerifyBackupAsync(backupPath);
    }

    public async Task<(int Valid, int Total, List<BackupVerificationResult> Results)> VerifyRecentBackupsAsync(int count = 5, CancellationToken ct = default)
    {
        var history = _backupService.GetBackupHistory();
        var toVerify = history.Take(count).ToList();
        var results = new List<BackupVerificationResult>();

        foreach (var backup in toVerify)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _backupService.VerifyBackupAsync(backup.Path);
            results.Add(result);
        }

        var valid = results.Count(r => r.Valid);
        _log.Information("Verified {Valid}/{Total} recent backups", valid, results.Count);
        return (valid, results.Count, results);
    }
}
