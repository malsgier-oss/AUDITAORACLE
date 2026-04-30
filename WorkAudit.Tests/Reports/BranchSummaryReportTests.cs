using FluentAssertions;
using Moq;
using WorkAudit.Core.Reports;
using WorkAudit.Domain;
using WorkAudit.Storage;
using Xunit;

namespace WorkAudit.Tests.Reports;

/// <summary>
/// Phase 2b parity guard: <see cref="BranchSummaryReport.GetData"/> must push the branch filter
/// into the SQL query. Pre-fix, the call ignored <c>branch</c> and post-filtered after grouping
/// — combined with the 50k row cap that meant the requested branch could disappear entirely.
/// </summary>
public class BranchSummaryReportTests
{
    [Fact]
    public void GetData_ForwardsBranchFilterToDocumentStore()
    {
        var store = new Mock<IDocumentStore>();
        store.Setup(s => s.ListDocuments(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns(new List<Document>
            {
                new() { Id = 1, Branch = "Main Branch", Status = Enums.Status.Cleared },
                new() { Id = 2, Branch = "Main Branch", Status = Enums.Status.Issue },
                new() { Id = 3, Branch = "Misrata Branch", Status = Enums.Status.Cleared }
            });

        var rows = BranchSummaryReport.GetData(store.Object, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            section: null, status: null, engagement: null, branch: "Main Branch");

        rows.Should().NotBeEmpty();
        store.Verify(s => s.ListDocuments(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void GetData_RequestsNewestFirstOrdering()
    {
        // Phase 1b: report generators must opt into newest-first ordering so the row cap drops
        // OLDEST rows, not newest. Otherwise recent Issue documents disappear from the report.
        var store = new Mock<IDocumentStore>();
        store.Setup(s => s.ListDocuments(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns(new List<Document>());

        BranchSummaryReport.GetData(store.Object, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        store.Verify(s => s.ListDocuments(
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), true /* newestFirst */),
            Times.AtLeastOnce,
            "BranchSummaryReport must request newest-first ordering");
    }
}
