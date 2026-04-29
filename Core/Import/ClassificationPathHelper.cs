using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Threading;
using Oracle.ManagedDataAccess.Client;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Import;

/// <summary>Handles classification renames when several document rows point at the same file.</summary>
public static class ClassificationPathHelper
{
    private const string UnresolvedPrefix = "\0unresolved:";
    private static readonly int[] RetryDelaysMs = { 50, 100, 200, 400, 800, 1600 };

    /// <summary>
    /// Validates that a document can be renamed/moved before attempting the operation.
    /// </summary>
    private static bool ValidateBeforeMove(Document doc, string baseDir, out string? reason)
    {
        reason = null;

        // Check file exists and is accessible
        if (string.IsNullOrWhiteSpace(doc.FilePath))
        {
            reason = "Stored file path is empty";
            return false;
        }

        if (!File.Exists(doc.FilePath))
        {
            reason = "Source file does not exist";
            return false;
        }

        // Check if file is locked by trying to open it exclusively
        try
        {
            using var stream = File.Open(doc.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (IOException)
        {
            reason = "File is currently locked by another process (may be open in preview or another application)";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            reason = "Access denied to source file";
            return false;
        }

        // Check destination directory is writable
        try
        {
            var testFile = Path.Combine(baseDir, $".write_test_{Guid.NewGuid()}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
        }
        catch (IOException ex)
        {
            reason = $"Destination directory is not writable: {ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            reason = "Access denied to destination directory";
            return false;
        }

        // Check disk space
        try
        {
            var fileInfo = new FileInfo(doc.FilePath);
            var drive = new DriveInfo(Path.GetPathRoot(baseDir) ?? baseDir);
            if (drive.AvailableFreeSpace < fileInfo.Length * 2) // 2x for safety
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "Insufficient disk space (need {0:N0} bytes, have {1:N0} bytes)",
                    fileInfo.Length * 2,
                    drive.AvailableFreeSpace);
                return false;
            }
        }
        catch
        {
            // If we can't check disk space, continue anyway
        }

        return true;
    }

    private static bool TryWithRetries(Func<bool> action, out string? failureReason, int maxAttempts = 6)
    {
        failureReason = null;
        var attempts = Math.Max(1, maxAttempts);
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                if (action())
                    return true;

                failureReason = "Database update affected 0 rows.";
            }
            catch (OracleException ex)
            {
                failureReason = $"Oracle error ({ex.Number}): {ex.Message}";
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
            }

            if (i < attempts - 1)
                Thread.Sleep(RetryDelaysMs[Math.Min(i, RetryDelaysMs.Length - 1)]);
        }

        return false;
    }

    private static IEnumerable<string> BuildEquivalentStoredPaths(string? rawPath, string baseDir)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawPath))
            return values;

        var trimmed = rawPath.Trim();
        values.Add(trimmed);

        try
        {
            var baseFull = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Path.IsPathRooted(trimmed))
            {
                var full = Path.GetFullPath(trimmed);
                values.Add(full);
                if (full.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = full.Substring(baseFull.Length + 1);
                    values.Add(relative);
                }
            }
            else
            {
                var combined = Path.GetFullPath(Path.Combine(baseDir, trimmed));
                values.Add(combined);
            }
        }
        catch
        {
            // Keep original path token only.
        }

        return values;
    }

    public static Dictionary<int, string> VerifyClassificationInvariants(
        IDocumentStore store,
        IEnumerable<int> documentIds,
        string expectedType,
        string expectedSection,
        Func<Document, string?> resolveExistingPath)
    {
        var failures = new Dictionary<int, string>();
        var normalizedExpectedType = DocumentTypeInfo.NormalizePickerToStorage(expectedType);
        var expectedSectionValue = expectedSection ?? "";

        foreach (var id in documentIds.Distinct())
        {
            var loaded = store.GetResult(id);
            if (!loaded.IsSuccess || loaded.Value == null)
            {
                failures[id] = "Document row could not be reloaded from database.";
                continue;
            }

            var dbDoc = loaded.Value;
            var normalizedActualType = DocumentTypeInfo.NormalizePickerToStorage(dbDoc.DocumentType);
            if (!string.Equals(normalizedActualType, normalizedExpectedType, StringComparison.OrdinalIgnoreCase))
            {
                failures[id] = $"Type mismatch after refresh (expected '{normalizedExpectedType}', got '{normalizedActualType}').";
                continue;
            }

            var actualSection = dbDoc.Section ?? "";
            if (!string.Equals(actualSection, expectedSectionValue, StringComparison.OrdinalIgnoreCase))
            {
                failures[id] = $"Section mismatch after refresh (expected '{expectedSectionValue}', got '{actualSection}').";
                continue;
            }

            if (resolveExistingPath(dbDoc) == null)
            {
                failures[id] = "File path does not resolve to an existing file after refresh.";
            }
        }

        return failures;
    }

    /// <summary>
    /// Runs at most one physical move per distinct file on disk; updates every document row that shared that file to the new path and type.
    /// Reloads each document from <paramref name="store"/> before resolving paths and renaming so metadata (e.g. extracted date) matches the database.
    /// After a successful move, persists <paramref name="newType"/> for every id in the file group so <c>document_type</c> cannot lag <c>file_path</c>.
    /// </summary>
    public static ClassificationMovesResult ApplyMovesForDocumentsSharingFiles(
        IDocumentStore store,
        IFileRenameService fileRenameService,
        IReadOnlyList<Document> documentsWithTypeApplied,
        string newType,
        string baseDir,
        Func<Document, string?> resolveExistingPath)
    {
        var unresolved = new List<int>();
        var renameFailed = new List<int>();
        var unresolvedReasons = new Dictionary<int, string>();
        var renameFailedReasons = new Dictionary<int, string>();
        var distinctFilesMoved = 0;

        var refreshed = new List<Document>();
        foreach (var d in documentsWithTypeApplied)
        {
            var r = store.GetResult(d.Id);
            if (r.IsSuccess && r.Value != null)
                refreshed.Add(r.Value);
        }

        foreach (var group in refreshed.GroupBy(d => resolveExistingPath(d) ?? UnresolvedPrefix + d.Id))
        {
            if (group.Key.StartsWith(UnresolvedPrefix, StringComparison.Ordinal))
            {
                foreach (var doc in group)
                {
                    unresolved.Add(doc.Id);
                    unresolvedReasons[doc.Id] = string.IsNullOrWhiteSpace(doc.FilePath)
                        ? "Stored file path is empty."
                        : $"Could not resolve file path on disk: {doc.FilePath}";
                }
                continue;
            }

            var list = group.ToList();
            var first = list[0];
            var originalStoredPath = first.FilePath;

            // Include unselected sibling rows that share the same underlying file path.
            // Without this, moving one selected row can leave another row orphaned on old path.
            foreach (var pathToken in BuildEquivalentStoredPaths(originalStoredPath, baseDir))
            {
                var siblings = store.GetDocumentsByFilePath(pathToken);
                foreach (var sibling in siblings)
                {
                    if (list.All(x => x.Id != sibling.Id))
                        list.Add(sibling);
                }
            }
            
            // Pre-flight validation to catch predictable failures early
            if (!ValidateBeforeMove(first, baseDir, out var validationReason))
            {
                var reason = string.IsNullOrWhiteSpace(validationReason)
                    ? "Pre-flight validation failed."
                    : validationReason!;
                foreach (var doc in list)
                {
                    renameFailed.Add(doc.Id);
                    renameFailedReasons[doc.Id] = reason;
                }
                continue;
            }
            
            // Track original path for potential rollback
            var originalPath = first.FilePath;
            
            if (!fileRenameService.TryRenameAndMoveForClassification(first, newType, baseDir, out var actualOriginalPath, out var failureReason))
            {
                var reason = string.IsNullOrWhiteSpace(failureReason)
                    ? "Rename/move did not complete."
                    : failureReason!;
                foreach (var doc in list)
                {
                    renameFailed.Add(doc.Id);
                    renameFailedReasons[doc.Id] = reason;
                }
                continue;
            }

            var movedPath = first.FilePath;
            var groupPersisted = false;
            string? groupFailureReason = null;

            using var conn = store.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var doc in list)
                {
                    if (!TryWithRetries(() => store.UpdateDocumentType(doc.Id, newType, tx), out var typeFailure))
                    {
                        groupFailureReason = $"Failed to persist document_type for doc {doc.Id}: {typeFailure}";
                        break;
                    }
                    doc.DocumentType = newType;
                }

                if (groupFailureReason == null)
                {
                    if (!TryWithRetries(() => store.UpdateDocumentFilePath(first.Id, movedPath, tx), out var firstPathFailure))
                    {
                        groupFailureReason = $"Failed to persist file_path for doc {first.Id}: {firstPathFailure}";
                    }
                }

                if (groupFailureReason == null)
                {
                    foreach (var other in list.Skip(1))
                    {
                        other.FilePath = movedPath;
                        if (!TryWithRetries(() => store.UpdateDocumentFilePath(other.Id, movedPath, tx), out var otherPathFailure))
                        {
                            groupFailureReason = $"Failed to persist file_path for doc {other.Id}: {otherPathFailure}";
                            break;
                        }
                    }
                }

                if (groupFailureReason == null)
                {
                    tx.Commit();
                    groupPersisted = true;
                }
                else
                {
                    tx.Rollback();
                }
            }
            catch (Exception ex)
            {
                groupFailureReason = $"Exception while persisting group: {ex.Message}";
                try { tx.Rollback(); } catch { /* ignore rollback failures */ }
            }

            if (!groupPersisted)
            {
                var reason = groupFailureReason ?? "Database update failed after file move.";
                if (fileRenameService.RollbackRename(movedPath, actualOriginalPath ?? originalPath))
                {
                    first.FilePath = actualOriginalPath ?? originalPath;
                    reason += " File has been moved back to original location.";
                }
                else
                {
                    reason += " WARNING: File could not be moved back - manual intervention required!";
                }

                foreach (var doc in list)
                {
                    renameFailed.Add(doc.Id);
                    renameFailedReasons[doc.Id] = reason;
                }
                continue;
            }

            distinctFilesMoved++;
        }

        return new ClassificationMovesResult
        {
            DistinctFilesMoved = distinctFilesMoved,
            UnresolvedPathDocumentIds = unresolved,
            UnresolvedPathReasons = unresolvedReasons,
            RenameFailedDocumentIds = renameFailed,
            RenameFailedReasons = renameFailedReasons
        };
    }

    /// <summary>Extra lines for a message box after set type/section (English; callers may prepend localized headline).</summary>
    public static string FormatMoveFootnote(ClassificationMovesResult moves)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Files renamed/moved on disk: {moves.DistinctFilesMoved}.");
        if (moves.UnresolvedPathDocumentIds.Count > 0)
        {
            var details = moves.UnresolvedPathDocumentIds
                .Select(id => moves.UnresolvedPathReasons.TryGetValue(id, out var reason) ? $"{id} ({reason})" : $"{id}")
                .ToList();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Path could not be resolved (filename not updated) — document ID(s): {string.Join("; ", details)}.");
        }
        if (moves.RenameFailedDocumentIds.Count > 0)
        {
            var details = moves.RenameFailedDocumentIds
                .Select(id => moves.RenameFailedReasons.TryGetValue(id, out var reason) ? $"{id} ({reason})" : $"{id}")
                .ToList();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Rename/move did not complete (filename may be unchanged) — document ID(s): {string.Join("; ", details)}.");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Combines outcomes from a first rename pass and a second pass over the retry subset; final failure lists come from the second pass.</summary>
    public static ClassificationMovesResult MergeMoveResults(ClassificationMovesResult first, ClassificationMovesResult second) =>
        new ClassificationMovesResult
        {
            DistinctFilesMoved = first.DistinctFilesMoved + second.DistinctFilesMoved,
            UnresolvedPathDocumentIds = second.UnresolvedPathDocumentIds,
            UnresolvedPathReasons = new Dictionary<int, string>(second.UnresolvedPathReasons),
            RenameFailedDocumentIds = second.RenameFailedDocumentIds,
            RenameFailedReasons = new Dictionary<int, string>(second.RenameFailedReasons)
        };

}
