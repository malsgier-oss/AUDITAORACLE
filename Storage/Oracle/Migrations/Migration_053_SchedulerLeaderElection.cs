using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage.Oracle.Migrations;

/// <summary>
/// Leader-election table so only one desktop instance runs scheduled backup/report jobs against a shared Oracle schema.
/// </summary>
internal sealed class Migration_053_SchedulerLeaderElection : IOracleMigration
{
    public int Version => 53;

    public string Name => "Scheduler leader election (multi-PC)";

    public void Apply(OracleConnection connection, OracleTransaction transaction, ILogger log)
    {
        using (var cmd = OracleSql.CreateCommand(connection,
                   """
                   CREATE TABLE workaudit_scheduler_locks (
                     lock_name VARCHAR2(64) PRIMARY KEY,
                     holder_id VARCHAR2(256) NOT NULL,
                     lease_until TIMESTAMP(6) NOT NULL
                   )
                   """))
        {
            cmd.Transaction = transaction;
            try
            {
                cmd.ExecuteNonQuery();
                log.Information("Created workaudit_scheduler_locks");
            }
            catch (OracleException ex) when (ex.Number == 955)
            {
                log.Debug("workaudit_scheduler_locks already exists");
            }
        }

        var now = DateTime.UtcNow;
        InsertSettingIfMissing(connection, transaction, now,
            "scheduler_leader_election_enabled",
            "true",
            "multi_pc",
            "When true, scheduled backup and scheduled report timers use DB leases so only one PC runs each job at a time (shared Oracle).",
            "bool");

        InsertSettingIfMissing(connection, transaction, now,
            "scheduler_lock_lease_minutes",
            "15",
            "multi_pc",
            "Lease duration (minutes) for scheduled job leader locks.",
            "int");

        InsertSettingIfMissing(connection, transaction, now,
            "scheduled_report_last_run_date",
            "",
            "multi_pc",
            "yyyy-MM-dd (local calendar) of last successful scheduled report; prevents duplicate daily runs across PCs.",
            "string");
    }

    private static void InsertSettingIfMissing(OracleConnection connection, OracleTransaction transaction, DateTime updatedAt,
        string key, string value, string category, string description, string valueType)
    {
        using var cmd = OracleSql.CreateCommand(connection,
            """
            INSERT INTO app_settings (key, value, category, description, value_type, updated_at)
            SELECT :key, :value, :category, :description, :valueType, :updated FROM DUAL
            WHERE NOT EXISTS (SELECT 1 FROM app_settings s WHERE s.key = :key2)
            """);
        cmd.Transaction = transaction;
        OracleSql.AddParameter(cmd, "key", key);
        OracleSql.AddParameter(cmd, "value", value);
        OracleSql.AddParameter(cmd, "category", category);
        OracleSql.AddParameter(cmd, "description", description);
        OracleSql.AddParameter(cmd, "valueType", valueType);
        OracleSql.AddParameter(cmd, "updated", updatedAt);
        OracleSql.AddParameter(cmd, "key2", key);
        cmd.ExecuteNonQuery();
    }
}
