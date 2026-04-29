using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Storage;
using Xunit;

namespace WorkAudit.Tests.Backup;

[Collection("BackupTests")]
public class BackupServiceTests : IDisposable
{
    private readonly Mock<IExportEncryptionService> _encryptionMock;
    private readonly AppConfiguration _appConfig;
    private readonly BackupService _service;
    private readonly string _testDir;
    private readonly string _testDbPath;

    public BackupServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"WorkAudit_test_backup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        _testDbPath = Path.Combine(_testDir, "test.db");
        File.WriteAllText(_testDbPath, "Test database content");

        _appConfig = new AppConfiguration
        {
            OracleConnectionString = _testDbPath,
            BaseDirectory = _testDir
        };

        _encryptionMock = new Mock<IExportEncryptionService>();
        _encryptionMock.Setup(x => x.EncryptFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((input, output, _) =>
            {
                File.Copy(input, output, true);
            });
        _encryptionMock.Setup(x => x.DecryptFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((input, output, _) =>
            {
                File.Copy(input, output, true);
            });

        _service = new BackupService(_appConfig, _encryptionMock.Object);
    }

    [Fact]
    public async Task CreateBackupAsync_WithoutDocuments_ShouldCreateDatabaseBackup()
    {
        var backupPath = Path.Combine(_testDir, "backup.zip");

        var result = await _service.CreateBackupAsync(backupPath, includeDocuments: false);

        result.Success.Should().BeTrue();
        File.Exists(backupPath).Should().BeTrue();
        result.BackupPath.Should().Be(backupPath);
    }

    [Fact]
    public async Task CreateBackupAsync_WithDocuments_ShouldIncludeDocumentsFolder()
    {
        var docsDir = Path.Combine(_testDir, "docs");
        Directory.CreateDirectory(docsDir);
        File.WriteAllText(Path.Combine(docsDir, "test.txt"), "Test document");

        var backupPath = Path.Combine(_testDir, "backup_with_docs.zip");

        var result = await _service.CreateBackupAsync(backupPath, includeDocuments: true);

        result.Success.Should().BeTrue();
        File.Exists(backupPath).Should().BeTrue();
    }

    [Fact]
    public async Task CreateBackupAsync_WithEncryption_ShouldEncryptBackup()
    {
        var backupPath = Path.Combine(_testDir, "encrypted_backup.zip");
        var password = "test-password";

        var result = await _service.CreateBackupAsync(backupPath, includeDocuments: false, encryptionPassword: password);

        result.Success.Should().BeTrue();
        _encryptionMock.Verify(x => x.EncryptFile(It.IsAny<string>(), It.IsAny<string>(), password), Times.Once);
    }

    [Fact]
    public async Task VerifyBackupAsync_ValidBackup_ShouldReturnSuccess()
    {
        var backupPath = Path.Combine(_testDir, "verify_backup.zip");
        await _service.CreateBackupAsync(backupPath, includeDocuments: false);

        var result = await _service.VerifyBackupAsync(backupPath);

        result.Valid.Should().BeTrue();
        result.Error.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task VerifyBackupAsync_NonExistentFile_ShouldReturnError()
    {
        var result = await _service.VerifyBackupAsync("nonexistent.zip");

        result.Valid.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task GetBackupHistory_ShouldReturnList()
    {
        await _service.CreateBackupAsync(null, includeDocuments: false);
        await Task.Delay(150);
        await _service.CreateBackupAsync(null, includeDocuments: false);

        var history = _service.GetBackupHistory();

        history.Should().NotBeEmpty();
        history.Count.Should().BeGreaterThanOrEqualTo(1); // At least one backup
    }

    [Fact]
    public async Task CleanupOldBackupsAsync_ShouldKeepSpecifiedCount()
    {
        for (int i = 0; i < 5; i++)
        {
            await _service.CreateBackupAsync(null, includeDocuments: false);
            await Task.Delay(50);
        }

        await _service.CleanupOldBackupsAsync(keepCount: 3);

        var history = _service.GetBackupHistory();
        history.Count.Should().BeLessOrEqualTo(3);
    }

    [Fact]
    public async Task CreateBackupAsync_DefaultPath_ShouldUseDefaultDirectory()
    {
        var result = await _service.CreateBackupAsync(destinationPath: null, includeDocuments: false);

        result.Success.Should().BeTrue();
        result.BackupPath.Should().NotBeNullOrEmpty();
        File.Exists(result.BackupPath).Should().BeTrue();
    }

    [Fact]
    public async Task CreateZipFromDirectoryResilientAsync_SkipsExclusivelyLockedFile_AndZipsOtherFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"WorkAudit_zip_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var okPath = Path.Combine(dir, "ok.txt");
        var lockedPath = Path.Combine(dir, "locked.txt");
        File.WriteAllText(okPath, "ok");
        File.WriteAllText(lockedPath, "locked");

        var zipPath = Path.Combine(Path.GetTempPath(), $"WorkAudit_zip_out_{Guid.NewGuid():N}.zip");
        var skipped = new List<string>();

        try
        {
            using (new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                await _service.CreateZipFromDirectoryResilientAsync(dir, zipPath, skipped);
            }

            skipped.Should().NotBeEmpty();
            skipped.Should().Contain(s => s.Contains("locked.txt", StringComparison.OrdinalIgnoreCase));
            File.Exists(zipPath).Should().BeTrue();

            using var archive = ZipFile.OpenRead(zipPath);
            var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
            names.Should().Contain("ok.txt");
            names.Should().NotContain(e => e.Contains("locked.txt", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignore cleanup errors
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);

            var appDataBackup = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WORKAUDIT", "Backups");
            if (Directory.Exists(appDataBackup))
            {
                foreach (var file in Directory.GetFiles(appDataBackup, "WorkAudit_Backup_*.zip"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
