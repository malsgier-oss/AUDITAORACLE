using FluentAssertions;
using Moq;
using WorkAudit.Core.Notes;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage;
using Xunit;

namespace WorkAudit.Tests.Notes;

public class NoteDocumentStatusSyncTests
{
    private readonly Mock<IDocumentStore> _documentStore = new();
    private readonly Mock<INotesStore> _notesStore = new();
    private readonly Mock<IAuditTrailService> _auditTrail = new();
    private readonly Mock<IChangeHistoryService> _changeHistory = new();

    private NoteDocumentStatusSync CreateSut()
    {
        _auditTrail
            .Setup(a => a.LogDocumentActionAsync(It.IsAny<string>(), It.IsAny<Document>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        return new NoteDocumentStatusSync(
            _documentStore.Object,
            _notesStore.Object,
            _auditTrail.Object,
            _changeHistory.Object);
    }

    [Fact]
    public async Task OnNoteAddedAsync_issueOpen_setsDocumentToIssue()
    {
        var sut = CreateSut();
        var doc = new Document { Id = 10, Uuid = "doc-1", Status = Enums.Status.Reviewed };
        _documentStore.Setup(s => s.Get(10)).Returns(doc);
        _documentStore.Setup(s => s.UpdateStatus(10, Enums.Status.Issue)).Returns(true);

        var note = new Note { Id = 1, DocumentId = 10, Type = NoteType.Issue, Status = NoteStatus.Open };
        await sut.OnNoteAddedAsync(note);

        _documentStore.Verify(s => s.UpdateStatus(10, Enums.Status.Issue), Times.Once);
        _changeHistory.Verify(c => c.RecordFieldChange("doc-1", 10, "status", Enums.Status.Reviewed, Enums.Status.Issue), Times.Once);
        _auditTrail.Verify(a => a.LogDocumentActionAsync(
            AuditAction.DocumentStatusChanged,
            It.Is<Document>(d => d.Id == 10 && d.Status == Enums.Status.Issue),
            It.Is<string?>(v => v != null && v.Contains("#1")),
            Enums.Status.Reviewed,
            Enums.Status.Issue), Times.Once);
    }

    [Fact]
    public async Task OnNoteAddedAsync_nonIssue_doesNothing()
    {
        var sut = CreateSut();
        var note = new Note { Id = 2, DocumentId = 10, Type = NoteType.Observation, Status = NoteStatus.Open };

        await sut.OnNoteAddedAsync(note);

        _documentStore.Verify(s => s.UpdateStatus(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OnNoteAddedAsync_archivedDoc_doesNothing()
    {
        var sut = CreateSut();
        _documentStore.Setup(s => s.Get(10)).Returns(new Document { Id = 10, Uuid = "doc-1", Status = Enums.Status.Archived });

        var note = new Note { Id = 3, DocumentId = 10, Type = NoteType.Issue, Status = NoteStatus.Open };
        await sut.OnNoteAddedAsync(note);

        _documentStore.Verify(s => s.UpdateStatus(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OnNoteStatusChangedAsync_issueResolved_withoutOtherOpenIssues_setsReviewed()
    {
        var sut = CreateSut();
        var doc = new Document { Id = 10, Uuid = "doc-1", Status = Enums.Status.Issue };
        _documentStore.Setup(s => s.Get(10)).Returns(doc);
        _documentStore.Setup(s => s.UpdateStatus(10, Enums.Status.Reviewed)).Returns(true);
        _notesStore.Setup(s => s.GetByDocumentId(10)).Returns(new List<Note>
        {
            new() { Id = 100, DocumentId = 10, Type = NoteType.Issue, Status = NoteStatus.Resolved }
        });

        var note = new Note { Id = 100, DocumentId = 10, Type = NoteType.Issue, Status = NoteStatus.Resolved };
        await sut.OnNoteStatusChangedAsync(note, NoteStatus.Open);

        _documentStore.Verify(s => s.UpdateStatus(10, Enums.Status.Reviewed), Times.Once);
    }

    [Fact]
    public async Task OnNoteStatusChangedAsync_issueResolved_withAnotherOpenIssue_doesNotSetReviewed()
    {
        var sut = CreateSut();
        _notesStore.Setup(s => s.GetByDocumentId(10)).Returns(new List<Note>
        {
            new() { Id = 100, DocumentId = 10, Type = NoteType.Issue, Status = NoteStatus.Resolved },
            new() { Id = 101, DocumentId = 10, Type = NoteType.Issue, Status = NoteStatus.Open }
        });

        var note = new Note { Id = 100, DocumentId = 10, Type = NoteType.Issue, Status = NoteStatus.Resolved };
        await sut.OnNoteStatusChangedAsync(note, NoteStatus.Open);

        _documentStore.Verify(s => s.UpdateStatus(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OnNoteStatusChangedAsync_reopenCase_isIgnored()
    {
        var sut = CreateSut();
        var note = new Note { Id = 1, DocumentId = 10, Type = NoteType.Issue, Status = NoteStatus.Open };

        await sut.OnNoteStatusChangedAsync(note, NoteStatus.Resolved);

        _documentStore.Verify(s => s.UpdateStatus(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }
}
