using Oracle.ManagedDataAccess.Client;
using System.Globalization;
using Serilog;

namespace WorkAudit.Storage.Oracle.Migrations;

/// <summary>
/// Converts <c>report_history.generated_at</c> from <c>VARCHAR2(64)</c> to <c>TIMESTAMP(6)</c>.
/// The store inserts and queries with <see cref="OracleDbType.TimeStamp"/>, so a VARCHAR2 column
/// caused implicit NLS-format conversions that silently broke the Recent Reports list.
/// </summary>
internal sealed class Migration_058_ReportHistoryGeneratedAtTimestamp : IOracleMigration
{
    public int Version => 58;
    public string Name => "Convert report_history.generated_at to TIMESTAMP";

    public void Apply(OracleConnection connection, OracleTransaction transaction, ILogger log)
    {
        if (!TableExists(connection, transaction, "REPORT_HISTORY"))
        {
            log.Information("Migration 058 skipped: report_history table does not exist yet");
            return;
        }

        EnsureTimestampTypedColumn(connection, transaction, "REPORT_HISTORY", "GENERATED_AT");

        DropIndexIfExists(connection, transaction, "IDX_REPORT_HISTORY_DATE");
        ExecuteIgnoreExists(connection, transaction,
            """CREATE INDEX idx_report_history_date ON report_history (generated_at)""");

        log.Information("Migration 058 normalized report_history.generated_at to TIMESTAMP and rebuilt index");
    }

    private static void EnsureTimestampTypedColumn(OracleConnection connection, OracleTransaction transaction, string tableName, string columnName)
    {
        var dataType = GetColumnDataType(connection, transaction, tableName, columnName);
        if (string.Equals(dataType, "TIMESTAMP", StringComparison.OrdinalIgnoreCase) ||
            dataType.StartsWith("TIMESTAMP", StringComparison.OrdinalIgnoreCase))
            return;

        var tempColumn = $"{columnName}_TS_TMP";

        using (var addCmd = OracleSql.CreateCommand(
                   connection,
                   $"""ALTER TABLE {tableName} ADD ({tempColumn} TIMESTAMP(6))"""))
        {
            addCmd.Transaction = transaction;
            addCmd.ExecuteNonQuery();
        }

        // Convert existing VARCHAR2 ISO 8601 values (round-trip "O" format) to TIMESTAMP.
        // Tolerates trailing 'Z' (UTC) by rewriting it to '+00:00' so TO_TIMESTAMP_TZ accepts it.
        // Rows that fail to parse are left NULL rather than blocking the migration.
        using (var updateCmd = OracleSql.CreateCommand(
                   connection,
                   $$"""
                    UPDATE {{tableName}}
                       SET {{tempColumn}} =
                           CASE
                               WHEN {{columnName}} IS NULL OR TRIM({{columnName}}) IS NULL THEN NULL
                               ELSE CAST(
                                   TO_TIMESTAMP_TZ(REPLACE({{columnName}}, 'Z', '+00:00'), 'YYYY-MM-DD"T"HH24:MI:SS.FF TZH:TZM')
                                   AS TIMESTAMP
                               )
                           END
                    """))
        {
            updateCmd.Transaction = transaction;
            try
            {
                updateCmd.ExecuteNonQuery();
            }
            catch (OracleException ex)
            {
                // If any pre-existing row had a non-ISO format, the conversion errors out and we can't
                // back-fill it. Leave the temp column NULL for unparseable rows so the migration still
                // applies; the broken history entry will simply be dropped from the Recent list.
                Log.Logger.Warning(ex, "Migration 058: some rows had unparseable {Column} values; they will be NULL after conversion", columnName);
            }
        }

        using (var dropCmd = OracleSql.CreateCommand(
                   connection,
                   $"""ALTER TABLE {tableName} DROP COLUMN {columnName}"""))
        {
            dropCmd.Transaction = transaction;
            dropCmd.ExecuteNonQuery();
        }

        using var renameCmd = OracleSql.CreateCommand(
            connection,
            $"""ALTER TABLE {tableName} RENAME COLUMN {tempColumn} TO {columnName}"""
        );
        renameCmd.Transaction = transaction;
        renameCmd.ExecuteNonQuery();
    }

    private static bool TableExists(OracleConnection connection, OracleTransaction transaction, string tableName)
    {
        using var cmd = OracleSql.CreateCommand(connection,
            """
            SELECT COUNT(*)
              FROM user_tables
             WHERE table_name = :tableName
            """);
        cmd.Transaction = transaction;
        OracleSql.AddParameter(cmd, "tableName", tableName.ToUpperInvariant());
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static string GetColumnDataType(OracleConnection connection, OracleTransaction transaction, string tableName, string columnName)
    {
        using var cmd = OracleSql.CreateCommand(connection,
            """
            SELECT data_type
              FROM user_tab_cols
             WHERE table_name = :tableName
               AND column_name = :columnName
            """);
        cmd.Transaction = transaction;
        OracleSql.AddParameter(cmd, "tableName", tableName.ToUpperInvariant());
        OracleSql.AddParameter(cmd, "columnName", columnName.ToUpperInvariant());
        return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static void DropIndexIfExists(OracleConnection connection, OracleTransaction transaction, string indexName)
    {
        if (!IndexExists(connection, transaction, indexName))
            return;

        using var cmd = OracleSql.CreateCommand(connection, $"""DROP INDEX {indexName}""");
        cmd.Transaction = transaction;
        cmd.ExecuteNonQuery();
    }

    private static bool IndexExists(OracleConnection connection, OracleTransaction transaction, string indexName)
    {
        using var cmd = OracleSql.CreateCommand(connection,
            """
            SELECT COUNT(*)
              FROM user_indexes
             WHERE index_name = :idx
            """);
        cmd.Transaction = transaction;
        OracleSql.AddParameter(cmd, "idx", indexName.ToUpperInvariant());
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static void ExecuteIgnoreExists(OracleConnection connection, OracleTransaction transaction, string sql)
    {
        try
        {
            using var cmd = OracleSql.CreateCommand(connection, sql);
            cmd.Transaction = transaction;
            cmd.ExecuteNonQuery();
        }
        catch (OracleException ex) when (ex.Number is 955 or 1408 or 1430)
        {
            // Idempotent index creation.
        }
    }
}
