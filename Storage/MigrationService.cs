using Oracle.ManagedDataAccess.Client;
using Serilog;
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

        var currentVersion = GetCurrentVersion(conn);
        _log.Information("Current database version: {Version}", currentVersion);
        if (currentVersion == 0 && TableExists(conn, "WORKAUDIT_MIGRATIONS"))
        {
            EnsureMigrationRecorded(conn, OracleBaselineInstaller.BaselineVersion, "Oracle baseline (existing tables)");
            currentVersion = GetCurrentVersion(conn);
        }

        ApplyForwardMigrations(conn, currentVersion);
    }

    private int GetCurrentVersion(OracleConnection conn)
    {
        if (!TableExists(conn, "documents"))
            return 0;
        if (!TableExists(conn, "workaudit_migrations"))
            return 0;

        using var cmd = OracleSql.CreateCommand(conn, "SELECT NVL(MAX(version), 0) FROM workaudit_migrations");
        var o = cmd.ExecuteScalar();
        return Convert.ToInt32(o);
    }

    private static bool IsMigrationRecorded(OracleConnection conn, int version)
    {
        using var cmd = OracleSql.CreateCommand(conn,
            "SELECT COUNT(*) FROM workaudit_migrations WHERE version = :v");
        OracleSql.AddParameter(cmd, "v", version);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static void RecordMigration(OracleConnection conn, int version, string name)
    {
        using var cmd = OracleSql.CreateCommand(conn,
            "INSERT INTO workaudit_migrations (version, name, applied_at) VALUES (:v, :n, :a)");
        OracleSql.AddParameter(cmd, "v", version);
        OracleSql.AddParameter(cmd, "n", name);
        OracleSql.AddParameter(cmd, "a", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void EnsureMigrationRecorded(OracleConnection conn, int version, string name)
    {
        if (!IsMigrationRecorded(conn, version))
            RecordMigration(conn, version, name);
    }

    private void ApplyForwardMigrations(OracleConnection conn, int currentVersion)
    {
        foreach (var migration in OracleMigrationRegistry.GetOrderedMigrations())
        {
            if (migration.Version <= currentVersion || IsMigrationRecorded(conn, migration.Version))
                continue;

            _log.Information("Applying Oracle migration v{Version}: {Name}", migration.Version, migration.Name);
            migration.Apply(conn, _log);
            RecordMigration(conn, migration.Version, migration.Name);
            _log.Information("Applied Oracle migration v{Version}", migration.Version);
        }
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
