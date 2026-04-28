using FluentAssertions;
using WorkAudit.Domain;
using WorkAudit.Storage;
using WorkAudit.Tests.Fixtures;
using Xunit;

namespace WorkAudit.Tests.Storage;

public class MarkupStoreTests : IClassFixture<OracleTestFixture>
{
    private readonly OracleTestFixture _fx;
    private DocumentStore _documents => _fx.DocumentStore;
    private MarkupStore _markup => new(_fx.ConnectionString!);

    public MarkupStoreTests(OracleTestFixture f) => _fx = f;

    [SkippableFact]
    public void ReplaceForDocument_ThenGet_ShouldRoundTrip()
    {
        Skip.IfNot(_fx.IsAvailable);
        var docId = (int)_documents.Insert(new Document { FilePath = "a.png", Branch = Branches.Default, Section = "S" });
        var regions = new[]
        {
            new MarkupRegion
            {
                Kind = MarkupKind.Highlight,
                Color = "#80FFFF00",
                NormX = 0.1, NormY = 0.2, NormW = 0.3, NormH = 0.15,
                PageIndex = 0,
                PreviewSurface = MarkupPreviewSurface.Image
            }
        };

        _markup.ReplaceForDocument(docId, regions, "auditor1");

        var got = _markup.GetByDocumentId(docId);
        got.Should().HaveCount(1);
        got[0].Kind.Should().Be(MarkupKind.Highlight);
        got[0].NormX.Should().BeApproximately(0.1, 1e-9);
        got[0].CreatedBy.Should().Be("auditor1");
    }

    [SkippableFact]
    public void ReplaceForDocument_ShouldReplacePrevious()
    {
        Skip.IfNot(_fx.IsAvailable);
        var docId = (int)_documents.Insert(new Document { FilePath = "b.pdf", Branch = Branches.Default, Section = "S" });
        _markup.ReplaceForDocument(docId, new[] { new MarkupRegion { Kind = MarkupKind.Rectangle, Color = "#FFFF0000", NormX = 0, NormY = 0, NormW = 1, NormH = 1, PreviewSurface = MarkupPreviewSurface.Pdf, PageIndex = 2 } }, "u");
        _markup.ReplaceForDocument(docId, Array.Empty<MarkupRegion>(), "u");
        _markup.GetByDocumentId(docId).Should().BeEmpty();
    }

    [SkippableFact]
    public void ReplaceForDocument_MergedImageAndPdf_RoundTrip()
    {
        Skip.IfNot(_fx.IsAvailable);
        var docId = (int)_documents.Insert(new Document { FilePath = "c.png", Branch = Branches.Default, Section = "S" });
        var img = new MarkupRegion { Kind = MarkupKind.Highlight, Color = "#80FFFF00", NormX = 0.1, NormY = 0.1, NormW = 0.2, NormH = 0.2, PreviewSurface = MarkupPreviewSurface.Image };
        var pdf = new MarkupRegion { Kind = MarkupKind.Rectangle, Color = "#CCFF0000", NormX = 0, NormY = 0, NormW = 0.5, NormH = 0.5, PreviewSurface = MarkupPreviewSurface.Pdf, PageIndex = 0 };
        _markup.ReplaceForDocument(docId, new[] { img, pdf }, "u");
        var got = _markup.GetByDocumentId(docId);
        got.Should().HaveCount(2);
        got.Count(m => m.PreviewSurface == MarkupPreviewSurface.Image).Should().Be(1);
        got.Count(m => m.PreviewSurface == MarkupPreviewSurface.Pdf).Should().Be(1);
    }
}
