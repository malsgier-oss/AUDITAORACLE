using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Core.Update;

/// <summary>
/// Auto-update service with rollback capability for production deployments.
/// Checks for updates from a central server and applies them with full backup and rollback support.
/// </summary>
public interface IAutoUpdateService
{
    Task<UpdateCheckResult> CheckForUpdatesAsync();
    Task<UpdateResult> ApplyUpdateAsync(UpdateInfo updateInfo, IProgress<int>? progress = null);
    Task<bool> RollbackUpdateAsync();
    string GetCurrentVersion();
}

public class AutoUpdateService : IAutoUpdateService
{
    private readonly ILogger _log = LoggingService.ForContext<AutoUpdateService>();
    private readonly string _updateServerUrl;
    private readonly string _currentVersion;
    private readonly string _appPath;
    private readonly string _backupPath;

    public AutoUpdateService(string updateServerUrl, string currentVersion)
    {
        _updateServerUrl = updateServerUrl;
        _currentVersion = currentVersion;
        _appPath = AppDomain.CurrentDomain.BaseDirectory;
        
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _backupPath = Path.Combine(appData, "WORKAUDIT", "Backups", "Updates");
        Directory.CreateDirectory(_backupPath);
    }

    public string GetCurrentVersion() => _currentVersion;

    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var versionUrl = $"{_updateServerUrl}/version.json";
            
            var response = await client.GetStringAsync(versionUrl);
            var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(response);

            if (updateInfo == null)
            {
                _log.Warning("Failed to parse update information");
                return new UpdateCheckResult { Success = false, Error = "Invalid update response" };
            }

            var isNewer = IsNewerVersion(updateInfo.Version, _currentVersion);
            
            if (isNewer)
            {
                _log.Information("Update available: {NewVersion} (current: {CurrentVersion})", 
                    updateInfo.Version, _currentVersion);
            }

            return new UpdateCheckResult
            {
                Success = true,
                UpdateAvailable = isNewer,
                UpdateInfo = updateInfo
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to check for updates");
            return new UpdateCheckResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<UpdateResult> ApplyUpdateAsync(UpdateInfo updateInfo, IProgress<int>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        var backupDir = Path.Combine(_backupPath, $"v{_currentVersion}_{DateTime.UtcNow:yyyyMMddHHmmss}");

        try
        {
            _log.Information("Starting update from v{Current} to v{New}", _currentVersion, updateInfo.Version);
            
            progress?.Report(10);

            Directory.CreateDirectory(backupDir);
            _log.Information("Created backup directory: {BackupDir}", backupDir);

            progress?.Report(20);

            await BackupCurrentVersion(backupDir);
            _log.Information("Current version backed up to {BackupDir}", backupDir);

            progress?.Report(40);

            var updatePackagePath = await DownloadUpdateAsync(updateInfo, progress);
            _log.Information("Update package downloaded: {Path}", updatePackagePath);

            progress?.Report(70);

            await ExtractUpdateAsync(updatePackagePath);
            _log.Information("Update extracted to application directory");

            progress?.Report(90);

            File.Delete(updatePackagePath);

            progress?.Report(100);
            sw.Stop();

            _log.Information("Update completed successfully in {Duration}ms", sw.ElapsedMilliseconds);

            return new UpdateResult
            {
                Success = true,
                NewVersion = updateInfo.Version,
                BackupPath = backupDir,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Update failed, attempting rollback");
            
            try
            {
                await RollbackFromBackupAsync(backupDir);
                _log.Information("Rollback completed successfully");
                
                return new UpdateResult
                {
                    Success = false,
                    Error = $"Update failed: {ex.Message}. Rollback successful.",
                    RolledBack = true
                };
            }
            catch (Exception rollbackEx)
            {
                _log.Fatal(rollbackEx, "CRITICAL: Rollback failed");
                return new UpdateResult
                {
                    Success = false,
                    Error = $"Update AND rollback failed. Manual recovery required from: {backupDir}",
                    RolledBack = false
                };
            }
        }
    }

    public async Task<bool> RollbackUpdateAsync()
    {
        try
        {
            var latestBackup = GetLatestBackup();
            if (latestBackup == null)
            {
                _log.Warning("No backup found for rollback");
                return false;
            }

            _log.Information("Rolling back to backup: {Backup}", latestBackup);
            await RollbackFromBackupAsync(latestBackup);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Rollback failed");
            return false;
        }
    }

    private async Task BackupCurrentVersion(string backupDir)
    {
        await Task.Run(() =>
        {
            var files = Directory.GetFiles(_appPath, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var destPath = Path.Combine(backupDir, fileName);
                File.Copy(file, destPath, overwrite: true);
            }
        });
    }

    private async Task<string> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<int>? progress)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var downloadUrl = updateInfo.DownloadUrl;
        var tempPath = Path.Combine(Path.GetTempPath(), $"WorkAudit_Update_{updateInfo.Version}.zip");

        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        using var fileStream = File.Create(tempPath);
        using var contentStream = await response.Content.ReadAsStreamAsync();

        var buffer = new byte[8192];
        long bytesRead = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            bytesRead += read;

            if (totalBytes > 0)
            {
                var percent = (int)((bytesRead * 30.0) / totalBytes) + 40;
                progress?.Report(Math.Min(percent, 69));
            }
        }

        return tempPath;
    }

    private async Task ExtractUpdateAsync(string zipPath)
    {
        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var destPath = Path.Combine(_appPath, entry.FullName);
                var destDir = Path.GetDirectoryName(destPath);
                
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                entry.ExtractToFile(destPath, overwrite: true);
            }
        });
    }

    private async Task RollbackFromBackupAsync(string backupDir)
    {
        await Task.Run(() =>
        {
            var files = Directory.GetFiles(backupDir, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var destPath = Path.Combine(_appPath, fileName);
                File.Copy(file, destPath, overwrite: true);
            }
        });
    }

    private string? GetLatestBackup()
    {
        var backups = Directory.GetDirectories(_backupPath);
        if (backups.Length == 0)
            return null;

        Array.Sort(backups);
        return backups[backups.Length - 1];
    }

    private bool IsNewerVersion(string newVersion, string currentVersion)
    {
        try
        {
            var newVer = Version.Parse(newVersion);
            var curVer = Version.Parse(currentVersion);
            return newVer > curVer;
        }
        catch
        {
            return string.Compare(newVersion, currentVersion, StringComparison.Ordinal) > 0;
        }
    }
}

public class UpdateCheckResult
{
    public bool Success { get; set; }
    public bool UpdateAvailable { get; set; }
    public UpdateInfo? UpdateInfo { get; set; }
    public string? Error { get; set; }
}

public class UpdateResult
{
    public bool Success { get; set; }
    public string? NewVersion { get; set; }
    public string? BackupPath { get; set; }
    public long DurationMs { get; set; }
    public bool RolledBack { get; set; }
    public string? Error { get; set; }
}

public class UpdateInfo
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public DateTime ReleaseDate { get; set; }
    public bool IsCritical { get; set; }
    public long PackageSizeBytes { get; set; }
}
