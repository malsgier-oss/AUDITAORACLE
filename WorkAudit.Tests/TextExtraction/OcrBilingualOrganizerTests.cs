using FluentAssertions;
using WorkAudit.Core.TextExtraction;
using Xunit;

namespace WorkAudit.Tests.TextExtraction;

public class OcrBilingualOrganizerTests
{
    [Fact]
    public void ClassifyLine_ArabicAndEnglish_Works()
    {
        OcrBilingualOrganizer.ClassifyLine("اسم العميل أحمد").Should().Be(OcrLineScript.Arabic);
        OcrBilingualOrganizer.ClassifyLine("Account Name: John Doe").Should().Be(OcrLineScript.English);
        OcrBilingualOrganizer.ClassifyLine("12345 - REF A1").Should().Be(OcrLineScript.Mixed);
    }

    [Fact]
    public void Organize_CreatesLanguageSections()
    {
        var text = "Account Name: John Doe\nاسم الحساب: أحمد\nRef: TX-991\n";
        var organized = OcrBilingualOrganizer.Organize(text);
        organized.Should().Contain("Arabic:");
        organized.Should().Contain("English:");
        organized.Should().Contain("Mixed/Numbers:");
        organized.Should().Contain("اسم الحساب: أحمد");
        organized.Should().Contain("Account Name: John Doe");
    }
}
