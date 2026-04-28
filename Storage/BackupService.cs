using System.IO;
using System.IO.Compression;
using Serilog;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;

namespace WorkAudit.Storage;

/// <summary>
/// Service for backing up and restoring the database and documents.
/// </summary>
public interface IBackupService
{
    Task<BackupResult> CreateBackupAsync(string? destinationPath = null, bool includeDocuments = true, string? encryptionPassword = null);
    Task<RestoreResult> RestoreBackupAsync(string backupPath, string? decryptionPassword = null);
    Task<BackupVerificationResult> VerifyBackupAsync(string backupPath);
    List<BackupInfo> GetBackupHistory();
    Task CleanupOldBackupsAsync(int keepCount = 10);
}

public class BackupService : IBackupService
{
    private readonly ILogger _log = LoggingService.ForContext<BackupService>();
    private readonly AppConfiguration _config;
    private readonly IExportEncryptionService? _encryptionService;
    private readonly string _backupDirectory;

    public BackupService(AppConfiguration config, IExportEncryptionService? encryptionService = null)
    {
        _config = config;
        _encryptionService = encryptionService;
        _backupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WORKAUDIT", "Backups");
        Directory.CreateDirectory(_backupDirectory);
    }

    public async Task<BackupResult> CreateBackupAsync(string? destinationPath = null, bool includeDocuments = true, string? encryptionPassword = null)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupName = $"WorkAudit_Backup_{timestamp}";
        var backupPath = destinationPath ?? Path.Combine(_backupDirectory, $"{backupName}.zip");
        var shouldEncrypt = !string.IsNullOrEmpty(encryptionPassword) && _encryptionService != null;

        _log.Information("Creating backup: {BackupPath} (Encrypted: {Encrypted})", backupPath, shouldEncrypt);

        var skippedFiles = new List<string>();
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), backupName);
            Directory.CreateDirectory(tempDir);

            // Copy local SQLite database file only when configuration points at an existing file (legacy); Oracle uses server DB.
            var dbPath = _config.OracleConnectionString;
            if (!string.IsNullOrWhiteSpace(dbPath) && dbPath.EndsWith(".db", StringComparison.OrdinalIgnoreCase) &&
                Path.IsPathRooted(dbPath) && File.Exists(dbPath))
            {
                var dbBackupPath = Path.Combine(tempDir, "workaudit.db");
                File.Copy(dbPath, dbBackupPath, true);
                _log.Debug("Database file copied to backup");
            }

            // Copy documents if requested
            if (includeDocuments && Directory.Exists(_config.BaseDirectory))
            {
                var docsBackupDir = Path.Combine(tempDir, "Documents");
                await CopyDirectoryAsync(_config.BaseDirectory, docsBackupDir, _config.BaseDirectory, skippedFiles);
                _log.Debug("Documents copied to backup");
            }

            // Create backup manifest
            var manifest = new BackupManifest
            {
                Version = Config.Defaults.AppVersion,
                CreatedAt = DateTime.UtcNow.ToString("O"),
                MachineName = Environment.MachineName,
                IncludesDocuments = includeDocuments,
                DatabaseSize = !string.IsNullOrWhiteSpace(dbPath) && File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0,
                IsEncrypted = shouldEncrypt
            };

            var manifestPath = Path.Combine(tempDir, "manifest.json");
            await File.WriteAllTextAsync(manifestPath,
                Newtonsoft.Json.JsonConvert.SerializeObject(manifest, Newtonsoft.Json.Formatting.Indented));

            // Create ZIP archive
            var tempZipPath = backupPath;
            if (shouldEncrypt)
            {
                // If encrypting, create temp ZIP first
                tempZipPath = Path.Combine(Path.GetTempPath(), $"{backupName}_temp.zip");
            }

            if (File.Exists(tempZipPath))
                File.Delete(tempZipPath);

            await CreateZipFromDirectoryResilientAsync(tempDir, tempZipPath, skippedFiles);

            // Staging folder can still be locked (AV, indexer) after zipping; a failed delete must not fail the backup.
            await TryDeleteStagingDirectoryAsync(tempDir);

            // Encrypt if password provided
            if (shouldEncrypt && !string.IsNullOrEmpty(encryptionPassword))
            {
                if (File.Exists(backupPath))
                    File.Delete(backupPath);

                _encryptionService!.EncryptFile(tempZipPath, backupPath, encryptionPassword);
                File.Delete(tempZipPath);
                _log.Information("Backup encrypted successfully");
            }

            var fileInfo = new FileInfo(backupPath);
            _log.Information("Backup created successfully: {Size} bytes", fileInfo.Length);

            return new BackupResult
            {
                Success = true,
                BackupPath = backupPath,
                SizeBytes = fileInfo.Length,
                CreatedAt = DateTime.UtcNow,
                IsEncrypted = shouldEncrypt,
                SkippedFiles = skippedFiles
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Backup failed");
            return new BackupResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<RestoreResult> RestoreBackupAsync(string backupPath, string? decryptionPassword = null)
    {
        _log.Information("Restoring backup from: {BackupPath}", backupPath);

        if (!File.Exists(backupPath))
        {
            return new RestoreResult { Success = false, Error = "Backup file not found" };
        }

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"WorkAudit_Restore_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var zipPath = backupPath;

            // Check if file is encrypted
            if (IsEncryptedBackup(backupPath))
            {
                if (string.IsNullOrEmpty(decryptionPassword))
                {
                    Directory.Delete(tempDir, true);
                    return new RestoreResult { Success = false, Error = "Backup is encrypted. Password required." };
                }

                if (_encryptionService == null)
                {
                    Directory.Delete(tempDir, true);
                    return new RestoreResult { Success = false, Error = "Encryption service not available." };
                }

                // Decrypt to temp location
                zipPath = Path.Combine(Path.GetTempPath(), $"WorkAudit_Decrypted_{Guid.NewGuid():N}.zip");
                try
                {
                    _encryptionService.DecryptFile(backupPath, zipPath, decryptionPassword);
                    _log.Information("Backup decrypted successfully");
                }
                catch (Exception ex)
                {
                    Directory.Delete(tempDir, true);
                    if (File.Exists(zipPath))
                        File.Delete(zipPath);
                    return new RestoreResult { Success = false, Error = $"Decryption failed: {ex.Message}" };
                }
            }

            // Extract backup
            ZipFile.ExtractToDirectory(zipPath, tempDir);

            // Cleanup decrypted temp file if it exists
            if (zipPath != backupPath && File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            // Verify manifest
            var manifestPath = Path.Combine(tempDir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Directory.Delete(tempDir, true);
                return new RestoreResult { Success = false, Error = "Invalid backup: manifest not found" };
            }

            var manifestJson = await File.ReadAllTextAsync(manifestPath);
            var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<BackupManifest>(manifestJson);

            // Create backup of current state before restoring
            await CreateBackupAsync(null, true, null);
            _log.Information("Created safety backup before restore");

            // Restore database file only when current configuration targets a local .db path
            var dbBackupPath = Path.Combine(tempDir, "workaudit.db");
            var targetCs = _config.OracleConnectionString;
            if (File.Exists(dbBackupPath) && !string.IsNullOrWhiteSpace(targetCs) &&
                targetCs.EndsWith(".db", StringComparison.OrdinalIgnoreCase) && Path.IsPathRooted(targetCs))
            {
                File.Copy(dbBackupPath, targetCs, true);
                _log.Information("Local database file restored");
            }
            else if (File.Exists(dbBackupPath))
            {
                _log.Warning("Backup contains workaudit.db but active configuration uses Oracle; database file was not restored.");
            }

            // Restore documents
            var docsBackupDir = Path.Combine(tempDir, "Documents");
            if (Directory.Exists(docsBackupDir))
            {
                await CopyDirectoryAsync(docsBackupDir, _config.BaseDirectory);
                _log.Information("Documents restored");
            }

            // Cleanup
            Directory.Delete(tempDir, true);

            _log.Information("Restore completed successfully");
            return new RestoreResult
            {
                Success = true,
                RestoredFrom = manifest?.CreatedAt ?? "Unknown"
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Restore failed");
            return new RestoreResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<BackupVerificationResult> VerifyBackupAsync(string backupPath)
    {
        if (!File.Exists(backupPath))
            return new BackupVerificationResult { Valid = false, Error = "Backup file not found" };

        try
        {
            using var archive = ZipFile.OpenRead(backupPath);
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null)
                return new BackupVerificationResult { Valid = false, Error = "Manifest not found" };

            using var stream = manifestEntry.Open();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<BackupManifest>(json);
            if (manifest == null)
                return new BackupVerificationResult { Valid = false, Error = "Invalid manifest" };

            var dbEntry = archive.GetEntry("workaudit.db");
            if (dbEntry == null)
                return new BackupVerificationResult { Valid = false, Error = "Database not found in backup" };

            _log.Information("Backup verification passed: {Path} (Version: {Version}, Created: {Created})",
                backupPath, manifest.Version, manifest.CreatedAt);
            return new BackupVerificationResult
            {
                Valid = true,
                Version = manifest.Version,
                CreatedAt = manifest.CreatedAt,
                IncludesDocuments = manifest.IncludesDocuments,
                DatabaseSize = manifest.DatabaseSize
            };
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Backup verification failed: {Path}", backupPath);
            return new BackupVerificationResult { Valid = false, Error = ex.Message };
        }
    }

    public List<BackupInfo> GetBackupHistory()
    {
        var backups = new List<BackupInfo>();

        if (!Directory.Exists(_backupDirectory))
            return backups;

        foreach (var file in Directory.GetFiles(_backupDirectory, "*.zip").OrderByDescending(f => f))
        {
            var fileInfo = new FileInfo(file);
            backups.Add(new BackupInfo
            {
                Path = file,
                Name = fileInfo.Name,
                SizeBytes = fileInfo.Length,
                CreatedAt = fileInfo.CreationTimeUtc
            });
        }

        return backups;
    }

    public async Task CleanupOldBackupsAsync(int keepCount = 10)
    {
        var backups = GetBackupHistory();
        var toDelete = backups.Skip(keepCount).ToList();

        foreach (var backup in toDelete)
        {
            try
            {
                File.Delete(backup.Path);
                _log.Information("Deleted old backup: {Name}", backup.Name);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to delete old backup: {Name}", backup.Name);
            }
        }

        await Task.CompletedTask;
    }

    private async Task CopyDirectoryAsync(string sourceDir, string destDir, string? pathRootForRelative = null, List<string>? skippedFiles = null)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            try
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to copy file during backup: {File}", file);
                if (pathRootForRelative != null && skippedFiles != null)
                {
                    var rel = Path.GetRelativePath(pathRootForRelative, file);
                    skippedFiles.Add($"copy failed: {rel.Replace('\\', '/')}");
                }
            }
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            await CopyDirectoryAsync(dir, destSubDir, pathRootForRelative, skippedFiles);
        }
    }

    /// <summary>
    /// Builds a zip from a directory tree; skips individual files that cannot be read after one retry.
    /// Required entries (manifest.json, workaudit.db) cause failure if they cannot be archived.
    /// </summary>
    internal async Task CreateZipFromDirectoryResilientAsync(string sourceDir, string zipPath, List<string> skippedFiles)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);
        await AddDirectoryToZipAsync(archive, sourceDir, skippedFiles);
    }

    private static bool IsCriticalBackupZipEntry(string relativePath)
    {
        var n = relativePath.Replace('\\', '/');
        return string.Equals(n, "manifest.json", StringComparison.OrdinalIgnoreCase)
               || string.Equals(n, "workaudit.db", StringComparison.OrdinalIgnoreCase);
    }

    private async Task AddDirectoryToZipAsync(ZipArchive archive, string sourceDir, List<string> skippedFiles)
    {
        var root = Path.GetFullPath(sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, filePath);
            if (relativePath.StartsWith("..", StringComparison.Ordinal))
                continue;

            var entryName = relativePath.Replace('\\', '/');
            var critical = IsCriticalBackupZipEntry(relativePath);
            var added = false;

            for (var attempt = 0; attempt < 2 && !added; attempt++)
            {
                try
                {
                    await using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await input.CopyToAsync(entryStream);
                    added = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt == 0)
                    {
                        await Task.Delay(200);
                        continue;
                    }

                    _log.Warning(ex, "Skipping file in backup zip: {File}", filePath);
                    if (critical)
                        throw new IOException($"Cannot archive required backup file: {entryName}. {ex.Message}", ex);
                    skippedFiles.Add($"zip failed: {entryName}");
                }
            }
        }
    }

    /// <summary>
    /// Removes staging files after the zip is written. Uses retries and per-file cleanup so a locked temp file
    /// (e.g. AV still scanning) does not fail the backup after the archive was already created.
    /// </summary>
    private async Task TryDeleteStagingDirectoryAsync(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;
                Directory.Delete(path, true);
                return;
            }
            catch (Exception ex)
            {
                if (attempt < 2)
                {
                    _log.Debug(ex, "Temp directory delete attempt {Attempt} failed, retrying: {Path}", attempt + 1, path);
                    await Task.Delay(400 * (attempt + 1));
                }
                else
                    _log.Debug(ex, "Temp directory delete attempt {Attempt} failed: {Path}", attempt + 1, path);
            }
        }

        if (!Directory.Exists(path))
            return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    _log.Debug(ex, "Could not delete temp file: {File}", file);
                }
            }

            foreach (var dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    Directory.Delete(dir, false);
                }
                catch
                {
                    // ignore
                }
            }

            if (Directory.Exists(path))
                Directory.Delete(path, false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex,
                "Could not delete temporary backup staging folder (backup file was created successfully). You may remove it manually: {Path}",
                path);
        }
    }

    private bool IsEncryptedBackup(string backupPath)
    {
        try
        {
            using var fs = File.OpenRead(backupPath);
            var header = new byte[4];
            if (fs.Read(header, 0, 4) == 4)
            {
                // Check for WorkAudit Encrypted eXport header "WAEX"
                return header[0] == 0x57 && header[1] == 0x41 && header[2] == 0x45 && header[3] == 0x58;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}

public class BackupResult
{
    public bool Success { get; set; }
    public string? BackupPath { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsEncrypted { get; set; }
    public string? Error { get; set; }
    /// <summary>Relative paths or notes for files that were not copied or not zipped (e.g. access denied).</summary>
    public List<string> SkippedFiles { get; set; } = new();
}

public class RestoreResult
{
    public bool Success { get; set; }
    public string? RestoredFrom { get; set; }
    public string? Error { get; set; }
}

public class BackupInfo
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BackupManifest
{
    public string Version { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string MachineName { get; set; } = "";
    public bool IncludesDocuments { get; set; }
    public long DatabaseSize { get; set; }
    public bool IsEncrypted { get; set; }
}

public class BackupVerificationResult
{
    public bool Valid { get; set; }
    public string? Error { get; set; }
    public string? Version { get; set; }
    public string? CreatedAt { get; set; }
    public bool IncludesDocuments { get; set; }
    public long DatabaseSize { get; set; }
}
