using Oracle.ManagedDataAccess.Client;
using Serilog;

namespace WorkAudit.Storage.Oracle.Migrations;

/// <summary>
/// Normalizes required text columns in documents table where prior records
/// may contain NULL/whitespace values from legacy import/update paths.
/// </summary>
internal sealed class Migration_050_NormalizeRequiredDocumentText : IOracleMigration
{
    public int Version => 50;
    public string Name => "Normalize required documents text columns";

    public void Apply(OracleConnection connection, ILogger log)
    {
        using var cmd = OracleSql.CreateCommand(connection, @"
            UPDATE documents
               SET source = CASE
                                WHEN source IS NULL OR TRIM(source) IS NULL THEN 'Unknown'
                                ELSE source
                            END,
                   engagement = CASE
                                    WHEN engagement IS NULL OR TRIM(engagement) IS NULL THEN 'General'
                                    ELSE engagement
                                END,
                   section = CASE
                                WHEN section IS NULL OR TRIM(section) IS NULL THEN 'Unspecified'
                                ELSE section
                            END");

        var affected = cmd.ExecuteNonQuery();
        log.Information("Migration 050 normalized {Count} document rows", affected);
    }
}
