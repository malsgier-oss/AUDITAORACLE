namespace WorkAudit.Storage;

/// <summary>Options for <see cref="IBackupService.RestoreBackupAsync"/>.</summary>
public sealed class RestoreBackupOptions
{
    /// <summary>When the backup contains an Oracle dump, run impdp after copying files to the configured datapump folder.</summary>
    public bool RestoreOracleSchema { get; init; } = true;

    /// <summary>Create a safety backup of the current state before applying the restore.</summary>
    public bool CreateSafetyBackup { get; init; } = true;

    /// <summary>When creating the safety backup, include Oracle schema if the backup being restored included it.</summary>
    public bool SafetyBackupIncludeOracle { get; init; } = true;
}
