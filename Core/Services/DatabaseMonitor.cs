using System.Diagnostics;
using System.Globalization;
using Oracle.ManagedDataAccess.Client;
using WorkAudit.Domain;
using WorkAudit.Storage;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Core.Services;

public interface IDatabaseMonitor
{
    DatabaseMetrics GetDatabaseMetrics(IMigrationService migration);
}

public sealed class DatabaseMonitor : IDatabaseMonitor
{
    private readonly AppConfiguration _config;
    private readonly IDocumentStore _documentStore;
    private readonly IUserStore _userStore;

    public DatabaseMonitor(AppConfiguration config, IDocumentStore documentStore, IUserStore userStore)
    {
        _config = config;
        _documentStore = documentStore;
        _userStore = userStore;
    }

    public DatabaseMetrics GetDatabaseMetrics(IMigrationService migration)
    {
        var m = new DatabaseMetrics { IsConnected = true, SchemaVersion = migration.GetCurrentVersion().ToString(CultureInfo.InvariantCulture) };
        var sw = Stopwatch.StartNew();
        try
        {
            _ = _userStore.Count();
            _ = _documentStore.GetTotalDocumentCount();
        }
        catch
        {
            m.IsConnected = false;
            m.Warnings.Add("Database connectivity check failed.");
            return m;
        }
        finally
        {
            sw.Stop();
            m.AvgQueryTimeMs = sw.ElapsedMilliseconds;
        }

        m.SlowQueriesCount = m.AvgQueryTimeMs > 5000 ? 1 : 0;
        if (m.AvgQueryTimeMs > 5000)
            m.Warnings.Add("Initial count queries took longer than 5s (server or network load).");

        try
        {
            using var conn = new OracleConnection(_config.OracleConnectionString);
            conn.Open();
            m.ActiveConnections = 1;
            m.TableRowCounts["documents"] = CountTable(conn, "documents");
            m.TableRowCounts["users"] = CountTable(conn, "users");
            m.TableRowCounts["sessions"] = SafeCountTable(conn, "sessions");
            m.TableRowCounts["audit_log"] = SafeCountTable(conn, "audit_log");

            TryOracleDynamicMetrics(conn, m);
        }
        catch (Exception ex)
        {
            m.Warnings.Add($"Extended metrics unavailable: {ex.Message}");
        }

        return m;
    }

    private static long CountTable(OracleConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = OracleSql.ToOracleBindSyntax($"SELECT COUNT(*) FROM {table}");
        var o = cmd.ExecuteScalar();
        return Convert.ToInt64(o ?? 0L, CultureInfo.InvariantCulture);
    }

    private static long SafeCountTable(OracleConnection conn, string table)
    {
        try
        {
            return CountTable(conn, table);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Optional <c>v$session</c> probes (require SELECT on fixed views; skipped silently otherwise).</summary>
    private static void TryOracleDynamicMetrics(OracleConnection conn, DatabaseMetrics m)
    {
        var gotTotal = TryScalarLong(conn, "SELECT COUNT(*) FROM v$session", out var total);
        var gotActive = TryScalarLong(conn, "SELECT COUNT(*) FROM v$session WHERE status = 'ACTIVE'", out var active);
        if (gotTotal)
            m.OracleVSessionTotal = (int)Math.Min(int.MaxValue, total);
        if (gotActive)
            m.OracleVSessionActive = (int)Math.Min(int.MaxValue, active);
    }

    private static bool TryScalarLong(OracleConnection conn, string sql, out long value)
    {
        value = 0;
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = OracleSql.ToOracleBindSyntax(sql);
            var o = cmd.ExecuteScalar();
            value = Convert.ToInt64(o ?? 0L, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
