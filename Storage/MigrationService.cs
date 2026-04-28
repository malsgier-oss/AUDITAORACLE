using Oracle.ManagedDataAccess.Client;
using Serilog;
using System.Data;
using WorkAudit.Core.Services;
using WorkAudit.Storage.Oracle;
using WorkAudit.Storage.Oracle.Migrations;

namespace WorkAudit.Storage;

public interface IMigrationService
{
    int GetCurrentVersion();
    void Migrate();
    List<MigrationInfo> GetMigrationHistory();
}

/// <summary>Oracle-only migrations: baseline schema at <see cref="OracleBaselineInstaller.BaselineVersion"/>.</summary>
public class MigrationService : IMigrationService
{
    private readonly ILogger _log = LoggingService.ForContext<MigrationService>();
    private readonly string _connectionString;

    public MigrationService(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    private static bool TableExists(OracleConnection conn, string tableNameLower)
    {
        using var cmd = OracleSql.CreateCommand(conn,
            "SELECT COUNT(*) FROM user_tables WHERE LOWER(table_name) = LOWER(:t)");
        OracleSql.AddParameter(cmd, "t", tableNameLower.Trim('"'));
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public int GetCurrentVersion()
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        if (!TableExists(conn, "documents"))
            return 0;
        if (!TableExists(conn, "workaudit_migrations"))
            return 0;

        using var cmd = OracleSql.CreateCommand(conn, "SELECT NVL(MAX(version), 0) FROM workaudit_migrations");
        var o = cmd.ExecuteScalar();
        return Convert.ToInt32(o);
    }

    public void Migrate()
    {
        _log.Information("Starting database migration check...");
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        if (!TableExists(conn, "documents"))
        {
            _log.Information("Applying Oracle baseline schema v{Version}", OracleBaselineInstaller.BaselineVersion);
            OracleBaselineInstaller.Install(conn);
            EnsureMigrationRecorded(conn, OracleBaselineInstaller.BaselineVersion, "Oracle baseline schema");
            _log.Information("Baseline applied.");
        }

        EnsureMigrationLockInfrastructure(conn);

        using var migrationTx = conn.BeginTransaction(IsolationLevel.Serializable);
        AcquireMigrationLock(conn, migrationTx);

        var currentVersion = GetCurrentVersion(conn, migrationTx);
        _log.Information("Current database version: {Version}", currentVersion);
        if (currentVersion == 0 && TableExists(conn, "WORKAUDIT_MIGRATIONS"))
        {
            EnsureMigrationRecorded(conn, OracleBaselineInstaller.BaselineVersion, "Oracle baseline (existing tables)", migrationTx);
            currentVersion = GetCurrentVersion(conn, migrationTx);
        }

        ApplyForwardMigrations(conn, currentVersion, migrationTx);
        ValidateCriticalSchema(conn, migrationTx);
        migrationTx.Commit();
    }

    private int GetCurrentVersion(OracleConnection conn, OracleTransaction? tx = null)
    {
        if (!TableExists(conn, "documents"))
            return 0;
        if (!TableExists(conn, "workaudit_migrations"))
            return 0;

        using var cmd = OracleSql.CreateCommand(conn, "SELECT NVL(MAX(version), 0) FROM workaudit_migrations");
        if (tx != null)
            cmd.Transaction = tx;
        var o = cmd.ExecuteScalar();
        return Convert.ToInt32(o);
    }

    private static bool IsMigrationRecorded(OracleConnection conn, int version, OracleTransaction? tx = null)
    {
        using var cmd = OracleSql.CreateCommand(conn,
            "SELECT COUNT(*) FROM workaudit_migrations WHERE version = :v");
        if (tx != null)
            cmd.Transaction = tx;
        OracleSql.AddParameter(cmd, "v", version);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static void RecordMigration(OracleConnection conn, int version, string name, OracleTransaction? tx = null)
    {
        using var cmd = OracleSql.CreateCommand(conn,
            "INSERT INTO workaudit_migrations (version, name, applied_at) VALUES (:v, :n, :a)");
        if (tx != null)
            cmd.Transaction = tx;
        OracleSql.AddParameter(cmd, "v", version);
        OracleSql.AddParameter(cmd, "n", name);
        OracleSql.AddParameter(cmd, "a", DateTime.UtcNow);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureMigrationRecorded(OracleConnection conn, int version, string name, OracleTransaction? tx = null)
    {
        if (!IsMigrationRecorded(conn, version, tx))
            RecordMigration(conn, version, name, tx);
    }

    private void ApplyForwardMigrations(OracleConnection conn, int currentVersion, OracleTransaction tx)
    {
        foreach (var migration in OracleMigrationRegistry.GetOrderedMigrations())
        {
            if (migration.Version <= currentVersion || IsMigrationRecorded(conn, migration.Version, tx))
                continue;

            _log.Information("Applying Oracle migration v{Version}: {Name}", migration.Version, migration.Name);
            migration.Apply(conn, tx, _log);
            RecordMigration(conn, migration.Version, migration.Name, tx);
            _log.Information("Applied Oracle migration v{Version}", migration.Version);
        }
    }

    private static void EnsureMigrationLockInfrastructure(OracleConnection conn)
    {
        using (var createCmd = OracleSql.CreateCommand(conn,
                   """
                   CREATE TABLE workaudit_migration_lock (
                     id NUMBER(10,0) PRIMARY KEY,
                     updated_at VARCHAR2(64) NOT NULL
                   )
                   """))
        {
            try
            {
                createCmd.ExecuteNonQuery();
            }
            catch (OracleException ex) when (ex.Number == 955)
            {
                // Table already exists.
            }
        }

        using var seedCmd = OracleSql.CreateCommand(conn,
            """
            INSERT INTO workaudit_migration_lock (id, updated_at)
            SELECT 1, :at FROM dual
            WHERE NOT EXISTS (SELECT 1 FROM workaudit_migration_lock WHERE id = 1)
            """);
        OracleSql.AddParameter(seedCmd, "at", DateTime.UtcNow);
        seedCmd.ExecuteNonQuery();
    }

    private static void AcquireMigrationLock(OracleConnection conn, OracleTransaction tx)
    {
        using var cmd = OracleSql.CreateCommand(conn,
            "SELECT id FROM workaudit_migration_lock WHERE id = 1 FOR UPDATE");
        cmd.Transaction = tx;
        _ = cmd.ExecuteScalar();
    }

    private static void ValidateCriticalSchema(OracleConnection conn, OracleTransaction tx)
    {
        string[] criticalTables = ["documents", "users", "workaudit_migrations", "sessions", "audit_log"];
        foreach (var table in criticalTables)
        {
            if (!TableExists(conn, table))
                throw new InvalidOperationException($"Missing required Oracle table: {table}");
        }

        EnsureColumnExists(conn, tx, "AUDIT_LOG", "EVENT_TIME");
        EnsureColumnExists(conn, tx, "REPORT_DISTRIBUTIONS", "EVENT_TIME");
    }

    private static void EnsureColumnExists(OracleConnection conn, OracleTransaction tx, string tableName, string columnName)
    {
        using var cmd = OracleSql.CreateCommand(conn,
            """
            SELECT COUNT(*)
              FROM user_tab_cols
             WHERE table_name = :tableName
               AND column_name = :columnName
            """);
        cmd.Transaction = tx;
        OracleSql.AddParameter(cmd, "tableName", tableName);
        OracleSql.AddParameter(cmd, "columnName", columnName);
        if (Convert.ToInt32(cmd.ExecuteScalar()) == 0)
            throw new InvalidOperationException($"Missing required Oracle column: {tableName}.{columnName}");
    }

    public List<MigrationInfo> GetMigrationHistory()
    {
        var history = new List<MigrationInfo>();
        try
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            if (!TableExists(conn, "WORKAUDIT_MIGRATIONS"))
                return history;

            using var cmd = OracleSql.CreateCommand(conn,
                "SELECT version, name, applied_at FROM workaudit_migrations ORDER BY version ASC");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                history.Add(new MigrationInfo
                {
                    Version = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    AppliedAt = reader.GetString(2)
                });
            }
        }
        catch
        {
            /* fresh DB */
        }

        return history;
    }
}

public class MigrationInfo
{
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public string AppliedAt { get; set; } = "";
}
