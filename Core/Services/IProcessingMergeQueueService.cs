using System;
using System.Collections.Generic;

namespace WorkAudit.Core.Services;

/// <summary>
/// Serialized background queue for Processing tab "Merge to PDF" so the UI stays responsive.
/// </summary>
public interface IProcessingMergeQueueService
{
    /// <summary>Jobs not yet finished (includes the one currently processing).</summary>
    int PendingCount { get; }

    /// <summary>
    /// Enqueues a merge job. <paramref name="orderedDocumentIds"/> must be in grid order and already validated (≥2 mergeable, deletable).
    /// <paramref name="nextSelectionDocumentId"/> is the document row below the merged block (for selection after merge); merged row is only tinted, not selected.
    /// <paramref name="allowLossyPdfFallback"/> when false, PDFs that cannot be imported losslessly cause merge failure instead of raster fallback (Processing preflight).
    /// </summary>
    void Enqueue(IReadOnlyList<int> orderedDocumentIds, int? nextSelectionDocumentId, bool allowLossyPdfFallback = true);

    /// <summary>Raised on a thread-pool thread after a successful merge; UI should dispatch to the UI thread.</summary>
    event EventHandler<ProcessingMergeCompletedEventArgs>? MergeCompleted;

    /// <summary>Raised when a job fails (import, export, validation, etc.).</summary>
    event EventHandler<ProcessingMergeFailedEventArgs>? MergeFailed;
}

public sealed class ProcessingMergeCompletedEventArgs : EventArgs
{
    public int ImportedDocumentId { get; }
    /// <summary>Row to select after merge (color highlight stays on <see cref="ImportedDocumentId"/>).</summary>
    public int? NextSelectionDocumentId { get; }

    public ProcessingMergeCompletedEventArgs(int importedDocumentId, int? nextSelectionDocumentId = null)
    {
        ImportedDocumentId = importedDocumentId;
        NextSelectionDocumentId = nextSelectionDocumentId;
    }
}

public sealed class ProcessingMergeFailedEventArgs : EventArgs
{
    public string Message { get; }
    public Exception? Exception { get; }
    public bool ShowAsWarning { get; init; }

    public ProcessingMergeFailedEventArgs(string message, Exception? exception = null)
    {
        Message = message;
        Exception = exception;
    }
}
