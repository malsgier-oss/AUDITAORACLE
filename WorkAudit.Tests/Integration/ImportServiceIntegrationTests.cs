using System.IO;
using FluentAssertions;
using Moq;
using WorkAudit.Core.Import;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Core.TextExtraction;
using WorkAudit.Domain;
using WorkAudit.Storage;
using WorkAudit.Tests.Fixtures;
using Xunit;

namespace WorkAudit.Tests.Integration;

public class ImportServiceIntegrationTests : IClassFixture<OracleTestFixture>, IDisposable
{
    private readonly OracleTestFixture _fx;
    private readonly string _baseDir;
    private readonly DocumentStore _store;
    private readonly ImportService _import;
    private readonly List<string> _tempFiles = new();

    public ImportServiceIntegrationTests(OracleTestFixture f)
    {
        _fx = f;
        _baseDir = Path.Combine(Path.GetTempPath(), $"WorkAudit_base_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_baseDir);
        _store = _fx.DocumentStore;

        var audit = new Mock<IAuditTrailService>();
        audit.Setup(a => a.LogDocumentActionAsync(It.IsAny<string>(), It.IsAny<Document>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        var rename = new Mock<IFileRenameService>();
        var appCfg = new AppConfiguration { BaseDirectory = _baseDir, OracleConnectionString = _fx.ConnectionString ?? "" };
        var config = new Mock<IConfigStore>();
        var ocr = new Mock<IOcrService>();

        _import = new ImportService(_store, audit.Object, rename.Object, appCfg, config.Object, ocr.Object);
    }

    private string CreateMinimalPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"WorkAudit_png_{Guid.NewGuid():N}.png");
        var minimalPngHeader = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        };
        File.WriteAllBytes(path, minimalPngHeader);
        _tempFiles.Add(path);
        return path;
    }

    private static ImportOptions BuildOptions(string baseDir) => new()
    {
        Branch = Branches.Default,
        Section = Enums.Section.Individuals,
        BaseDirectory = baseDir,
        CopyToBaseDir = true,
        SkipDuplicates = true,
        DocumentDate = DateTime.UtcNow.Date
    };

    [SkippableFact]
    public async Task ImportFilesAsync_DuplicatePathsInBatch_ImportsOnce()
    {
        Skip.IfNot(_fx.IsAvailable);
        var path = CreateMinimalPng();
        var options = BuildOptions(_baseDir);

        var result = await _import.ImportFilesAsync(new[] { path, path, path }, options);

        result.SuccessCount.Should().Be(1);
        result.SkippedCount.Should().Be(0);
        result.ImportedDocuments.Should().HaveCount(1);
    }

    [SkippableFact]
    public async Task ImportFilesAsync_SameBytesDifferentPaths_SkipsAfterFirstWhenSkipDuplicates()
    {
        Skip.IfNot(_fx.IsAvailable);
        var dir = Path.Combine(Path.GetTempPath(), $"WorkAudit_dup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var p1 = Path.Combine(dir, "a.png");
        var p2 = Path.Combine(dir, "b.png");
        File.WriteAllBytes(p1, bytes);
        File.WriteAllBytes(p2, bytes);
        try
        {
            var options = BuildOptions(_baseDir);
            var result = await _import.ImportFilesAsync(new[] { p1, p2 }, options);

            result.SuccessCount.Should().Be(1);
            result.SkippedCount.Should().Be(1);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try
            {
                if (File.Exists(f)) File.Delete(f);
            }
            catch
            {
                // ignore
            }
        }

        try
        {
            if (Directory.Exists(_baseDir))
                Directory.Delete(_baseDir, recursive: true);
        }
        catch
        {
            // ignore
        }

    }
}
