using FluentAssertions;
using Moq;
using WorkAudit.Core.Reports;
using WorkAudit.Domain;
using WorkAudit.Storage;
using Xunit;

namespace WorkAudit.Tests.Reports;

/// <summary>
/// Phase 1c regression guard: <see cref="ReportDraftService.BuildInitialHtml"/> must NOT emit
/// the historical placeholder strings ("Finding 1", "Recommendation 1", "This is an editable
/// draft.") that used to ship in every new draft regardless of the underlying data.
/// </summary>
public class ReportDraftServiceTests
{
    private static readonly string[] BannedPlaceholders =
    {
        "Finding 1",
        "Finding 2",
        "Recommendation 1",
        "Recommendation 2",
        "This is an editable draft",
        "Add your summary here"
    };

    [Fact]
    public void BuildInitialHtml_NoStore_StillExcludesPlaceholderText()
    {
        var config = new ReportConfig
        {
            ReportType = ReportType.ExecutiveSummary,
            DateFrom = DateTime.UtcNow.AddDays(-7),
            DateTo = DateTime.UtcNow
        };

        var html = ReportDraftService.BuildInitialHtml(config, documentStore: null);

        foreach (var banned in BannedPlaceholders)
            html.Should().NotContain(banned, $"the draft must never seed the legacy placeholder '{banned}'");
    }

    [Fact]
    public void BuildInitialHtml_WithDocumentStore_IncludesRealKpiNumbers()
    {
        var docs = new[]
        {
            new Document { Id = 1, Branch = "Main Branch", Section = "Audit", Status = Enums.Status.Cleared },
            new Document { Id = 2, Branch = "Main Branch", Section = "Audit", Status = Enums.Status.Issue, FilePath = "/r/x.pdf" },
            new Document { Id = 3, Branch = "Misrata Branch", Section = "Compliance", Status = Enums.Status.Issue, FilePath = "/r/y.pdf" },
            new Document { Id = 4, Branch = "Misrata Branch", Section = "Compliance", Status = Enums.Status.Cleared },
            new Document { Id = 5, Branch = "Misrata Branch", Section = "Compliance", Status = Enums.Status.Reviewed }
        };
        var store = new Mock<IDocumentStore>();
        store.Setup(s => s.ListDocuments(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns(docs.ToList());

        var config = new ReportConfig
        {
            ReportType = ReportType.ExecutiveSummary,
            DateFrom = DateTime.UtcNow.AddDays(-7),
            DateTo = DateTime.UtcNow
        };

        var html = ReportDraftService.BuildInitialHtml(config, store.Object);

        foreach (var banned in BannedPlaceholders)
            html.Should().NotContain(banned);

        // Total documents: 5
        html.Should().Contain(">5<", "the snapshot must reflect the total documents pulled from the store");
        // Should reference the actual branches that came back from the mock.
        html.Should().Contain("Main Branch");
        html.Should().Contain("Misrata Branch");
        // Should expose the issue-document file names so reviewers see the real data, not boilerplate.
        html.Should().Contain("x.pdf");
        html.Should().Contain("y.pdf");
    }

    [Fact]
    public void BuildInitialHtml_WhenDocumentStoreThrows_FallsBackGracefully()
    {
        var store = new Mock<IDocumentStore>();
        store.Setup(s => s.ListDocuments(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>()))
            .Throws(new InvalidOperationException("database unavailable"));

        var config = new ReportConfig
        {
            ReportType = ReportType.ExecutiveSummary,
            DateFrom = DateTime.UtcNow.AddDays(-7),
            DateTo = DateTime.UtcNow
        };

        Action act = () => ReportDraftService.BuildInitialHtml(config, store.Object);
        act.Should().NotThrow();
    }
}
