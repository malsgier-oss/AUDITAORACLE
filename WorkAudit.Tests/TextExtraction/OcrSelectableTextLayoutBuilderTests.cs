using FluentAssertions;
using WorkAudit.Core.TextExtraction;
using Xunit;

namespace WorkAudit.Tests.TextExtraction;

public class OcrSelectableTextLayoutBuilderTests
{
    [Fact]
    public void BuildFromWordTokens_GroupsByAdaptiveThreshold_BuildsFullTextAndSpans()
    {
        var words = new[]
        {
            new OcrWordToken("Hello", 10, 10, 30, 12),
            new OcrWordToken("world", 45, 11, 40, 12),
            new OcrWordToken("Line2", 12, 40, 35, 12)
        };

        var layout = OcrSelectableTextLayoutBuilder.BuildFromWordTokens(words);
        layout.Spans.Should().HaveCount(3);
        layout.FullText.Should().Be("Hello world\nLine2");
        layout.Spans[0].CharStart.Should().Be(0);
        layout.Spans[0].CharLength.Should().Be(5);
        layout.Spans[1].LineIndex.Should().Be(0);
        layout.Spans[2].LineIndex.Should().Be(1);
    }

    [Fact]
    public void GetTextForSpanRange_Inclusive_ReturnsSubstring()
    {
        var words = new[]
        {
            new OcrWordToken("a", 0, 0, 5, 10),
            new OcrWordToken("b", 10, 0, 5, 10),
            new OcrWordToken("c", 0, 20, 5, 10)
        };
        var layout = OcrSelectableTextLayoutBuilder.BuildFromWordTokens(words);
        layout.GetTextForSpanRange(1, 2).Should().Be("b\nc");
    }

    [Fact]
    public void OcrCopyFormatter_RemovesSpaceBeforePeriod()
    {
        OcrCopyFormatter.FormatCopy("hello .").Should().Be("hello.");
    }

    [Fact]
    public void BuildFromPreviewOcrRegions_SingleLinePerRegion()
    {
        var regions = new[]
        {
            new PreviewOcrRegion(0, 0, 100, 20, "One"),
            new PreviewOcrRegion(0, 25, 100, 20, "Two")
        };
        var layout = OcrSelectableTextLayoutBuilder.BuildFromPreviewOcrRegions(regions);
        layout.Should().NotBeNull();
        layout!.FullText.Should().Be("One\nTwo");
    }
}
