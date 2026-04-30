using FluentAssertions;
using WorkAudit.Storage.Oracle.Migrations;
using Xunit;

namespace WorkAudit.Tests.Storage;

public class OracleMigrationTests
{
    [Fact]
    public void RemoveObsoleteAiSettingsMigration_ShouldOnlyDeleteKnownObsoleteKeys()
    {
        var sql = Migration_054_RemoveObsoleteAiSettings.DeleteObsoleteSettingsSql;

        sql.Should().Contain("'classification_confidence_threshold'");
        sql.Should().Contain("'vision_extraction_enabled'");
        sql.Should().Contain("'vision_model_name'");
        sql.Should().Contain("'vision_timeout_seconds'");
        sql.Should().Contain("'ollama_model'");
        sql.Should().Contain("'ollama_endpoint'");
        sql.Should().NotContain("category = 'ai'");
    }
}
