using FluentAssertions;
using WorkAudit.Core.TextExtraction;
using Xunit;

namespace WorkAudit.Tests.TextExtraction;

public class OcrStructuredDataExtractorTests
{
    [Fact]
    public void Extract_ParsesKeyFields_FromBilingualText()
    {
        var text = """
                   Account Name: John Doe
                   Account No: 1234 5678 9012
                   Transaction Ref: TRX-5566
                   Amount: 12,450.75
                   Date: 2026-03-19
                   """;

        var result = OcrStructuredDataExtractor.Extract(text);
        result.AccountName.Should().Be("John Doe");
        result.AccountNumber.Should().Be("123456789012");
        result.TransactionReference.Should().Be("TRX-5566");
        result.Amounts.Should().Be("12450.75");
        result.ExtractedDate.Should().Be("2026-03-19");
        result.AccountNameConfidence.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Extract_NoMatches_ReturnsEmptyResult()
    {
        var result = OcrStructuredDataExtractor.Extract("random text without fields");
        result.AccountName.Should().BeNull();
        result.AccountNumber.Should().BeNull();
        result.TransactionReference.Should().BeNull();
        result.Amounts.Should().BeNull();
        result.ExtractedDate.Should().BeNull();
    }
}
