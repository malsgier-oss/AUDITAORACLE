using FluentAssertions;
using WorkAudit.Core.Backup;
using Xunit;

namespace WorkAudit.Tests.Core.Backup;

public sealed class OracleBackupConnectionParserTests
{
    [Fact]
    public void TryParse_ValidOdpNetString_ShouldReturnUserAndDataSource()
    {
        var ok = OracleBackupConnectionParser.TryParse(
            "User Id=WORKAUDIT;Password=secret;Data Source=//host:1521/FREEPDB1",
            out var user, out var pwd, out var ds);

        ok.Should().BeTrue();
        user.Should().Be("WORKAUDIT");
        pwd.Should().Be("secret");
        ds.Should().Be("//host:1521/FREEPDB1");
    }

    [Fact]
    public void BuildUserIdArgument_WithSpecialCharsInPassword_ShouldQuotePassword()
    {
        var arg = OracleBackupConnectionParser.BuildUserIdArgument("WORKAUDIT", "p@ss word", "//host:1521/PDB");
        arg.Should().Contain("WORKAUDIT/");
        arg.Should().Contain("@//host:1521/PDB");
    }
}
