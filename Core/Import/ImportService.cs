using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Serilog;
using UglyToad.PdfPig;
using WorkAudit.Config;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;
using PDFtoImage;
using WorkAudit.Core.TextExtraction;
using WorkAudit.Core.Helpers;

namespace WorkAudit.Core.Import;

/// <summary>
/// Service for importing documents from files.
/// Supports images and PDFs.
/// </summary>
public interface IImportService
{
    Task<ImportResult> ImportFileAsync(string filePath, ImportOptions options, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportFilesAsync(IEnumerable<string> filePaths, ImportOptions options, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportFolderAsync(string folderPath, ImportOptions options, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<ReprocessResult> ReprocessDocumentAsync(int documentId, ReprocessOptions options, CancellationToken cancellationToken = default);
    /// <summary>Imports one PDF as a single document row (e.g. merged PDF from Processing). Copies into base dir when <see cref="ImportOptions.CopyToBaseDir"/> is true.</summary>
    Task<Document?> ImportSinglePdfDocumentAsync(string pdfPath, ImportOptions options, Document metadataTemplate, CancellationToken cancellationToken = default);
    bool IsSupportedFile(string filePath);
    string[] GetSupportedExtensions();
}

public class ImportService : IImportService
{
    /// <summary>Result of attempting to import a single image file.</summary>
    private sealed record ProcessImageOutcome(Document? Document, ProcessImageFailure? Failure);

    /// <summary>When <see cref="ProcessImageOutcome.Document"/> is null.</summary>
    private sealed record ProcessImageFailure(bool CountAsSkipped, string Message);

    private readonly ILogger _log = LoggingService.ForContext<ImportService>();
    private readonly IDocumentStore _documentStore;
    private readonly INotesStore? _notesStore;
    private readonly IAuditTrailService _auditTrail;
    private readonly IFileRenameService _fileRenameService;
    private readonly AppConfiguration _appConfig;
    private readonly IConfigStore _configStore;
    private readonly IOcrService _ocrService;

    private static readonly string[] SupportedImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".tif" };
    private static readonly string[] SupportedPdfExtensions = { ".pdf" };
    private const int PdfRenderDpi = 200;

    public ImportService(IDocumentStore documentStore, IAuditTrailService auditTrail, IFileRenameService fileRenameService, AppConfiguration appConfig, IConfigStore configStore, IOcrService ocrService, INotesStore? notesStore = null)
    {
        _documentStore = documentStore;
        _notesStore = notesStore;
        _auditTrail = auditTrail;
        _fileRenameService = fileRenameService;
        _appConfig = appConfig;
        _configStore = configStore;
        _ocrService = ocrService;
    }

    /// <summary>Sets <see cref="Document.CreatedBy"/> from the logged-in user so Processing queue filters match imports.</summary>
    private void StampImportCreator(Document doc)
    {
        var u = DocumentCreatedBy.FromAppConfiguration(_appConfig);
        if (!string.IsNullOrEmpty(u))
            doc.CreatedBy = u;
    }

    public string[] GetSupportedExtensions()
    {
        return SupportedImageExtensions.Concat(SupportedPdfExtensions).ToArray();
    }

    public bool IsSupportedFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedImageExtensions.Contains(ext) || SupportedPdfExtensions.Contains(ext);
    }

    /// <summary>Normalizes paths and drops duplicates while preserving first-seen order (case-insensitive on Windows).</summary>
    private static List<string> DeduplicatePathsPreservingOrder(IEnumerable<string> paths)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        var result = new List<string>();
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            string normalized;
            try
            {
                normalized = Path.GetFullPath(p.Trim());
            }
            catch
            {
                normalized = p.Trim();
            }

            if (seen.Add(normalized))
                result.Add(normalized);
        }

        return result;
    }

    public async Task<ImportResult> ImportFileAsync(string filePath, ImportOptions options, CancellationToken cancellationToken = default)
    {
        var result = new ImportResult();

        if (!File.Exists(filePath))
        {
            result.Errors.Add($"File not found: {filePath}");
            return result;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        try
        {
            if (SupportedPdfExtensions.Contains(ext))
            {
                var pdfResults = await ImportPdfAsync(filePath, options, cancellationToken);
                result.Merge(pdfResults);
            }
            else if (SupportedImageExtensions.Contains(ext))
            {
                var outcome = await ProcessImageAsync(filePath, options, cancellationToken);
                if (outcome.Document != null)
                {
                    result.ImportedDocuments.Add(outcome.Document);
                    result.SuccessCount++;
                }
                else if (outcome.Failure != null)
                {
                    if (outcome.Failure.CountAsSkipped)
                    {
                        result.SkippedCount++;
                        result.Warnings.Add($"{outcome.Failure.Message} — {filePath}");
                    }
                    else
                    {
                        result.FailedCount++;
                        result.Errors.Add($"{outcome.Failure.Message} — {filePath}");
                    }
                }
                else
                {
                    result.FailedCount++;
                    result.Errors.Add($"Failed to process (unknown reason) — {filePath}");
                }
            }
            else
            {
                result.SkippedCount++;
                result.Warnings.Add($"Unsupported file type: {ext}");
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error importing file: {Path}", filePath);
            result.FailedCount++;
            result.Errors.Add($"Error: {ex.Message}");
        }

        return result;
    }

    public async Task<ImportResult> ImportFilesAsync(IEnumerable<string> filePaths, ImportOptions options, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = new ImportResult();
        var files = DeduplicatePathsPreservingOrder(filePaths);
        var processed = 0;

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileResult = await ImportFileAsync(filePath, options, cancellationToken);
            result.Merge(fileResult);

            processed++;
            progress?.Report(new ImportProgress
            {
                CurrentFile = filePath,
                ProcessedCount = processed,
                TotalCount = files.Count,
                PercentComplete = files.Count > 0 ? (int)(processed * 100.0 / files.Count) : 100
            });
        }

        progress?.Report(new ImportProgress
        {
            ProcessedCount = processed,
            TotalCount = files.Count,
            PercentComplete = 100,
            IsComplete = true
        });

        return result;
    }

    public async Task<ImportResult> ImportFolderAsync(string folderPath, ImportOptions options, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folderPath))
        {
            return new ImportResult { Errors = { $"Folder not found: {folderPath}" } };
        }

        var searchOption = options.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = DeduplicatePathsPreservingOrder(
            GetSupportedExtensions()
                .SelectMany(ext => Directory.GetFiles(folderPath, $"*{ext}", searchOption)));

        _log.Information("Found {Count} files to import in {Folder}", files.Count, folderPath);

        return await ImportFilesAsync(files, options, progress, cancellationToken);
    }

    public async Task<ReprocessResult> ReprocessDocumentAsync(int documentId, ReprocessOptions options, CancellationToken cancellationToken = default)
    {
        var result = new ReprocessResult();
        _log.Information("ReprocessDocumentAsync doc Id={Id}", documentId);

        var getResult = _documentStore.GetResult(documentId);
        if (!getResult.IsSuccess)
        {
            _log.Warning("ReprocessDocumentAsync doc {Id}: {Error}", documentId, getResult.Error);
            result.Errors.Add(getResult.Error ?? $"Document {documentId} not found.");
            return result;
        }
        var doc = getResult.Value!;
        if (string.IsNullOrEmpty(doc.FilePath))
        {
            _log.Warning("ReprocessDocumentAsync doc {Id} has no file path", documentId);
            result.Errors.Add($"Document {documentId} has no file path.");
            return result;
        }
        if (!File.Exists(doc.FilePath))
        {
            _log.Warning("ReprocessDocumentAsync file not found: {Path}", doc.FilePath);
            result.Errors.Add($"File not found: {doc.FilePath}");
            return result;
        }
        var ext = Path.GetExtension(doc.FilePath).ToLowerInvariant();
        try
        {
            if (SupportedImageExtensions.Contains(ext))
            {
                _log.Debug("ReprocessDocumentAsync doc {Id} -> image path", documentId);
                await ReprocessImageDocumentAsync(doc, options, result, cancellationToken);
            }
            else if (SupportedPdfExtensions.Contains(ext))
            {
                _log.Debug("ReprocessDocumentAsync doc {Id} -> PDF path", documentId);
                await ReprocessPdfDocumentAsync(doc, options, result, cancellationToken);
            }
            else
            {
                _log.Warning("ReprocessDocumentAsync doc {Id} unsupported extension: {Ext}", documentId, ext);
                result.Errors.Add($"Reprocess supports images and PDF only; got {ext}");
                return result;
            }

            var ocrLen = doc.OcrText?.Length ?? 0;
            _log.Information("ReprocessDocumentAsync doc {Id} after pipeline: OcrText length={Len}, ClassificationConfidence={Conf}", documentId, ocrLen, doc.ClassificationConfidence);

            if (_documentStore.Update(doc))
            {
                result.SuccessCount = 1;
                _log.Information("ReprocessDocumentAsync doc {Id} updated in store (type={Type}, classConf={Conf})", documentId, doc.DocumentType, doc.ClassificationConfidence);
                var detail = "Reprocessed (extraction)";
                await _auditTrail.LogDocumentActionAsync(AuditAction.DocumentUpdated, doc, detail);
            }
            else
            {
                _log.Error("ReprocessDocumentAsync doc {Id}: Update(doc) returned false", documentId);
                result.FailedCount = 1;
                result.Errors.Add("Failed to update document in store.");
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Reprocess failed for document {Id}: {Message}", documentId, ex.Message);
            result.FailedCount = 1;
            result.Errors.Add($"{ex.Message}");
        }

        return result;
    }

    private Task ReprocessImageDocumentAsync(Document doc, ReprocessOptions options, ReprocessResult result, CancellationToken cancellationToken)
    {
        var docType = DocumentTypeInfo.FolderSegmentForType(doc.DocumentType);
        if (!string.IsNullOrEmpty(docType))
        {
            var baseDir = !string.IsNullOrEmpty(_appConfig.BaseDirectory) ? _appConfig.BaseDirectory : Config.Defaults.GetDefaultBaseDir();
            _fileRenameService.TryRenameAndMoveForClassification(doc, docType, baseDir);
        }
        return Task.CompletedTask;
    }

    private Task ReprocessPdfDocumentAsync(Document doc, ReprocessOptions options, ReprocessResult result, CancellationToken cancellationToken)
    {
        int pageNumber = 1;
        var notesText = GetNotesTextForDocument(doc.Id);
        if (!string.IsNullOrEmpty(notesText))
        {
            var match = Regex.Match(notesText, @"Page\s+(\d+)\s+of\s+\d+", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var p) && p >= 1)
                pageNumber = p;
        }

        string text;
        using (var pdfDoc = UglyToad.PdfPig.PdfDocument.Open(doc.FilePath))
        {
            if (pageNumber > pdfDoc.NumberOfPages)
            {
                pageNumber = 1;
            }
            var page = pdfDoc.GetPage(pageNumber);
            text = page.Text ?? "";
        }

        doc.OcrText = text;
        doc.Snippet = GenerateSnippet(text);
        _log.Information("ReprocessPdfDocumentAsync doc {Id}: extracted text length={Len}", doc.Id, text?.Length ?? 0);

        var docType = DocumentTypeInfo.FolderSegmentForType(doc.DocumentType);
        if (!string.IsNullOrEmpty(docType))
        {
            var baseDir = !string.IsNullOrEmpty(_appConfig.BaseDirectory) ? _appConfig.BaseDirectory : Config.Defaults.GetDefaultBaseDir();
            _fileRenameService.TryRenameAndMoveForClassification(doc, docType, baseDir);
        }
        return Task.CompletedTask;
    }

    private string GetNotesTextForDocument(int documentId)
    {
        if (_notesStore == null) return "";
        var notes = _notesStore.GetByDocumentId(documentId);
        if (notes.Count == 0) return "";
        return string.Join(" ", notes.Select(n => n.Content));
    }

    private async Task<ImportResult> ImportPdfAsync(string pdfPath, ImportOptions options, CancellationToken cancellationToken)
    {
        var result = new ImportResult();

        try
        {
            // Read all page text while the PDF is open, then dispose before File.Copy.
            // On Windows, copying the same path while PdfPig holds the file open causes
            // IOException: "being used by another process".
            int pageCount;
            List<string> pageTexts;
            using (var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
            {
                pageCount = document.NumberOfPages;
                pageTexts = new List<string>(pageCount);

                _log.Information("Importing PDF with {Pages} pages: {Path}", pageCount, pdfPath);

                for (int i = 1; i <= pageCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var page = document.GetPage(i);
                    var text = page.Text ?? "";

                    if (options.PreExtractedPageTexts != null && options.PreExtractedPageTexts.Count >= i)
                    {
                        var preText = options.PreExtractedPageTexts[i - 1];
                        if (!string.IsNullOrWhiteSpace(preText))
                        {
                            text = preText;
                        }
                    }

                    pageTexts.Add(text);
                }
            }

            for (int i = 1; i <= pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var text = pageTexts[i - 1];

                var doc = new Document
                {
                    FilePath = pdfPath,
                    PageCount = pageCount,
                    Source = "import:pdf",
                    Branch = options.Branch ?? Domain.Branches.Default,
                    Section = options.Section ?? Enums.Section.Individuals,
                    DocumentType = string.IsNullOrWhiteSpace(options.DocumentType) ? null : options.DocumentType.Trim(),
                    ExtractedDate = options.DocumentDate.HasValue ? options.DocumentDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                    OcrText = text,
                    Snippet = GenerateSnippet(text),
#pragma warning disable CS0618
                    Notes = $"Page {i} of {pageCount}"
#pragma warning restore CS0618
                };

                if (options.CopyToBaseDir)
                {
                    var destPath = GetDestinationPath(pdfPath, doc, options);
                    CopyFile(pdfPath, destPath);
                    doc.FilePath = destPath;
                }

                StampImportCreator(doc);
                var insertResult = _documentStore.InsertResult(doc);
                if (!insertResult.IsSuccess)
                {
                    _log.Warning("PDF import failed to insert document: {Error}", insertResult.Error);
                    result.FailedCount++;
                    var detail = string.IsNullOrWhiteSpace(insertResult.Error)
                        ? "Failed to save document"
                        : insertResult.Error!;
                    result.Errors.Add($"{detail} (page {i} of {pageCount}) — {pdfPath}");
                    return result;
                }
                doc.Id = (int)insertResult.Value;
                TryFinalizeImportedPathWithDocumentId(doc, options);

                // Trigger OCR if PdfPig text is empty or below quality threshold (scanned PDFs with junk embedded text)
                if (DocumentWorkspaceOcr.PdfExtractedTextNeedsOcr(doc.OcrText, _configStore))
                {
                    _ocrService.EnqueueOcrTask(doc);
                }

                await _auditTrail.LogDocumentActionAsync(AuditAction.DocumentCreated, doc, "PDF import");

                result.ImportedDocuments.Add(doc);
                result.SuccessCount++;
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error importing PDF: {Path}", pdfPath);
            result.FailedCount++;
            result.Errors.Add($"PDF import error: {ex.Message}");
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Document?> ImportSinglePdfDocumentAsync(string pdfPath, ImportOptions options, Document metadataTemplate, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pdfPath))
        {
            _log.Warning("ImportSinglePdfDocumentAsync: file not found: {Path}", pdfPath);
            return null;
        }

        if (!string.Equals(Path.GetExtension(pdfPath), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            _log.Warning("ImportSinglePdfDocumentAsync: not a PDF: {Path}", pdfPath);
            return null;
        }

        int pageCount;
        var pageTexts = new List<string>();
        using (var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
        {
            pageCount = document.NumberOfPages;
            for (var i = 1; i <= pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(i);
                pageTexts.Add(page.Text ?? "");
            }
        }

        var combinedText = string.Join("\n\n", pageTexts);
        var doc = new Document
        {
            FilePath = pdfPath,
            PageCount = pageCount,
            Source = "processing:merge",
            Branch = metadataTemplate.Branch ?? options.Branch ?? Domain.Branches.Default,
            Section = string.IsNullOrEmpty(metadataTemplate.Section) ? (options.Section ?? Enums.Section.Individuals) : metadataTemplate.Section,
            DocumentType = string.IsNullOrEmpty(metadataTemplate.DocumentType) ? null : metadataTemplate.DocumentType,
            ExtractedDate = !string.IsNullOrEmpty(metadataTemplate.ExtractedDate)
                    ? metadataTemplate.ExtractedDate
                    : (options.DocumentDate.HasValue ? options.DocumentDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null),
            OcrText = combinedText,
            Snippet = GenerateSnippet(combinedText),
#pragma warning disable CS0618
            Notes = "Merged PDF"
#pragma warning restore CS0618
        };

        var fileInfo = new FileInfo(pdfPath);
        doc.FileSize = fileInfo.Length;
        doc.FileHash = CalculateFileHash(pdfPath);
        if (options.SkipDuplicates && !string.IsNullOrEmpty(doc.FileHash))
        {
            var existing = _documentStore.GetByFileHash(doc.FileHash);
            if (existing != null)
            {
                if (options.UseExistingDocumentOnDuplicateHash)
                {
                    _log.Information("Merged PDF duplicate hash matches existing document {Id}; using existing record", existing.Id);
                    return existing;
                }
                _log.Warning("Merged PDF duplicate hash matches document {Id}", existing.Id);
                return null;
            }
        }

        if (options.CopyToBaseDir)
        {
            var destPath = GetDestinationPath(pdfPath, doc, options);
            CopyFile(pdfPath, destPath);
            doc.FilePath = destPath;
        }

        StampImportCreator(doc);
        var insertResult = _documentStore.InsertResult(doc);
        if (!insertResult.IsSuccess)
        {
            _log.Warning("ImportSinglePdfDocumentAsync insert failed: {Error}", insertResult.Error);
            return null;
        }

        doc.Id = (int)insertResult.Value;
        TryFinalizeImportedPathWithDocumentId(doc, options);

        if (DocumentWorkspaceOcr.PdfExtractedTextNeedsOcr(doc.OcrText, _configStore))
            _ocrService.EnqueueOcrTask(doc);

        await _auditTrail.LogDocumentActionAsync(AuditAction.DocumentCreated, doc, "Merged PDF from Processing");

        return doc;
    }

    private async Task<ProcessImageOutcome> ProcessImageAsync(string imagePath, ImportOptions options, CancellationToken cancellationToken)
    {
        var doc = new Document
        {
            FilePath = imagePath,
            Source = "import:file",
            Branch = options.Branch ?? Domain.Branches.Default,
            Section = options.Section ?? Enums.Section.Individuals,
            DocumentType = string.IsNullOrWhiteSpace(options.DocumentType) ? null : options.DocumentType.Trim(),
            PageCount = 1,
            ExtractedDate = options.DocumentDate.HasValue ? options.DocumentDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null
        };

        if (!string.IsNullOrWhiteSpace(options.PreExtractedText))
        {
            doc.OcrText = options.PreExtractedText;
            doc.Snippet = GenerateSnippet(doc.OcrText);
        }

        // Get file info
        var fileInfo = new FileInfo(imagePath);
        doc.FileSize = fileInfo.Length;
        doc.FileHash = CalculateFileHash(imagePath);

        // Check for duplicates by file hash
        if (options.SkipDuplicates && !string.IsNullOrEmpty(doc.FileHash))
        {
            var existing = _documentStore.GetByFileHash(doc.FileHash);
            if (existing != null)
            {
                doc.IsDuplicate = true;
                doc.DuplicateOf = existing.Uuid;
                _log.Warning("Duplicate detected: {Path} matches {Existing}", imagePath, existing.FilePath);

                if (options.SkipDuplicates)
                {
                    return new ProcessImageOutcome(null,
                        new ProcessImageFailure(true, $"Skipped duplicate (same content as document #{existing.Id})"));
                }
            }
        }

        if (options.CopyToBaseDir)
        {
            var destPath = GetDestinationPath(imagePath, doc, options);
            CopyFile(imagePath, destPath);
            doc.FilePath = destPath;
        }

        // No text extraction from images (AI/OCR removed)

        // Save to database
        StampImportCreator(doc);
        var insertResult = _documentStore.InsertResult(doc);
        if (!insertResult.IsSuccess)
        {
            _log.Warning("Image import failed to insert document: {Error}", insertResult.Error);
            var detail = string.IsNullOrWhiteSpace(insertResult.Error)
                ? "Could not save document"
                : insertResult.Error!;
            return new ProcessImageOutcome(null, new ProcessImageFailure(false, detail));
        }
        doc.Id = (int)insertResult.Value;
        TryFinalizeImportedPathWithDocumentId(doc, options);

        // Automatically trigger OCR for images
        _ocrService.EnqueueOcrTask(doc);

        await _auditTrail.LogDocumentActionAsync(AuditAction.DocumentCreated, doc, "Image import");

        return new ProcessImageOutcome(doc, null);
    }

    /// <summary>Renames <c>{yyyyMMdd}_{seq}</c> copy to <c>{yyyyMMdd}_{documentId}</c> under the same folder and updates the DB.</summary>
    private void TryFinalizeImportedPathWithDocumentId(Document doc, ImportOptions options)
    {
        if (!options.CopyToBaseDir || doc.Id <= 0)
            return;

        var current = doc.FilePath;
        if (string.IsNullOrEmpty(current) || !File.Exists(current))
            return;

        var ext = Path.GetExtension(current);
        var dir = Path.GetDirectoryName(current);
        if (string.IsNullOrEmpty(dir))
            return;

        var documentDateYyyyMmDd = GetDocumentDateForPath(doc, options);
        var dateForFileName = documentDateYyyyMmDd.Replace("-", "");
        var finalName = $"{dateForFileName}_{doc.Id}{ext}";
        var finalPath = Path.Combine(dir, finalName);

        if (string.Equals(Path.GetFullPath(current), Path.GetFullPath(finalPath), StringComparison.OrdinalIgnoreCase))
            return;

        if (File.Exists(finalPath))
        {
            _log.Warning("TryFinalizeImportedPathWithDocumentId: destination exists {Path}, leaving {Current}", finalPath, current);
            return;
        }

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                var currentFull = Path.GetFullPath(current);
                var finalFull = Path.GetFullPath(finalPath);
                if (Path.GetPathRoot(currentFull)?.TrimEnd('\\', '/') == Path.GetPathRoot(finalFull)?.TrimEnd('\\', '/'))
                    File.Move(current, finalPath);
                else
                {
                    File.Copy(current, finalPath);
                    File.Delete(current);
                }

                if (_documentStore.UpdateDocumentFilePath(doc.Id, finalPath))
                    doc.FilePath = finalPath;
                else
                    _log.Warning("TryFinalizeImportedPathWithDocumentId: failed to update file_path for doc {Id}", doc.Id);
                return;
            }
            catch (IOException ex) when (attempt < 2)
            {
                _log.Debug(ex, "TryFinalizeImportedPathWithDocumentId: retry rename for doc {Id}", doc.Id);
                Thread.Sleep(200);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "TryFinalizeImportedPathWithDocumentId: failed for doc {Id}", doc.Id);
                return;
            }
        }
    }

    private string GetDestinationPath(string sourcePath, Document doc, ImportOptions options)
    {
        var documentDateYyyyMmDd = GetDocumentDateForPath(doc, options);
        var dateForFileName = documentDateYyyyMmDd.Replace("-", "");

        var ext = Path.GetExtension(sourcePath);

        var basePath = options.BaseDirectory ?? Config.Defaults.GetDefaultBaseDir();
        var branch = options.Branch ?? doc.Branch;
        var relativePath = Folders.GetDocumentPath(
            doc.Section,
            DocumentTypeInfo.FolderSegmentForType(doc.DocumentType),
            branch,
            doc.ClearingDirection,
            doc.ClearingStatus,
            documentDateYyyyMmDd);

        var destDir = Path.Combine(basePath, relativePath);
        var namePrefix = $"{dateForFileName}_";
        var seq = DocumentFileNaming.GetNextSequenceInDirectory(destDir, namePrefix);
        var fileName = $"{dateForFileName}_{seq}{ext}";

        return Path.Combine(destDir, fileName);
    }

    private static string GetDocumentDateForPath(Document doc, ImportOptions options)
    {
        if (!string.IsNullOrEmpty(doc.ExtractedDate) && doc.ExtractedDate.Trim().Length >= 10)
        {
            var s = doc.ExtractedDate.Trim().Substring(0, 10);
            if (s.Length == 10 && s[4] == '-' && s[7] == '-')
                return s;
        }
        if (options.DocumentDate.HasValue)
            return options.DocumentDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private void CopyFile(string source, string destination)
    {
        try
        {
            var destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(source, destination, overwrite: true);
            _log.Debug("Copied file: {Source} -> {Destination}", source, destination);
        }
        catch (IOException ex)
        {
            _log.Error(ex, "File copy failed: {Source} -> {Dest}", source, destination);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Error(ex, "Access denied copying file: {Source} -> {Dest}", source, destination);
            throw;
        }
    }

    private string? CalculateFileHash(string filePath)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return null;
        }
    }

    private string GenerateSnippet(string? text, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var cleaned = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

        if (cleaned.Length <= maxLength)
            return cleaned;

        var truncated = cleaned.Substring(0, maxLength);
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
            truncated = truncated.Substring(0, lastSpace);

        return truncated + "...";
    }
}

public class ImportOptions
{
    public string? Branch { get; set; }
    public string? Section { get; set; }
    /// <summary>Optional type from Input tab; null means unclassified.</summary>
    public string? DocumentType { get; set; }
    public DateTime? DocumentDate { get; set; }
    public string? BaseDirectory { get; set; }
    public bool CopyToBaseDir { get; set; } = true;
    public bool IncludeSubfolders { get; set; }
    public bool SkipDuplicates { get; set; } = true;
    /// <summary>
    /// When <see cref="SkipDuplicates"/> is true and a file hash already exists, return that
    /// <see cref="Document"/> instead of null. Used by Processing merge so a byte-identical merged PDF
    /// still completes the job (sources removed) by pointing at the existing library document.
    /// </summary>
    public bool UseExistingDocumentOnDuplicateHash { get; set; }
    /// <summary>When true, extract text from PDFs during import.</summary>
    public bool ExtractPdf { get; set; }
    /// <summary>When true, extract text from photos/images during import.</summary>
    public bool ExtractPhoto { get; set; }
    /// <summary>Optional pre-extracted text for single image imports.</summary>
    public string? PreExtractedText { get; set; }
    /// <summary>Optional pre-extracted text for each page of a PDF import.</summary>
    public List<string>? PreExtractedPageTexts { get; set; }
}

public class ImportResult
{
    public List<Document> ImportedDocuments { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }

    public int TotalProcessed => SuccessCount + FailedCount + SkippedCount;
    public bool HasErrors => Errors.Count > 0;

    public void Merge(ImportResult other)
    {
        ImportedDocuments.AddRange(other.ImportedDocuments);
        Errors.AddRange(other.Errors);
        Warnings.AddRange(other.Warnings);
        SuccessCount += other.SuccessCount;
        FailedCount += other.FailedCount;
        SkippedCount += other.SkippedCount;
    }
}

public class ImportProgress
{
    public string? CurrentFile { get; set; }
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public int PercentComplete { get; set; }
    public bool IsComplete { get; set; }
}

public class ReprocessOptions
{
    // Note: All options removed as extraction features are no longer used
    // Reprocessing now only handles file rename/move based on classification
}

public class ReprocessResult
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool HasErrors => Errors.Count > 0;
}
