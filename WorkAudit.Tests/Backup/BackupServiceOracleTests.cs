using System.IO.Compression;
using FluentAssertions;
using Moq;
using WorkAudit.Core.Backup;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Storage;
using Xunit;

namespace WorkAudit.Tests.Backup;

[Collection("BackupTests")]
public sealed class BackupServiceOracleTests : IDisposable
{
    private readonly string _testDir;

    public BackupServiceOracleTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"WorkAudit_oracle_backup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task CreateBackupAsync_WithOracle_MissingLocalFolder_ShouldFail()
    {
        var cfg = new Mock<IConfigStore>();
        cfg.Setup(c => c.GetSettingValue("oracle_datapump_directory", It.IsAny<string?>())).Returns("DATA_PUMP_DIR");
        cfg.Setup(c => c.GetSettingValue("oracle_datapump_local_folder", It.IsAny<string?>())).Returns("");
        cfg.Setup(c => c.GetSettingValue("oracle_backup_dump_tool_path", It.IsAny<string?>())).Returns("");

        var enc = new Mock<IExportEncryptionService>();
        var oracle = new Mock<IOracleBackupGateway>();
        var appCfg = new AppConfiguration
        {
            OracleConnectionString = "User Id=WORKAUDIT;Password=x;Data Source=//localhost:1521/XEPDB1",
            BaseDirectory = _testDir
        };

        var svc = new BackupService(appCfg, enc.Object, oracle.Object, cfg.Object);
        var zip = Path.Combine(_testDir, "o.zip");

        var result = await svc.CreateBackupAsync(zip, includeDocuments: false, null, includeOracleData: true);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("oracle_datapump_local_folder");
        oracle.Verify(x => x.ExportSchemaAsync(It.IsAny<OraclePumpExportRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateBackupAsync_WithOracle_MockGateway_ShouldIncludeOracleFolderInZip()
    {
        var dpLocal = Path.Combine(_testDir, "dp");
        Directory.CreateDirectory(dpLocal);

        var cfg = new Mock<IConfigStore>();
        cfg.Setup(c => c.GetSettingValue("oracle_datapump_directory", It.IsAny<string?>())).Returns("DATA_PUMP_DIR");
        cfg.Setup(c => c.GetSettingValue("oracle_datapump_local_folder", It.IsAny<string?>())).Returns(dpLocal);
        cfg.Setup(c => c.GetSettingValue("oracle_backup_dump_tool_path", It.IsAny<string?>())).Returns("");

        var enc = new Mock<IExportEncryptionService>();
        var oracle = new Mock<IOracleBackupGateway>();
        oracle.Setup(x => x.ExportSchemaAsync(It.IsAny<OraclePumpExportRequest>(), It.IsAny<CancellationToken>()))
            .Callback<OraclePumpExportRequest, CancellationToken>((req, _) =>
            {
                File.WriteAllText(Path.Combine(dpLocal, req.DumpFileName), "DMPBINARY");
                File.WriteAllText(Path.Combine(dpLocal, req.LogFileName), "LOGTEXT");
            })
            .ReturnsAsync(OraclePumpOperationResult.Ok(0, "ok", ""));

        var appCfg = new AppConfiguration
        {
            OracleConnectionString = "User Id=WORKAUDIT;Password=x;Data Source=//localhost:1521/XEPDB1",
            BaseDirectory = _testDir
        };

        var svc = new BackupService(appCfg, enc.Object, oracle.Object, cfg.Object);
        var zip = Path.Combine(_testDir, "full.zip");

        var result = await svc.CreateBackupAsync(zip, includeDocuments: false, null, includeOracleData: true);

        result.Success.Should().BeTrue("error: {0}", result.Error ?? "(none)");
        oracle.Verify(x => x.ExportSchemaAsync(It.IsAny<OraclePumpExportRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        names.Should().Contain(n =>
            n.StartsWith($"{BackupService.OracleZipFolder}/", StringComparison.OrdinalIgnoreCase) &&
            n.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task VerifyBackupAsync_WithOracleDump_ShouldPass()
    {
        var enc = new Mock<IExportEncryptionService>();
        var appCfg = new AppConfiguration { OracleConnectionString = "User Id=X;Password=y;Data Source=//z:1521/PDB", BaseDirectory = _testDir };
        var svc = new BackupService(appCfg, enc.Object, null, null);

        var staging = Path.Combine(_testDir, "stage");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(Path.Combine(staging, BackupService.OracleZipFolder));
        File.WriteAllText(Path.Combine(staging, BackupService.OracleZipFolder, "wa_test.dmp"), "x");
        var manifest = new BackupManifest
        {
            Version = "1",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            MachineName = "t",
            IncludesDocuments = false,
            IncludesOracleSchema = true,
            OracleDumpFileName = "wa_test.dmp",
            OracleLogFileName = "wa_test.log",
            OracleSchemaName = "WORKAUDIT",
            OracleDirectoryName = "DATA_PUMP_DIR"
        };
        await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"),
            Newtonsoft.Json.JsonConvert.SerializeObject(manifest));

        var zip = Path.Combine(_testDir, "v.zip");
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(staging, zip);

        var vr = await svc.VerifyBackupAsync(zip);
        vr.Valid.Should().BeTrue();
        vr.IncludesOracleSchema.Should().BeTrue();
        vr.OracleVerificationNote.Should().Contain("present");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch
        {
            // ignore
        }
    }
}
