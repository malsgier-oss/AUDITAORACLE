using Serilog;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Notes;

public interface INoteDocumentStatusSync
{
    Task OnNoteAddedAsync(Note note);
    Task OnNoteStatusChangedAsync(Note note, string previousStatus);
}

/// <summary>
/// Synchronizes document workflow status from Issue-note lifecycle events.
/// </summary>
public sealed class NoteDocumentStatusSync : INoteDocumentStatusSync
{
    private readonly ILogger _log = LoggingService.ForContext<NoteDocumentStatusSync>();
    private readonly IDocumentStore _documentStore;
    private readonly INotesStore _notesStore;
    private readonly IAuditTrailService _auditTrail;
    private readonly IChangeHistoryService _changeHistory;

    public NoteDocumentStatusSync(
        IDocumentStore documentStore,
        INotesStore notesStore,
        IAuditTrailService auditTrail,
        IChangeHistoryService changeHistory)
    {
        _documentStore = documentStore;
        _notesStore = notesStore;
        _auditTrail = auditTrail;
        _changeHistory = changeHistory;
    }

    public async Task OnNoteAddedAsync(Note note)
    {
        if (!IsIssueOpenForDocument(note))
            return;

        await ApplyDocumentStatusAsync(note.DocumentId, Enums.Status.Issue, note.Id).ConfigureAwait(false);
    }

    public async Task OnNoteStatusChangedAsync(Note note, string previousStatus)
    {
        if (!IsIssueNoteForDocument(note))
            return;
        if (string.Equals(previousStatus, note.Status, StringComparison.Ordinal))
            return;

        // Reopen is intentionally unsupported: resolved Issue notes are immutable.
        if (string.Equals(note.Status, NoteStatus.Resolved, StringComparison.Ordinal)
            && !string.Equals(previousStatus, NoteStatus.Resolved, StringComparison.Ordinal)
            && NoOtherOpenIssueNotes(note))
        {
            await ApplyDocumentStatusAsync(note.DocumentId, Enums.Status.Reviewed, note.Id).ConfigureAwait(false);
        }
    }

    private async Task ApplyDocumentStatusAsync(int documentId, string targetStatus, int triggeringNoteId)
    {
        try
        {
            var doc = _documentStore.Get(documentId);
            if (doc == null)
                return;
            if (string.Equals(doc.Status, Enums.Status.Archived, StringComparison.Ordinal))
                return;
            if (string.Equals(doc.Status, targetStatus, StringComparison.Ordinal))
                return;

            var oldStatus = doc.Status;
            if (!_documentStore.UpdateStatus(doc.Id, targetStatus))
                return;

            doc.Status = targetStatus;
            _changeHistory.RecordFieldChange(doc.Uuid, doc.Id, "status", oldStatus, targetStatus);
            await _auditTrail.LogDocumentActionAsync(
                AuditAction.DocumentStatusChanged,
                doc,
                details: $"Auto-set by note #{triggeringNoteId}",
                oldValue: oldStatus,
                newValue: targetStatus).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed syncing document status from note {NoteId}", triggeringNoteId);
        }
    }

    private bool NoOtherOpenIssueNotes(Note note)
    {
        var notes = _notesStore.GetByDocumentId(note.DocumentId);
        return notes.All(n =>
            n.Id == note.Id
            || !string.Equals(n.Type, NoteType.Issue, StringComparison.Ordinal)
            || (n.Status != NoteStatus.Open && n.Status != NoteStatus.InProgress));
    }

    private static bool IsIssueOpenForDocument(Note note) =>
        note.DocumentId > 0
        && string.Equals(note.Type, NoteType.Issue, StringComparison.Ordinal)
        && string.Equals(note.Status, NoteStatus.Open, StringComparison.Ordinal);

    private static bool IsIssueNoteForDocument(Note note) =>
        note.DocumentId > 0
        && string.Equals(note.Type, NoteType.Issue, StringComparison.Ordinal);
}
