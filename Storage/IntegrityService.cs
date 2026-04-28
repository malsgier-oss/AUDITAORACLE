using System.IO;
using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage;

/// <summary>
/// Validates database integrity: foreign keys, orphaned records, and data consistency.
/// </summary>
public interface IIntegrityService
{
    IntegrityReport RunChecks();
    bool RepairOrphanedSessions();
}

public class IntegrityReport
{
    public bool Passed { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public int OrphanedSessions { get; set; }
    public int OrphanedDocumentFiles { get; set; }
}

public class IntegrityService : IIntegrityService
{
    private readonly ILogger _log = LoggingService.ForContext<IntegrityService>();
    private readonly string _connectionString;
    private static void Prep(OracleCommand cmd)
    {
        cmd.BindByName = true;
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
    }

    public IntegrityService(string dbPath)
    {
        _connectionString = dbPath;
    }

    public IntegrityReport RunChecks()
    {
        var report = new IntegrityReport { Passed = true };

        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        // Check for orphaned sessions (sessions referencing deleted users)
        var orphanedSessions = GetOrphanedSessions(conn);
        report.OrphanedSessions = orphanedSessions.Count;
        if (orphanedSessions.Count > 0)
        {
            report.Warnings.Add($"{orphanedSessions.Count} session(s) reference non-existent users");
            report.Passed = false;
        }

        // Check for documents with missing file paths
        var orphanedFiles = GetDocumentsWithMissingFiles(conn);
        report.OrphanedDocumentFiles = orphanedFiles.Count;
        if (orphanedFiles.Count > 0)
        {
            report.Warnings.Add($"{orphanedFiles.Count} document(s) reference missing files");
        }

        // Verify users table has required columns
        if (!TableExists(conn, "users"))
        {
            report.Errors.Add("Users table is missing");
            report.Passed = false;
        }

        // Verify sessions table
        if (!TableExists(conn, "sessions"))
        {
            report.Errors.Add("Sessions table is missing");
            report.Passed = false;
        }

        _log.Information("Integrity check completed: Passed={Passed}, OrphanedSessions={Sessions}, MissingFiles={Files}",
            report.Passed, report.OrphanedSessions, report.OrphanedDocumentFiles);

        return report;
    }

    public bool RepairOrphanedSessions()
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        var orphaned = GetOrphanedSessions(conn);
        if (orphaned.Count == 0)
            return true;

        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM sessions WHERE user_id NOT IN (SELECT id FROM users)";
            Prep(cmd);
            var deleted = cmd.ExecuteNonQuery();
            tx.Commit();
            _log.Information("Repaired {Count} orphaned sessions", deleted);
            return true;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _log.Error(ex, "Failed to repair orphaned sessions");
            return false;
        }
    }

    private static List<int> GetOrphanedSessions(OracleConnection conn)
    {
        var ids = new List<int>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.id FROM sessions s
            LEFT JOIN users u ON s.user_id = u.id
            WHERE u.id IS NULL";
        Prep(cmd); using var reader = cmd.ExecuteReader();
        while (reader.Read())
            ids.Add(reader.GetInt32(0));
        return ids;
    }

    private static List<long> GetDocumentsWithMissingFiles(OracleConnection conn)
    {
        var ids = new List<long>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, file_path FROM documents WHERE file_path IS NOT NULL AND file_path != ''";
        Prep(cmd); using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var path = reader.IsDBNull(1) ? null : reader.GetString(1);
            if (!string.IsNullOrEmpty(path) && !File.Exists(path))
                ids.Add(id);
        }
        return ids;
    }

    private static bool TableExists(OracleConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT table_name FROM user_tables WHERE table_name = UPPER(@name)";
        cmd.Parameters.AddWithValue("@name", tableName);
        Prep(cmd);
        return cmd.ExecuteScalar() != null;
    }
}
