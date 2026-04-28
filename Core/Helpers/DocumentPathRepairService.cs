using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;
using WorkAudit.Core.Import;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Helpers;

/// <summary>
/// Service to detect and repair documents with invalid or missing file paths.
/// Useful when database records are out of sync with physical files.
/// </summary>
public class DocumentPathRepairService
{
    private readonly IDocumentStore _store;
    private readonly ILogger _log = LoggingService.ForContext<DocumentPathRepairService>();

    public DocumentPathRepairService(IDocumentStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Finds documents with empty or invalid file paths.
    /// </summary>
    public List<Document> FindDocumentsWithInvalidPaths()
    {
        var allDocs = _store.ListDocuments(limit: 5000);
        var invalid = allDocs
            .Where(d => string.IsNullOrEmpty(d.FilePath) || !File.Exists(d.FilePath))
            .ToList();

        _log.Information("Found {Count} documents with invalid paths", invalid.Count);
        return invalid;
    }

    /// <summary>
    /// Attempts to locate the correct file path for a document based on its metadata.
    /// Returns the reconstructed path if file exists, null otherwise.
    /// </summary>
    public string? TryReconstructFilePath(Document doc, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(doc.DocumentType))
        {
            _log.Debug("Cannot reconstruct path for document {Id}: no document type", doc.Id);
            return null;
        }

        var branch = doc.Branch ?? Branches.Default;
        var section = doc.Section ?? Enums.Section.Individuals;
        var documentDate = FileRenameService.GetDocumentDateYyyyMmDd(doc);
        
        // Get the relative directory path
        var relativeDir = Folders.GetDocumentPath(section, doc.DocumentType, branch, 
            doc.ClearingDirection, doc.ClearingStatus, documentDate);
        var targetDir = Path.Combine(baseDir, relativeDir);

        if (!Directory.Exists(targetDir))
        {
            _log.Debug("Target directory does not exist: {Dir}", targetDir);
            return null;
        }

        // Build expected filename pattern
        var sanitizedType = Folders.SanitizeSegment(doc.DocumentType);
        var dateForFileName = documentDate.Replace("-", "");
        var prefix = $"{sanitizedType}_{dateForFileName}_";

        // Prefer exact id-based name (classification / rename-with-id)
        foreach (var ext in new[] { ".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp" })
        {
            var candidate = Path.Combine(targetDir, $"{sanitizedType}_{dateForFileName}_{doc.Id}{ext}");
            if (!File.Exists(candidate))
                continue;
            if (!string.IsNullOrEmpty(doc.FileHash))
            {
                var h = CalculateFileHash(candidate);
                if (h != doc.FileHash)
                    continue;
            }

            _log.Information("Found id-based file for document {Id}: {Path}", doc.Id, candidate);
            return candidate;
        }

        // Legacy sequence suffix and other matches under same prefix
        var files = Directory.GetFiles(targetDir, $"{prefix}*")
            .Where(f => !string.IsNullOrEmpty(doc.FileHash) ? 
                CalculateFileHash(f) == doc.FileHash : 
                true)
            .ToList();

        if (files.Count == 1)
        {
            _log.Information("Found matching file for document {Id}: {Path}", doc.Id, files[0]);
            return files[0];
        }
        else if (files.Count > 1)
        {
            _log.Warning("Found multiple matching files for document {Id}, cannot auto-repair", doc.Id);
            return null;
        }
        else
        {
            _log.Debug("No matching file found for document {Id} in {Dir}", doc.Id, targetDir);
            return null;
        }
    }

    /// <summary>
    /// Repairs a single document by updating its file path in the database.
    /// </summary>
    public bool RepairDocument(Document doc, string correctPath)
    {
        if (string.IsNullOrEmpty(correctPath) || !File.Exists(correctPath))
        {
            _log.Warning("Cannot repair document {Id}: invalid path {Path}", doc.Id, correctPath);
            return false;
        }

        var success = _store.UpdateDocumentFilePath(doc.Id, correctPath);
        if (success)
        {
            _log.Information("Repaired document {Id}: updated path to {Path}", doc.Id, correctPath);
            doc.FilePath = correctPath;
        }
        else
        {
            _log.Error("Failed to update database for document {Id}", doc.Id);
        }

        return success;
    }

    /// <summary>
    /// Finds and repairs all documents with invalid paths.
    /// Returns count of successfully repaired documents.
    /// </summary>
    public RepairResult RepairAllInvalidPaths(string baseDir)
    {
        var result = new RepairResult();
        var invalidDocs = FindDocumentsWithInvalidPaths();
        
        result.TotalInvalid = invalidDocs.Count;

        foreach (var doc in invalidDocs)
        {
            var correctPath = TryReconstructFilePath(doc, baseDir);
            if (correctPath != null)
            {
                if (RepairDocument(doc, correctPath))
                {
                    result.Repaired++;
                }
                else
                {
                    result.FailedToUpdate.Add(doc.Id);
                }
            }
            else
            {
                result.CouldNotLocate.Add(doc.Id);
            }
        }

        _log.Information("Repair complete: {Repaired}/{Total} repaired, {NotFound} not found, {Failed} failed to update",
            result.Repaired, result.TotalInvalid, result.CouldNotLocate.Count, result.FailedToUpdate.Count);

        return result;
    }

    private string? CalculateFileHash(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to calculate hash for {Path}", filePath);
            return null;
        }
    }
}

/// <summary>Result of repair operation.</summary>
public class RepairResult
{
    public int TotalInvalid { get; set; }
    public int Repaired { get; set; }
    public List<int> CouldNotLocate { get; set; } = new();
    public List<int> FailedToUpdate { get; set; } = new();

    public bool HasIssues => CouldNotLocate.Count > 0 || FailedToUpdate.Count > 0;
}
