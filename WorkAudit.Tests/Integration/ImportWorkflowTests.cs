using System.IO;
using FluentAssertions;
using Xunit;

namespace WorkAudit.Tests.Integration;

public class ImportWorkflowTests
{
    [Fact]
    public void CreateTestImage_ShouldCreateValidPath()
    {
        var path = CreateTestImage();
        path.Should().NotBeNullOrEmpty();
        File.Exists(path).Should().BeTrue();
        try { File.Delete(path); } catch { }
    }

    private static string CreateTestImage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"WorkAudit_test_{Guid.NewGuid():N}.png");
        var minimalPngHeader = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        };
        File.WriteAllBytes(path, minimalPngHeader);
        return path;
    }
}
