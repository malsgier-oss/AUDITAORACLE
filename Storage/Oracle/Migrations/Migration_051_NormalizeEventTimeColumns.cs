using Oracle.ManagedDataAccess.Client;
using Serilog;

namespace WorkAudit.Storage.Oracle.Migrations;

/// <summary>
/// Renames reserved-ish timestamp columns to event_time and converts values to native TIMESTAMP.
/// </summary>
internal sealed class Migration_051_NormalizeEventTimeColumns : IOracleMigration
{
    public int Version => 51;
    public string Name => "Normalize event_time columns to TIMESTAMP";

    public void Apply(OracleConnection connection, OracleTransaction transaction, ILogger log)
    {
        RenameColumnIfExists(connection, transaction, "AUDIT_LOG", "TIMESTAMP", "EVENT_TIME");
        RenameColumnIfExists(connection, transaction, "REPORT_DISTRIBUTIONS", "TIMESTAMP", "EVENT_TIME");

        EnsureTimestampTypedColumn(connection, transaction, "AUDIT_LOG", "EVENT_TIME");
        EnsureTimestampTypedColumn(connection, transaction, "REPORT_DISTRIBUTIONS", "EVENT_TIME");

        DropIndexIfExists(connection, transaction, "IDX_AUDIT_TIMESTAMP");
        DropIndexIfExists(connection, transaction, "IDX_AUDIT_LOG_ACTION_TIMESTAMP");
        DropIndexIfExists(connection, transaction, "IDX_REPORT_DIST_TIMESTAMP");

        ExecuteIgnoreExists(connection, transaction, """CREATE INDEX idx_audit_event_time ON audit_log (event_time)""");
        ExecuteIgnoreExists(connection, transaction, """CREATE INDEX idx_audit_log_action_event_time ON audit_log (action, event_time)""");
        ExecuteIgnoreExists(connection, transaction, """CREATE INDEX idx_report_dist_event_time ON report_distributions (event_time)""");

        log.Information("Migration 051 normalized event_time columns and indexes");
    }

    private static void RenameColumnIfExists(OracleConnection connection, OracleTransaction transaction, string tableName, string oldColumn, string newColumn)
    {
        if (!ColumnExists(connection, transaction, tableName, oldColumn) || ColumnExists(connection, transaction, tableName, newColumn))
            return;

        using var renameCmd = OracleSql.CreateCommand(
            connection,
            $"""ALTER TABLE {tableName} RENAME COLUMN {oldColumn} TO {newColumn}"""
        );
        renameCmd.Transaction = transaction;
        renameCmd.ExecuteNonQuery();
    }

    private static void EnsureTimestampTypedColumn(OracleConnection connection, OracleTransaction transaction, string tableName, string columnName)
    {
        var dataType = GetColumnDataType(connection, transaction, tableName, columnName);
        if (string.Equals(dataType, "TIMESTAMP", StringComparison.OrdinalIgnoreCase))
            return;

        var tempColumn = $"{columnName}_TS_TMP";

        using (var addCmd = OracleSql.CreateCommand(
                   connection,
                   $"""ALTER TABLE {tableName} ADD ({tempColumn} TIMESTAMP(6))"""))
        {
            addCmd.Transaction = transaction;
            addCmd.ExecuteNonQuery();
        }

        using (var updateCmd = OracleSql.CreateCommand(
                   connection,
                   $"""
                    UPDATE {tableName}
                       SET {tempColumn} =
                           CASE
                               WHEN {columnName} IS NULL OR TRIM({columnName}) IS NULL THEN NULL
                               ELSE CAST(
                                   TO_TIMESTAMP_TZ(REPLACE({columnName}, 'Z', '+00:00'), 'YYYY-MM-DD"T"HH24:MI:SS.FF TZH:TZM')
                                   AS TIMESTAMP
                               )
                           END
                    """))
        {
            updateCmd.Transaction = transaction;
            updateCmd.ExecuteNonQuery();
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

    private static bool ColumnExists(OracleConnection connection, OracleTransaction transaction, string tableName, string columnName)
    {
        using var cmd = OracleSql.CreateCommand(connection,
            """
            SELECT COUNT(*)
              FROM user_tab_cols
             WHERE table_name = :tableName
               AND column_name = :columnName
            """);
        cmd.Transaction = transaction;
        OracleSql.AddParameter(cmd, "tableName", tableName.ToUpperInvariant());
        OracleSql.AddParameter(cmd, "columnName", columnName.ToUpperInvariant());
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
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
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
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
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
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
