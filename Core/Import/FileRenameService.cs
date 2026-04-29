using System.IO;
using System.Globalization;
using System.Threading;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Import;

/// <summary>
/// Renames and moves document files when classification changes.
/// Path: baseDir/Branch/Section/DocType/Date/{SanitizedType}_{yyyyMMdd}_{DocumentId}.ext
/// Uses user/import document date (<see cref="Document.ExtractedDate"/>) for path and filename; if unset, UTC today.
/// </summary>
public class FileRenameService : IFileRenameService
{
    private readonly IConfigStore _configStore;
    private readonly ILogger _log = LoggingService.ForContext<FileRenameService>();

    public FileRenameService(IConfigStore configStore)
    {
        _configStore = configStore;
    }

    /// <summary>Get document date as yyyy-MM-dd from <see cref="Document.ExtractedDate"/> only; else UTC today.</summary>
    internal static string GetDocumentDateYyyyMmDd(Document doc)
    {
        if (!string.IsNullOrEmpty(doc.ExtractedDate) && doc.ExtractedDate.Trim().Length >= 10)
        {
            var s = doc.ExtractedDate.Trim().Substring(0, 10);
            if (s.Length == 10 && s[4] == '-' && s[7] == '-')
                return s;
        }
        return DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public bool TryRenameAndMoveForClassification(Document doc, string newType, string baseDir)
    {
        return TryRenameAndMoveForClassification(doc, newType, baseDir, out _, out _);
    }

    /// <inheritdoc />
    public bool TryRenameAndMoveForClassification(Document doc, string newType, string baseDir, out string? failureReason)
    {
        return TryRenameAndMoveForClassification(doc, newType, baseDir, out _, out failureReason);
    }

    /// <inheritdoc />
    public bool TryRenameAndMoveForClassification(Document doc, string newType, string baseDir, out string? originalPath, out string? failureReason)
    {
        originalPath = doc.FilePath; // Capture original path for rollback
        failureReason = null;
        if (!_configStore.GetSettingBool("rename_file_on_classify", true))
        {
            failureReason = "Rename on classify is disabled by configuration.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(newType))
        {
            failureReason = "Document type is empty.";
            return false;
        }

        var currentPath = doc.FilePath;
        if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath))
        {
            failureReason = "Source file does not exist at recorded path.";
            return false;
        }

        var baseDirNorm = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var currentFull = Path.GetFullPath(currentPath);
        if (!currentFull.StartsWith(baseDirNorm + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !currentFull.StartsWith(baseDirNorm + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            _log.Debug("FileRenameService: skipping {Path} - not under base dir", currentPath);
            failureReason = "Source path is outside the configured base directory.";
            return false;
        }

        var documentDate = GetDocumentDateYyyyMmDd(doc);
        var dateForFileName = documentDate.Replace("-", "");

        var ext = Path.GetExtension(currentPath);
        var sanitizedType = Folders.SanitizeSegment(newType);
        var branch = doc.Branch ?? Branches.Default;
        var section = doc.Section ?? Enums.Section.Individuals;
        var relativeDir = Folders.GetDocumentPath(section, newType, branch, doc.ClearingDirection, doc.ClearingStatus, documentDate);
        var destDir = Path.Combine(baseDir, relativeDir);
        var newFileName = $"{sanitizedType}_{dateForFileName}_{doc.Id}{ext}";

        var destPath = Path.Combine(destDir, newFileName);

        if (string.Equals(currentFull, Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
        {
            failureReason = "Source and destination paths are the same.";
            return false;
        }

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                Directory.CreateDirectory(destDir);
                if (Path.GetPathRoot(currentFull)?.TrimEnd('\\', '/') == Path.GetPathRoot(destPath)?.TrimEnd('\\', '/'))
                {
                    File.Move(currentPath, destPath);
                }
                else
                {
                    File.Copy(currentPath, destPath);
                    File.Delete(currentPath);
                }

                doc.FilePath = destPath;
                _log.Information("FileRenameService: moved {Old} -> {New}", currentPath, destPath);
                return true;
            }
            catch (IOException ex) when (attempt < 2)
            {
                // Some locks are transient (preview/render pipelines); retry once after a short delay.
                _log.Debug(ex, "FileRenameService: transient I/O move failure for {Path}; retrying once", currentPath);
                Thread.Sleep(200);
            }
            catch (IOException ex)
            {
                _log.Warning(ex, "FileRenameService: failed to move {Path} (file in use?)", currentPath);
                failureReason = "I/O error while moving file (file may be in use).";
                return false;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "FileRenameService: unexpected failure moving {Path}", currentPath);
                failureReason = "Unexpected error while moving file.";
                return false;
            }
        }

        failureReason = "I/O error while moving file (file may be in use).";
        return false;
    }

    /// <inheritdoc />
    public bool RollbackRename(string currentPath, string originalPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath) || string.IsNullOrWhiteSpace(originalPath))
        {
            _log.Warning("RollbackRename called with null/empty paths");
            return false;
        }

        // Don't attempt rollback if paths are the same
        if (string.Equals(Path.GetFullPath(currentPath), Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase))
        {
            _log.Debug("RollbackRename: paths are identical, no rollback needed");
            return true;
        }

        // Only rollback if the current file exists and original doesn't
        if (!File.Exists(currentPath))
        {
            _log.Debug("RollbackRename: current file does not exist at {Path}, cannot rollback", currentPath);
            return false;
        }

        if (File.Exists(originalPath))
        {
            _log.Warning("RollbackRename: original file already exists at {Path}, skipping rollback", originalPath);
            return false;
        }

        try
        {
            var originalDir = Path.GetDirectoryName(originalPath);
            if (!string.IsNullOrEmpty(originalDir))
            {
                Directory.CreateDirectory(originalDir);
            }

            // Use same logic as forward move: File.Move for same volume, Copy+Delete for cross-volume
            var currentFull = Path.GetFullPath(currentPath);
            var originalFull = Path.GetFullPath(originalPath);
            
            if (Path.GetPathRoot(currentFull)?.TrimEnd('\\', '/') == Path.GetPathRoot(originalFull)?.TrimEnd('\\', '/'))
            {
                File.Move(currentPath, originalPath);
            }
            else
            {
                File.Copy(currentPath, originalPath);
                File.Delete(currentPath);
            }

            _log.Information("RollbackRename: successfully moved file back from {Current} to {Original}", currentPath, originalPath);
            return true;
        }
        catch (IOException ex)
        {
            _log.Error(ex, "RollbackRename: I/O error moving file back from {Current} to {Original}", currentPath, originalPath);
            return false;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "RollbackRename: unexpected error moving file back from {Current} to {Original}", currentPath, originalPath);
            return false;
        }
    }
}
