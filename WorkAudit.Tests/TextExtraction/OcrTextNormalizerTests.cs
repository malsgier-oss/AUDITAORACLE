using FluentAssertions;
using WorkAudit.Core.TextExtraction;
using Xunit;

namespace WorkAudit.Tests.TextExtraction;

public class OcrTextNormalizerTests
{
    [Fact]
    public void Normalize_Empty_ReturnsEmpty()
    {
        OcrTextNormalizer.Normalize(null).Should().BeEmpty();
        OcrTextNormalizer.Normalize("").Should().BeEmpty();
        OcrTextNormalizer.Normalize("   ").Should().BeEmpty();
    }

    [Fact]
    public void Normalize_OcrError_PassthroughTrimmed()
    {
        var s = "OCR Error: something failed\n";
        OcrTextNormalizer.Normalize(s).Should().Be("OCR Error: something failed");
        OcrTextNormalizer.Normalize("Error: Tesseract data missing.").Should().Be("Error: Tesseract data missing.");
    }

    [Fact]
    public void Normalize_CollapsesSpacesAndBlankLines()
    {
        OcrTextNormalizer.Normalize("a    b  c").Should().Be("a b c");
        OcrTextNormalizer.Normalize("line1\n\n\n\nline2").Should().Be("line1\n\nline2");
    }

    [Fact]
    public void Normalize_StripsControlsAndZeroWidth()
    {
        var s = "a\u200Bb\uFEFFc\u0001d";
        OcrTextNormalizer.Normalize(s).Should().Be("abcd");
    }

    [Fact]
    public void Normalize_HyphenLineBreak_MergesBeforeLetter()
    {
        OcrTextNormalizer.Normalize("sub-\nnet", mergeHyphenLineBreaks: true).Should().Be("subnet");
        OcrTextNormalizer.Normalize("sub-\nnet", mergeHyphenLineBreaks: false).Should().Be("sub-\nnet");
    }

    [Fact]
    public void IsOcrErrorMessage_DetectsPrefixes()
    {
        OcrTextNormalizer.IsOcrErrorMessage("OCR Error: x").Should().BeTrue();
        OcrTextNormalizer.IsOcrErrorMessage("Error: Tesseract data missing.").Should().BeTrue();
        OcrTextNormalizer.IsOcrErrorMessage("hello").Should().BeFalse();
    }
}
