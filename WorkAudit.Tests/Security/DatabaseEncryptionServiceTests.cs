using System;
using System.IO;
using System.Text;
using FluentAssertions;
using WorkAudit.Core.Security;
using Xunit;

namespace WorkAudit.Tests.Security;

public class DatabaseEncryptionServiceTests : IDisposable
{
    private readonly DatabaseEncryptionService _service;
    private readonly string _testDir;

    public DatabaseEncryptionServiceTests()
    {
        _service = new DatabaseEncryptionService();
        _testDir = Path.Combine(Path.GetTempPath(), $"WorkAudit_test_dbenc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public void EncryptDatabaseFile_ValidFile_ShouldCreateEncryptedFile()
    {
        var dbPath = Path.Combine(_testDir, "test.db");
        File.WriteAllText(dbPath, "Sample database content");

        _service.EncryptDatabaseFile(dbPath);

        var encryptedPath = dbPath + ".encrypted";
        File.Exists(encryptedPath).Should().BeTrue();
        _service.IsEncrypted(encryptedPath).Should().BeTrue();
    }

    [Fact]
    public void EncryptDatabaseFile_NonExistentFile_ShouldThrowException()
    {
        var dbPath = Path.Combine(_testDir, "nonexistent.db");

        Action act = () => _service.EncryptDatabaseFile(dbPath);

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void EncryptDatabaseFile_AlreadyEncrypted_ShouldNotReEncrypt()
    {
        var dbPath = Path.Combine(_testDir, "test.db");
        File.WriteAllText(dbPath, "Sample database content");

        _service.EncryptDatabaseFile(dbPath);
        var encryptedPath = dbPath + ".encrypted";
        var firstEncryptTime = File.GetLastWriteTimeUtc(encryptedPath);

        System.Threading.Thread.Sleep(100);
        _service.EncryptDatabaseFile(encryptedPath);

        var secondEncryptTime = File.GetLastWriteTimeUtc(encryptedPath);
        firstEncryptTime.Should().Be(secondEncryptTime);
    }

    [Fact]
    public void DecryptDatabaseFile_ValidEncryptedFile_ShouldRestoreOriginal()
    {
        var dbPath = Path.Combine(_testDir, "test.db");
        var originalContent = "Sample database content with special chars: !@#$%";
        File.WriteAllText(dbPath, originalContent);

        _service.EncryptDatabaseFile(dbPath);
        var encryptedPath = dbPath + ".encrypted";
        var decryptedPath = Path.Combine(_testDir, "decrypted.db");

        _service.DecryptDatabaseFile(encryptedPath, decryptedPath);

        File.Exists(decryptedPath).Should().BeTrue();
        var decryptedContent = File.ReadAllText(decryptedPath);
        decryptedContent.Should().Be(originalContent);
    }

    [Fact]
    public void DecryptDatabaseFile_NonExistentFile_ShouldThrowException()
    {
        var encryptedPath = Path.Combine(_testDir, "nonexistent.encrypted");
        var outputPath = Path.Combine(_testDir, "output.db");

        Action act = () => _service.DecryptDatabaseFile(encryptedPath, outputPath);

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void DecryptDatabaseFile_NonEncryptedFile_ShouldThrowException()
    {
        var plainPath = Path.Combine(_testDir, "plain.db");
        File.WriteAllText(plainPath, "Not encrypted");
        var outputPath = Path.Combine(_testDir, "output.db");

        Action act = () => _service.DecryptDatabaseFile(plainPath, outputPath);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not an encrypted WorkAudit database*");
    }

    [Fact]
    public void IsEncrypted_EncryptedFile_ShouldReturnTrue()
    {
        var dbPath = Path.Combine(_testDir, "test.db");
        File.WriteAllText(dbPath, "Sample database content");

        _service.EncryptDatabaseFile(dbPath);
        var encryptedPath = dbPath + ".encrypted";

        _service.IsEncrypted(encryptedPath).Should().BeTrue();
    }

    [Fact]
    public void IsEncrypted_PlainFile_ShouldReturnFalse()
    {
        var dbPath = Path.Combine(_testDir, "plain.db");
        File.WriteAllText(dbPath, "Plain text content");

        _service.IsEncrypted(dbPath).Should().BeFalse();
    }

    [Fact]
    public void IsEncrypted_NonExistentFile_ShouldReturnFalse()
    {
        var dbPath = Path.Combine(_testDir, "nonexistent.db");

        _service.IsEncrypted(dbPath).Should().BeFalse();
    }

    [Fact]
    public void IsEncrypted_EmptyFile_ShouldReturnFalse()
    {
        var dbPath = Path.Combine(_testDir, "empty.db");
        File.WriteAllText(dbPath, "");

        _service.IsEncrypted(dbPath).Should().BeFalse();
    }

    [Fact]
    public void EncryptDecrypt_LargeFile_ShouldWork()
    {
        var dbPath = Path.Combine(_testDir, "large.db");
        var largeContent = new string('A', 1024 * 100); // 100 KB
        File.WriteAllText(dbPath, largeContent);

        _service.EncryptDatabaseFile(dbPath);
        var encryptedPath = dbPath + ".encrypted";
        var decryptedPath = Path.Combine(_testDir, "decrypted_large.db");
        _service.DecryptDatabaseFile(encryptedPath, decryptedPath);

        var decryptedContent = File.ReadAllText(decryptedPath);
        decryptedContent.Should().Be(largeContent);
    }

    [Fact]
    public void EncryptDecrypt_BinaryContent_ShouldWork()
    {
        var dbPath = Path.Combine(_testDir, "binary.db");
        var binaryContent = new byte[256];
        for (int i = 0; i < 256; i++)
            binaryContent[i] = (byte)i;
        File.WriteAllBytes(dbPath, binaryContent);

        _service.EncryptDatabaseFile(dbPath);
        var encryptedPath = dbPath + ".encrypted";
        var decryptedPath = Path.Combine(_testDir, "decrypted_binary.db");
        _service.DecryptDatabaseFile(encryptedPath, decryptedPath);

        var decryptedContent = File.ReadAllBytes(decryptedPath);
        decryptedContent.Should().Equal(binaryContent);
    }

    [Fact]
    public void Encrypt_SameFileTwice_ShouldProduceDifferentCiphertext()
    {
        var dbPath1 = Path.Combine(_testDir, "test1.db");
        var dbPath2 = Path.Combine(_testDir, "test2.db");
        var content = "Same content";
        File.WriteAllText(dbPath1, content);
        File.WriteAllText(dbPath2, content);

        _service.EncryptDatabaseFile(dbPath1);
        _service.EncryptDatabaseFile(dbPath2);

        var encrypted1 = File.ReadAllBytes(dbPath1 + ".encrypted");
        var encrypted2 = File.ReadAllBytes(dbPath2 + ".encrypted");

        encrypted1.Should().NotEqual(encrypted2);
    }

    [Fact]
    public void EncryptedFile_ShouldHaveCorrectHeader()
    {
        var dbPath = Path.Combine(_testDir, "test.db");
        File.WriteAllText(dbPath, "Test content");

        _service.EncryptDatabaseFile(dbPath);
        var encryptedPath = dbPath + ".encrypted";

        var header = new byte[4];
        using (var fs = File.OpenRead(encryptedPath))
        {
            fs.Read(header, 0, 4);
        }

        header.Should().Equal(new byte[] { 0x57, 0x41, 0x44, 0x42 }); // "WADB"
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
