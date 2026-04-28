using FluentAssertions;
using WorkAudit.Core.TextExtraction;
using Xunit;

namespace WorkAudit.Tests.TextExtraction;

public class OcrArabicNormalizerTests
{
    [Fact]
    public void Normalize_JoinsSplitAlefLam_OnArabicLine()
    {
        var raw = "\u0627 \u0644\u0645\u0628\u0644\u063A"; // "ا ل" + مبلغ
        var s = OcrArabicNormalizer.Normalize(raw);
        s.Should().Contain("\u0627\u0644"); // ال
    }

    [Fact]
    public void Normalize_LeavesEnglishUntouched()
    {
        var s = OcrArabicNormalizer.Normalize("INV-123 Total 1,200.50");
        s.Should().Be("INV-123 Total 1,200.50");
    }
}
