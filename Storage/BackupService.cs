using System.IO;
using System.IO.Compression;
using System.Globalization;
using Serilog;
using WorkAudit.Core.Backup;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;

namespace WorkAudit.Storage;

/// <summary>
/// Service for backing up and restoring application files (documents + manifest) and optional Oracle schema exports.
/// </summary>
public interface IBackupService
{
    Task<BackupResult> CreateBackupAsync(string? destinationPath = null, bool includeDocuments = true,
        string? encryptionPassword = null, bool includeOracleData = false, CancellationToken cancellationToken = default);

    Task<RestoreResult> RestoreBackupAsync(string backupPath, string? decryptionPassword = null,
        RestoreBackupOptions? options = null, CancellationToken cancellationToken = default);

    Task<BackupVerificationResult> VerifyBackupAsync(string backupPath, CancellationToken cancellationToken = default);

    List<BackupInfo> GetBackupHistory();

    Task CleanupOldBackupsAsync(int keepCount = 10);
}

public class BackupService : IBackupService
{
    public const string OracleZipFolder = "Oracle";

    private readonly ILogger _log = LoggingService.ForContext<BackupService>();
    private readonly AppConfiguration _config;
    private readonly IExportEncryptionService? _encryptionService;
    private readonly IOracleBackupGateway? _oracleGateway;
    private readonly IConfigStore? _configStore;
    private readonly string _backupDirectory;

    public BackupService(
        AppConfiguration config,
        IExportEncryptionService? encryptionService = null,
        IOracleBackupGateway? oracleGateway = null,
        IConfigStore? configStore = null)
    {
        _config = config;
        _encryptionService = encryptionService;
        _oracleGateway = oracleGateway;
        _configStore = configStore;
        _backupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WORKAUDIT", "Backups");
        Directory.CreateDirectory(_backupDirectory);
    }

    public async Task<BackupResult> CreateBackupAsync(string? destinationPath = null, bool includeDocuments = true,
        string? encryptionPassword = null, bool includeOracleData = false, CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var backupName = $"WorkAudit_Backup_{timestamp}";
        var backupPath = destinationPath ?? Path.Combine(_backupDirectory, $"{backupName}.zip");
        var shouldEncrypt = !string.IsNullOrEmpty(encryptionPassword) && _encryptionService != null;

        _log.Information("Creating backup: {BackupPath} (Encrypted: {Encrypted}, Oracle: {Oracle})", backupPath, shouldEncrypt,
            includeOracleData);

        var skippedFiles = new List<string>();
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), backupName);
            Directory.CreateDirectory(tempDir);

            string? oracleSchema = null;
            string? oracleDirectory = null;
            string? dumpFileName = null;
            string? logFileName = null;
            long oracleArtifactBytes = 0;

            if (includeOracleData)
            {
                if (_oracleGateway == null || _configStore == null)
                    throw new InvalidOperationException("Oracle backup is not available (gateway or configuration store missing).");

                oracleDirectory = (_configStore.GetSettingValue("oracle_datapump_directory", "DATA_PUMP_DIR") ?? "DATA_PUMP_DIR")
                    .Trim();
                var localFolder = (_configStore.GetSettingValue("oracle_datapump_local_folder", "") ?? "").Trim();
                if (string.IsNullOrEmpty(localFolder))
                    throw new InvalidOperationException(
                        "Oracle backup requires app setting oracle_datapump_local_folder: a folder path that matches your Oracle DIRECTORY object and is visible to this PC (often a UNC share or mapped drive where expdp writes the .dmp file).");

                if (!OracleBackupConnectionParser.TryParse(_config.OracleConnectionString, out oracleSchema, out _, out _))
                    throw new InvalidOperationException("Cannot parse Oracle User Id from the connection string for schema export.");

                dumpFileName = $"wa_{timestamp}.dmp";
                logFileName = $"wa_{timestamp}.log";
                var expdpPath = _configStore.GetSettingValue("oracle_backup_dump_tool_path", "")?.Trim();
                if (string.IsNullOrEmpty(expdpPath))
                    expdpPath = null;

                var exportReq = new OraclePumpExportRequest
                {
                    ConnectionString = _config.OracleConnectionString,
                    SchemaName = oracleSchema,
                    OracleDirectoryName = oracleDirectory,
                    DumpFileName = dumpFileName,
                    LogFileName = logFileName,
                    ExpdpExecutablePath = expdpPath,
                    WorkingDirectory = localFolder
                };

                var pumpResult = await _oracleGateway.ExportSchemaAsync(exportReq, cancellationToken).ConfigureAwait(false);
                if (!pumpResult.Success)
                    throw new IOException(pumpResult.ErrorMessage ?? "Oracle expdp failed.");

                var dumpOnDisk = Path.Combine(localFolder, dumpFileName);
                await WaitForDumpFileAsync(dumpOnDisk, cancellationToken).ConfigureAwait(false);

                var oracleDest = Path.Combine(tempDir, OracleZipFolder);
                Directory.CreateDirectory(oracleDest);
                var destDump = Path.Combine(oracleDest, dumpFileName);
                File.Copy(dumpOnDisk, destDump, overwrite: true);
                oracleArtifactBytes += new FileInfo(destDump).Length;

                var logOnDisk = Path.Combine(localFolder, logFileName);
                if (File.Exists(logOnDisk))
                {
                    var destLog = Path.Combine(oracleDest, logFileName);
                    File.Copy(logOnDisk, destLog, overwrite: true);
                    oracleArtifactBytes += new FileInfo(destLog).Length;
                }
            }

            if (includeDocuments && Directory.Exists(_config.BaseDirectory))
            {
                var docsBackupDir = Path.Combine(tempDir, "Documents");
                await CopyDirectoryAsync(_config.BaseDirectory, docsBackupDir, _config.BaseDirectory, skippedFiles)
                    .ConfigureAwait(false);
                _log.Debug("Documents copied to backup");
            }

            var manifest = new BackupManifest
            {
                Version = Config.Defaults.AppVersion,
                CreatedAt = DateTime.UtcNow.ToString("O"),
                MachineName = Environment.MachineName,
                IncludesDocuments = includeDocuments,
                DatabaseSize = oracleArtifactBytes,
                IsEncrypted = shouldEncrypt,
                IncludesOracleSchema = includeOracleData,
                OracleSchemaName = oracleSchema,
                OracleDirectoryName = oracleDirectory,
                OracleDumpFileName = dumpFileName,
                OracleLogFileName = logFileName,
                OracleExportTool = "expdp"
            };

            var manifestPath = Path.Combine(tempDir, "manifest.json");
            await File.WriteAllTextAsync(manifestPath,
                Newtonsoft.Json.JsonConvert.SerializeObject(manifest, Newtonsoft.Json.Formatting.Indented), cancellationToken)
                .ConfigureAwait(false);

            var tempZipPath = backupPath;
            if (shouldEncrypt)
                tempZipPath = Path.Combine(Path.GetTempPath(), $"{backupName}_temp.zip");

            if (File.Exists(tempZipPath))
                File.Delete(tempZipPath);

            await CreateZipFromDirectoryResilientAsync(tempDir, tempZipPath, skippedFiles).ConfigureAwait(false);

            await TryDeleteStagingDirectoryAsync(tempDir).ConfigureAwait(false);

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
                SkippedFiles = skippedFiles,
                OracleExportIncluded = includeOracleData
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Backup failed");
            return new BackupResult
            {
                Success = false,
                Error = ex.Message,
                OracleExportIncluded = includeOracleData
            };
        }
    }

    public async Task<RestoreResult> RestoreBackupAsync(string backupPath, string? decryptionPassword = null,
        RestoreBackupOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new RestoreBackupOptions();
        _log.Information("Restoring backup from: {BackupPath}", backupPath);

        if (!File.Exists(backupPath))
            return new RestoreResult { Success = false, Error = "Backup file not found" };

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"WorkAudit_Restore_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var zipPath = backupPath;

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

            ZipFile.ExtractToDirectory(zipPath, tempDir);

            if (zipPath != backupPath && File.Exists(zipPath))
                File.Delete(zipPath);

            var manifestPath = Path.Combine(tempDir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Directory.Delete(tempDir, true);
                return new RestoreResult { Success = false, Error = "Invalid backup: manifest not found" };
            }

            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<BackupManifest>(manifestJson);

            if (options.CreateSafetyBackup)
            {
                var wantSafetyOracle = options.SafetyBackupIncludeOracle && manifest is { IncludesOracleSchema: true };
                var canSafetyOracle = wantSafetyOracle && _configStore != null &&
                                      !string.IsNullOrWhiteSpace(_configStore.GetSettingValue("oracle_datapump_local_folder", ""));
                await CreateBackupAsync(null, true, null, canSafetyOracle, cancellationToken).ConfigureAwait(false);
                _log.Information("Created safety backup before restore (Oracle: {Oracle})", canSafetyOracle);
            }

            if (manifest is { IncludesOracleSchema: true } && options.RestoreOracleSchema)
            {
                if (_oracleGateway == null || _configStore == null)
                {
                    Directory.Delete(tempDir, true);
                    return new RestoreResult { Success = false, Error = "Oracle restore is not available (gateway or configuration store missing)." };
                }

                var localFolder = (_configStore.GetSettingValue("oracle_datapump_local_folder", "") ?? "").Trim();
                if (string.IsNullOrEmpty(localFolder))
                {
                    Directory.Delete(tempDir, true);
                    return new RestoreResult
                    {
                        Success = false,
                        Error = "oracle_datapump_local_folder is not configured; cannot copy dump files for impdp."
                    };
                }

                var oracleSrc = Path.Combine(tempDir, OracleZipFolder);
                if (!Directory.Exists(oracleSrc) ||
                    string.IsNullOrEmpty(manifest.OracleDumpFileName))
                {
                    Directory.Delete(tempDir, true);
                    return new RestoreResult { Success = false, Error = "Backup manifest claims Oracle data but Oracle folder or dump file name is missing." };
                }

                Directory.CreateDirectory(localFolder);
                var srcDump = Path.Combine(oracleSrc, manifest.OracleDumpFileName);
                if (!File.Exists(srcDump))
                {
                    Directory.Delete(tempDir, true);
                    return new RestoreResult { Success = false, Error = $"Dump file not found in backup: {manifest.OracleDumpFileName}" };
                }

                var destDump = Path.Combine(localFolder, manifest.OracleDumpFileName);
                File.Copy(srcDump, destDump, overwrite: true);

                if (!string.IsNullOrEmpty(manifest.OracleLogFileName))
                {
                    var srcLog = Path.Combine(oracleSrc, manifest.OracleLogFileName);
                    if (File.Exists(srcLog))
                        File.Copy(srcLog, Path.Combine(localFolder, manifest.OracleLogFileName), overwrite: true);
                }

                var oracleDirectory = string.IsNullOrEmpty(manifest.OracleDirectoryName)
                    ? "DATA_PUMP_DIR"
                    : manifest.OracleDirectoryName;

                var schema = manifest.OracleSchemaName;
                if (string.IsNullOrEmpty(schema))
                {
                    if (!OracleBackupConnectionParser.TryParse(_config.OracleConnectionString, out schema, out _, out _))
                    {
                        Directory.Delete(tempDir, true);
                        return new RestoreResult { Success = false, Error = "Cannot determine Oracle schema for import." };
                    }
                }

                var impLog = $"wa_restore_{DateTime.UtcNow:yyyyMMddHHmmss}.log";
                var impdpPath = _configStore.GetSettingValue("oracle_backup_dump_tool_path", "")?.Trim();
                if (string.IsNullOrEmpty(impdpPath))
                    impdpPath = null;

                var importReq = new OraclePumpImportRequest
                {
                    ConnectionString = _config.OracleConnectionString,
                    SchemaName = schema,
                    OracleDirectoryName = oracleDirectory,
                    DumpFileName = manifest.OracleDumpFileName,
                    LogFileName = impLog,
                    ImpdpExecutablePath = impdpPath,
                    WorkingDirectory = localFolder,
                    ReplaceExistingObjects = true
                };

                var impResult = await _oracleGateway.ImportSchemaAsync(importReq, cancellationToken).ConfigureAwait(false);
                if (!impResult.Success)
                {
                    Directory.Delete(tempDir, true);
                    return new RestoreResult
                    {
                        Success = false,
                        Error = impResult.ErrorMessage ?? "Oracle impdp failed."
                    };
                }

                _log.Information("Oracle schema import completed");
            }

            var docsBackupDir = Path.Combine(tempDir, "Documents");
            if (Directory.Exists(docsBackupDir))
            {
                await CopyDirectoryAsync(docsBackupDir, _config.BaseDirectory).ConfigureAwait(false);
                _log.Information("Documents restored");
            }

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

    public async Task<BackupVerificationResult> VerifyBackupAsync(string backupPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupPath))
            return new BackupVerificationResult { Valid = false, Error = "Backup file not found" };

        if (IsEncryptedBackup(backupPath))
            return new BackupVerificationResult
            {
                Valid = false,
                Error = "Encrypted backups cannot be verified as a ZIP without decryption. Decrypt or verify from the restore wizard."
            };

        try
        {
            using var archive = ZipFile.OpenRead(backupPath);
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null)
                return new BackupVerificationResult { Valid = false, Error = "Manifest not found" };

            using var stream = manifestEntry.Open();
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<BackupManifest>(json);
            if (manifest == null)
                return new BackupVerificationResult { Valid = false, Error = "Invalid manifest" };

            string? oracleNote = null;
            if (manifest.IncludesOracleSchema)
            {
                var dumpName = manifest.OracleDumpFileName;
                if (string.IsNullOrEmpty(dumpName))
                {
                    oracleNote = "Manifest marks Oracle backup but dump file name is missing.";
                    return new BackupVerificationResult
                    {
                        Valid = false,
                        Error = oracleNote,
                        Version = manifest.Version,
                        CreatedAt = manifest.CreatedAt,
                        IncludesDocuments = manifest.IncludesDocuments,
                        DatabaseSize = manifest.DatabaseSize,
                        IncludesOracleSchema = true,
                        OracleVerificationNote = oracleNote
                    };
                }

                var dumpEntry = archive.GetEntry($"{OracleZipFolder}/{dumpName}") ??
                                archive.GetEntry($"{OracleZipFolder}\\{dumpName}");
                if (dumpEntry == null)
                {
                    oracleNote = $"Oracle dump entry not found in archive: {OracleZipFolder}/{dumpName}";
                    return new BackupVerificationResult
                    {
                        Valid = false,
                        Error = oracleNote,
                        Version = manifest.Version,
                        CreatedAt = manifest.CreatedAt,
                        IncludesDocuments = manifest.IncludesDocuments,
                        DatabaseSize = manifest.DatabaseSize,
                        IncludesOracleSchema = true,
                        OracleVerificationNote = oracleNote
                    };
                }

                oracleNote = "Oracle dump file present in archive.";
            }

            _log.Information("Backup verification passed: {Path} (Version: {Version}, Created: {Created})",
                backupPath, manifest.Version, manifest.CreatedAt);
            return new BackupVerificationResult
            {
                Valid = true,
                Version = manifest.Version,
                CreatedAt = manifest.CreatedAt,
                IncludesDocuments = manifest.IncludesDocuments,
                DatabaseSize = manifest.DatabaseSize,
                IncludesOracleSchema = manifest.IncludesOracleSchema,
                OracleVerificationNote = oracleNote
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

    private static async Task WaitForDumpFileAsync(string path, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 120; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(path))
                {
                    using var fs = File.OpenRead(path);
                    if (fs.Length > 0)
                        return;
                }
            }
            catch (IOException)
            {
                // still writing
            }

            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }

        throw new IOException($"Timed out waiting for Oracle dump file at {path}. Ensure expdp completed and oracle_datapump_local_folder matches the DIRECTORY path.");
    }

    private async Task CopyDirectoryAsync(string sourceDir, string destDir, string? pathRootForRelative = null,
        List<string>? skippedFiles = null)
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
            await CopyDirectoryAsync(dir, destSubDir, pathRootForRelative, skippedFiles).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds a zip from a directory tree; skips individual files that cannot be read after one retry.
    /// Required entries (manifest.json) cause failure if they cannot be archived.
    /// </summary>
    internal async Task CreateZipFromDirectoryResilientAsync(string sourceDir, string zipPath, List<string> skippedFiles)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);
        await AddDirectoryToZipAsync(archive, sourceDir, skippedFiles).ConfigureAwait(false);
    }

    private static bool IsCriticalBackupZipEntry(string relativePath)
    {
        var n = relativePath.Replace('\\', '/');
        return string.Equals(n, "manifest.json", StringComparison.OrdinalIgnoreCase);
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
                    await input.CopyToAsync(entryStream).ConfigureAwait(false);
                    added = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt == 0)
                    {
                        await Task.Delay(200).ConfigureAwait(false);
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
                    await Task.Delay(400 * (attempt + 1)).ConfigureAwait(false);
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
    public List<string> SkippedFiles { get; set; } = new();

    /// <summary>True when this backup run requested Oracle export (success or failure).</summary>
    public bool OracleExportIncluded { get; set; }
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

    public bool IncludesOracleSchema { get; set; }
    public string? OracleSchemaName { get; set; }
    public string? OracleDirectoryName { get; set; }
    public string? OracleDumpFileName { get; set; }
    public string? OracleLogFileName { get; set; }
    public string OracleExportTool { get; set; } = "expdp";
}

public class BackupVerificationResult
{
    public bool Valid { get; set; }
    public string? Error { get; set; }
    public string? Version { get; set; }
    public string? CreatedAt { get; set; }
    public bool IncludesDocuments { get; set; }
    public long DatabaseSize { get; set; }
    public bool IncludesOracleSchema { get; set; }
    public string? OracleVerificationNote { get; set; }
}
