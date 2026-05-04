using FluentAssertions;
using Moq;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;
using Xunit;

namespace WorkAudit.Tests.Core;

public sealed class WorkflowMonitorTests
{
    private static Mock<IDocumentStore> CreateDocStoreMock()
    {
        var doc = new Mock<IDocumentStore>(MockBehavior.Loose);
        doc.Setup(x => x.ListDocuments(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns(new List<Document>());
        doc.Setup(d => d.CountDocumentsWithFilePath(It.IsAny<string>())).Returns(0);
        return doc;
    }

    [Fact]
    public void DetectIssues_AddsMergeQueueBacklog_WhenPendingAboveFive()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "wa_wf_merge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
        var doc = CreateDocStoreMock();
        var assign = new Mock<IDocumentAssignmentStore>(MockBehavior.Loose);
        assign.Setup(a => a.ListAll(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns(new List<DocumentAssignment>());
        var merge = new FixedMergeQueueState { PendingCount = 8, OldestPendingMergeEnqueueUtc = DateTime.UtcNow.AddMinutes(-10) };
        var cfg = new AppConfiguration { BaseDirectory = tmp };

        var sut = new WorkflowMonitor(doc.Object, assign.Object, merge);
        var issues = sut.DetectIssues(cfg);

        issues.Should().Contain(i => i.Type == "MergeQueueBacklog");
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void DetectIssues_AddsMergeQueueBacklog_WhenSingleJobOlderThanOneHour()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "wa_wf_merge2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
        var doc = CreateDocStoreMock();
        var assign = new Mock<IDocumentAssignmentStore>(MockBehavior.Loose);
        assign.Setup(a => a.ListAll(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns(new List<DocumentAssignment>());
        var merge = new FixedMergeQueueState { PendingCount = 1, OldestPendingMergeEnqueueUtc = DateTime.UtcNow.AddHours(-2) };
        var cfg = new AppConfiguration { BaseDirectory = tmp };

        var sut = new WorkflowMonitor(doc.Object, assign.Object, merge);
        var issues = sut.DetectIssues(cfg);

        issues.Should().Contain(i => i.Type == "MergeQueueBacklog");
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    private sealed class FixedMergeQueueState : IProcessingMergeQueueService
    {
        public int PendingCount { get; init; }
        public DateTime? OldestPendingMergeEnqueueUtc { get; init; }

        public void Enqueue(IReadOnlyList<int> orderedDocumentIds, int? nextSelectionDocumentId, bool allowLossyPdfFallback = true)
        {
        }

        public event EventHandler<ProcessingMergeCompletedEventArgs>? MergeCompleted
        {
            add { }
            remove { }
        }

        public event EventHandler<ProcessingMergeFailedEventArgs>? MergeFailed
        {
            add { }
            remove { }
        }
    }
}
