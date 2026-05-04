using Oracle.ManagedDataAccess.Client;
using Serilog;

namespace WorkAudit.Storage.Oracle.Migrations;

/// <summary>
/// Second-pass cleanup for legacy AI/vision <c>app_settings</c> rows (e.g. category casing/spacing)
/// that migration 54 may have missed.
/// </summary>
internal sealed class Migration_055_CleanupObsoleteAiSettings : IOracleMigration
{
    public int Version => 55;

    public string Name => "Cleanup obsolete AI app settings (case-insensitive)";

    public void Apply(OracleConnection connection, OracleTransaction transaction, ILogger log)
    {
        using var cmd = OracleSql.CreateCommand(connection,
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
            OR LOWER(TRIM(category)) = 'ai'
            """);
        cmd.Transaction = transaction;
        var removedRows = cmd.ExecuteNonQuery();

        log.Information("Migration 055 removed {RemovedRows} obsolete AI app_settings rows", removedRows);
    }
}
