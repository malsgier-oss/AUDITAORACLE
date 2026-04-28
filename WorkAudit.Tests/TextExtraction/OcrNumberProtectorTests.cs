using FluentAssertions;
using WorkAudit.Core.TextExtraction;
using Xunit;

namespace WorkAudit.Tests.TextExtraction;

public class OcrNumberProtectorTests
{
    [Fact]
    public void Repair_CollapsesHyphenBreakInId()
    {
        var s = OcrNumberProtector.Repair("INV - 12345");
        s.Should().Be("INV-12345");
    }

    [Fact]
    public void Repair_FixesDigitSeparatorSpaces()
    {
        var s = OcrNumberProtector.Repair("1 , 200 . 50");
        s.Should().Be("1,200.50");
    }
}
