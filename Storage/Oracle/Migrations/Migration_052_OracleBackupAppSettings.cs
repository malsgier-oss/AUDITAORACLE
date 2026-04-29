using Oracle.ManagedDataAccess.Client;
using Serilog;

namespace WorkAudit.Storage.Oracle.Migrations;

/// <summary>Adds app_settings keys for optional Oracle Data Pump–backed backups.</summary>
internal sealed class Migration_052_OracleBackupAppSettings : IOracleMigration
{
    public int Version => 52;
    public string Name => "Oracle backup app settings";

    public void Apply(OracleConnection connection, OracleTransaction transaction, ILogger log)
    {
        var now = DateTime.UtcNow;
        InsertSettingIfMissing(connection, transaction, now,
            "include_oracle_data", "false", "backup", "Include Oracle schema in WorkAudit backups (requires expdp/impdp and DIRECTORY setup)", "bool");
        InsertSettingIfMissing(connection, transaction, now,
            "oracle_datapump_directory", "DATA_PUMP_DIR", "backup", "Oracle DIRECTORY object name used by expdp/impdp", "string");
        InsertSettingIfMissing(connection, transaction, now,
            "oracle_datapump_local_folder", "", "backup",
            "Folder path matching the Oracle DIRECTORY physical path (visible to this PC) where .dmp/.log files are read after export and placed before import", "string");
        InsertSettingIfMissing(connection, transaction, now,
            "oracle_backup_dump_tool_path", "", "backup",
            "Optional full path to expdp.exe or folder containing expdp.exe/impdp.exe; empty = use PATH", "string");
        InsertSettingIfMissing(connection, transaction, now,
            "oracle_backup_retention_days", "0", "backup",
            "Reserved: days to retain Oracle dump copies on disk (0 = use backup file retention only)", "int");

        log.Information("Migration 052 applied Oracle backup settings");
    }

    private static void InsertSettingIfMissing(OracleConnection connection, OracleTransaction transaction, DateTime updatedAt,
        string key, string value, string category, string description, string valueType)
    {
        using var cmd = OracleSql.CreateCommand(connection,
            """
            INSERT INTO app_settings (key, value, category, description, value_type, updated_at)
            SELECT :key, :value, :category, :description, :valueType, :updated FROM DUAL
            WHERE NOT EXISTS (SELECT 1 FROM app_settings s WHERE s.key = :key)
            """);
        cmd.Transaction = transaction;
        OracleSql.AddParameter(cmd, "key", key);
        OracleSql.AddParameter(cmd, "value", value);
        OracleSql.AddParameter(cmd, "category", category);
        OracleSql.AddParameter(cmd, "description", description);
        OracleSql.AddParameter(cmd, "valueType", valueType);
        OracleSql.AddParameter(cmd, "updated", updatedAt);
        cmd.ExecuteNonQuery();
    }
}
