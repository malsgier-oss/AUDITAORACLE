using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WorkAudit.Config;
using WorkAudit.Core.Export;
using WorkAudit.Core.Helpers;
using WorkAudit.Core.Import;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

/// <summary>
/// Processes merge jobs one at a time on a background worker so the Processing view stays interactive.
/// </summary>
public sealed class ProcessingMergeQueueService : IProcessingMergeQueueService
{
    private readonly IDocumentStore _store;
    private readonly ISearchExportService _exportService;
    private readonly IImportService _importService;
    private readonly IPermissionService _permissionService;
    private readonly IAuditTrailService _auditTrail;
    private readonly ISecureDeleteService _secureDelete;
    private readonly AppConfiguration _appConfig;
    private readonly ISessionService _sessionService;
    private readonly IConfigStore _configStore;
    private readonly IProcessingProgressService _progressService;
    private readonly Serilog.ILogger _log = LoggingService.ForContext<ProcessingMergeQueueService>();

    private readonly Channel<MergeJob> _channel;
    private int _pendingJobs;
    private readonly object _mergeTimingLock = new();
    private readonly Queue<DateTime> _mergeJobsWaitingSince = new();
    private DateTime? _currentMergeJobEnqueuedUtc;

    public int PendingCount => Volatile.Read(ref _pendingJobs);

    /// <inheritdoc />
    public DateTime? OldestPendingMergeEnqueueUtc
    {
        get
        {
            lock (_mergeTimingLock)
            {
                var oldestWait = _mergeJobsWaitingSince.Count > 0 ? _mergeJobsWaitingSince.Peek() : (DateTime?)null;
                var cur = _currentMergeJobEnqueuedUtc;
                if (!oldestWait.HasValue) return cur;
                if (!cur.HasValue) return oldestWait;
                return oldestWait.Value <= cur.Value ? oldestWait : cur;
            }
        }
    }

    public event EventHandler<ProcessingMergeCompletedEventArgs>? MergeCompleted;
    public event EventHandler<ProcessingMergeFailedEventArgs>? MergeFailed;

    public ProcessingMergeQueueService(
        IDocumentStore store,
        ISearchExportService exportService,
        IImportService importService,
        IPermissionService permissionService,
        IAuditTrailService auditTrail,
        ISecureDeleteService secureDelete,
        AppConfiguration appConfig,
        ISessionService sessionService,
        IConfigStore configStore,
        IProcessingProgressService progressService)
    {
        _store = store;
        _exportService = exportService;
        _importService = importService;
        _permissionService = permissionService;
        _auditTrail = auditTrail;
        _secureDelete = secureDelete;
        _appConfig = appConfig;
        _sessionService = sessionService;
        _configStore = configStore;
        _progressService = progressService;

        _channel = Channel.CreateUnbounded<MergeJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _ = Task.Run(RunWorkerAsync);
    }

    /// <inheritdoc />
    public void Enqueue(IReadOnlyList<int> orderedDocumentIds, int? nextSelectionDocumentId, bool allowLossyPdfFallback = true)
    {
        if (orderedDocumentIds == null || orderedDocumentIds.Count < 2)
            throw new ArgumentException("At least two document IDs are required.", nameof(orderedDocumentIds));

        var enqueuedUtc = DateTime.UtcNow;
        lock (_mergeTimingLock)
        {
            Interlocked.Increment(ref _pendingJobs);
            var written = _channel.Writer.TryWrite(new MergeJob(orderedDocumentIds.ToArray(), nextSelectionDocumentId, allowLossyPdfFallback, enqueuedUtc));
            if (!written)
            {
                Interlocked.Decrement(ref _pendingJobs);
                throw new InvalidOperationException("Merge queue is not accepting jobs.");
            }

            _mergeJobsWaitingSince.Enqueue(enqueuedUtc);
        }
    }

    private async Task RunWorkerAsync()
    {
        await foreach (var job in _channel.Reader.ReadAllAsync())
        {
            DateTime jobEnqueuedUtc;
            lock (_mergeTimingLock)
            {
                jobEnqueuedUtc = _mergeJobsWaitingSince.Count > 0
                    ? _mergeJobsWaitingSince.Dequeue()
                    : job.EnqueuedAtUtc;
                _currentMergeJobEnqueuedUtc = jobEnqueuedUtc;
            }

            var n = Volatile.Read(ref _pendingJobs);
            var progressMsg = ReportLocalizationService.GetString("MergeToPdfProgress", _configStore);
            if (n > 1)
                progressMsg += " (" + n + " in queue)";
            _progressService.Start(0, progressMsg);

            try
            {
                var importedId = await ExecuteMergeJobAsync(job).ConfigureAwait(false);
                MergeCompleted?.Invoke(this, new ProcessingMergeCompletedEventArgs(importedId, job.NextSelectionDocumentId));
            }
            catch (MergeQueueUserMessageException ex)
            {
                if (!ex.IsWarning)
                    _log.Error(ex, "Merge queue job failed");
                MergeFailed?.Invoke(this, new ProcessingMergeFailedEventArgs(ex.Message, ex) { ShowAsWarning = ex.IsWarning });
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Merge queue job failed");
                MergeFailed?.Invoke(this, new ProcessingMergeFailedEventArgs(ex.Message, ex));
            }
            finally
            {
                lock (_mergeTimingLock)
                    _currentMergeJobEnqueuedUtc = null;
                if (Interlocked.Decrement(ref _pendingJobs) == 0)
                    _progressService.Complete();
            }
        }
    }

    private async Task<int> ExecuteMergeJobAsync(MergeJob job)
    {
        var loaded = _store.GetByIds(job.Ids.ToList());
        var byId = loaded.ToDictionary(d => d.Id);
        var mergeable = new List<Document>();
        foreach (var id in job.Ids)
        {
            if (!byId.TryGetValue(id, out var doc)) continue;
            if (!_permissionService.CanAccessDocument(doc)) continue;
            var resolvedPath = ResolveExistingDocumentPath(doc);
            if (resolvedPath == null) continue;
            if (!SearchExportService.IsSupportedForCombinedPdfExport(resolvedPath)) continue;
            mergeable.Add(doc);
        }

        if (mergeable.Count < 2)
        {
            var msg = ReportLocalizationService.GetString("MergeToPdfNeedTwo", _configStore);
            throw new MergeQueueUserMessageException(msg, isWarning: true);
        }

        foreach (var d in mergeable)
        {
            var resolved = ResolveExistingDocumentPath(d);
            if (resolved != null)
                d.FilePath = resolved;
        }

        var cannotDelete = mergeable.Where(d => !_permissionService.CanDeleteDocument(d)).ToList();
        if (cannotDelete.Count > 0)
        {
            var msg = ReportLocalizationService.GetString("MergeToPdfCannotDeleteSources", _configStore);
            throw new MergeQueueUserMessageException(msg, isWarning: true);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"WorkAudit_Merge_{Guid.NewGuid():N}.pdf");
        try
        {
            var dbPathsForMergedSources = mergeable
                .Select(d => d.FilePath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            await Task.Run(() => _exportService.ExportToPdf(mergeable, tempPath,
                new ExportCombinedPdfOptions { AllowLossyPdfFallback = job.AllowLossyPdfFallback })).ConfigureAwait(false);

            var meta = PickMergeMetadataTemplate(mergeable);
            var baseDir = !string.IsNullOrEmpty(_appConfig.BaseDirectory) ? _appConfig.BaseDirectory : Defaults.GetDefaultBaseDir();
            var importOptions = new ImportOptions
            {
                BaseDirectory = baseDir,
                CopyToBaseDir = true,
                Branch = meta.Branch ?? Branches.ToConcreteBranchOrDefault(_sessionService.CurrentUser?.Branch),
                Section = meta.Section,
                SkipDuplicates = true,
                UseExistingDocumentOnDuplicateHash = true
            };
            if (DateTime.TryParse(meta.ExtractedDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var docDate))
                importOptions.DocumentDate = docDate;

            var imported = await _importService.ImportSinglePdfDocumentAsync(tempPath, importOptions, meta).ConfigureAwait(false);
            if (imported == null)
            {
                var msg = ReportLocalizationService.GetString("MergeToPdfImportFailed", _configStore);
                throw new MergeQueueUserMessageException(msg, isWarning: true);
            }

            await DeleteDocumentsAfterMergeAsync(mergeable, dbPathsForMergedSources).ConfigureAwait(false);
            return imported.Id;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Could not delete temp merge file {Path}", tempPath);
            }
        }
    }

    private string? ResolveExistingDocumentPath(Document? doc)
    {
        if (doc == null) return null;
        var baseDir = !string.IsNullOrEmpty(_appConfig.BaseDirectory) ? _appConfig.BaseDirectory : Defaults.GetDefaultBaseDir();
        return DocumentFilePathResolver.ResolveExistingPath(doc, baseDir);
    }

    private static Document PickMergeMetadataTemplate(IReadOnlyList<Document> mergeable)
    {
        foreach (var d in mergeable)
        {
            var t = d.DocumentType?.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            if (DocumentTypeInfo.IsUnclassified(t)) continue;
            return d;
        }
        return mergeable[0];
    }

    private async Task DeleteDocumentsAfterMergeAsync(IReadOnlyList<Document> documents, IReadOnlyList<string> filePathsFromDbBeforeExportMutation)
    {
        foreach (var doc in documents)
        {
            try
            {
                await _auditTrail.LogDocumentActionAsync(AuditAction.DocumentDeleted, doc, "Removed after PDF merge").ConfigureAwait(false);
                if (!_store.Delete(doc.Id))
                    _log.Warning("Delete after merge failed for document {Id}", doc.Id);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to delete source document {Id} after merge", doc.Id);
            }
        }

        foreach (var path in filePathsFromDbBeforeExportMutation)
        {
            try
            {
                TrySecureDeleteMergedSourceFileIfUnreferenced(path);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to delete file on disk after merge: {Path}", path);
            }
        }
    }

    private void TrySecureDeleteMergedSourceFileIfUnreferenced(string? filePathFromDb)
    {
        if (string.IsNullOrEmpty(filePathFromDb)) return;
        if (_store.CountDocumentsWithFilePath(filePathFromDb) > 0) return;

        if (File.Exists(filePathFromDb))
        {
            _secureDelete.SecureDelete(filePathFromDb);
            return;
        }

        if (!Path.IsPathRooted(filePathFromDb))
        {
            var baseDir = !string.IsNullOrEmpty(_appConfig.BaseDirectory) ? _appConfig.BaseDirectory : Defaults.GetDefaultBaseDir();
            try
            {
                var combined = Path.GetFullPath(Path.Combine(baseDir, filePathFromDb));
                if (File.Exists(combined))
                    _secureDelete.SecureDelete(combined);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Could not resolve path for secure delete: {Path}", filePathFromDb);
            }
        }
    }

    private readonly record struct MergeJob(int[] Ids, int? NextSelectionDocumentId, bool AllowLossyPdfFallback, DateTime EnqueuedAtUtc);

    private sealed class MergeQueueUserMessageException : Exception
    {
        public bool IsWarning { get; }
        public MergeQueueUserMessageException(string message, bool isWarning = true) : base(message) => IsWarning = isWarning;
    }
}
