using Oracle.ManagedDataAccess.Client;
using Serilog;

namespace WorkAudit.Storage.Oracle.Migrations;

/// <summary>Removes obsolete AI/vision app_settings keys from existing Oracle databases.</summary>
internal sealed class Migration_054_RemoveObsoleteAiSettings : IOracleMigration
{
    public int Version => 54;

    public string Name => "Remove obsolete AI app settings";

    internal const string DeleteObsoleteSettingsSql =
        """
        DELETE FROM app_settings
        WHERE key IN (
            'classification_confidence_threshold',
            'vision_extraction_enabled',
            'vision_model_name',
            'vision_timeout_seconds',
            'ollama_model',
            'ollama_endpoint'
        )
        """;

    public void Apply(OracleConnection connection, OracleTransaction transaction, ILogger log)
    {
        using var cmd = OracleSql.CreateCommand(connection, DeleteObsoleteSettingsSql);
        cmd.Transaction = transaction;
        var removedRows = cmd.ExecuteNonQuery();

        log.Information("Migration 054 removed {RemovedRows} obsolete AI app_settings rows", removedRows);
    }
}
