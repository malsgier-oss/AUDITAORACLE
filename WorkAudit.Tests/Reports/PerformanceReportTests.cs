using FluentAssertions;
using Moq;
using WorkAudit.Core.Reports;
using WorkAudit.Domain;
using WorkAudit.Storage;
using Xunit;

namespace WorkAudit.Tests.Reports;

/// <summary>
/// Guards the GetData methods used by PerformanceReport.GeneratePdf. The legacy bug compared
/// branch-rows against the section filter (and vice versa), wiping the whole table out when both
/// dimensions were supplied. The data layer must still aggregate correctly when filters scope
/// the query in SQL.
/// </summary>
public class PerformanceReportTests
{
    private static IDocumentStore MockStore(IEnumerable<Document> docs, out Mock<IDocumentStore> mock)
    {
        var m = new Mock<IDocumentStore>();
        m.Setup(s => s.ListDocuments(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns(docs.ToList());
        mock = m;
        return m.Object;
    }

    [Fact]
    public void GetDataByBranch_AggregatesByBranchAndExposesIssueCounts()
    {
        var docs = new[]
        {
            new Document { Branch = "Main Branch", Status = Enums.Status.Issue },
            new Document { Branch = "Main Branch", Status = Enums.Status.Cleared },
            new Document { Branch = "Misrata Branch", Status = Enums.Status.Issue },
            new Document { Branch = "Misrata Branch", Status = Enums.Status.Issue },
            new Document { Branch = "Misrata Branch", Status = Enums.Status.Cleared }
        };
        var store = MockStore(docs, out _);

        var rows = PerformanceReport.GetDataByBranch(store, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        rows.Should().HaveCount(2);
        rows.Single(r => r.Name == "Main Branch").Volume.Should().Be(2);
        rows.Single(r => r.Name == "Main Branch").Issue.Should().Be(1);
        rows.Single(r => r.Name == "Misrata Branch").Volume.Should().Be(3);
        rows.Single(r => r.Name == "Misrata Branch").Issue.Should().Be(2);
    }

    [Fact]
    public void GetDataByBranch_ForwardsBranchAndSectionFiltersToStore()
    {
        var docs = new[]
        {
            new Document { Branch = "Main Branch", Section = "Audit", Status = Enums.Status.Cleared }
        };
        var store = MockStore(docs, out var mock);

        PerformanceReport.GetDataByBranch(store, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
            branch: "Main Branch", section: "Audit");

        mock.Verify(s => s.ListDocuments(
                "Main Branch",          // branch
                "Audit",                // section
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                true /* newestFirst */),
            Times.Once);
    }

    [Fact]
    public void GetDataBySection_AggregatesBySection()
    {
        var docs = new[]
        {
            new Document { Section = "Audit", Status = Enums.Status.Cleared },
            new Document { Section = "Audit", Status = Enums.Status.Issue },
            new Document { Section = "Compliance", Status = Enums.Status.Cleared }
        };
        var store = MockStore(docs, out _);

        var rows = PerformanceReport.GetDataBySection(store, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        rows.Should().HaveCount(2);
        rows.Single(r => r.Name == "Audit").Volume.Should().Be(2);
        rows.Single(r => r.Name == "Compliance").Volume.Should().Be(1);
    }
}
