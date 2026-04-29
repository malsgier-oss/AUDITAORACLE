using Oracle.ManagedDataAccess.Client;
using System.Globalization;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Storage.Oracle;

/// <summary>
/// Distributed lease for scheduled background work when multiple app instances share one Oracle schema.
/// </summary>
public interface ISchedulerLockStore
{
    /// <summary>
    /// Acquires the lock if unowned or lease expired, or renews if <paramref name="holderId"/> already holds it.
    /// </summary>
    bool TryAcquireOrRenew(string lockName, string holderId, TimeSpan leaseDuration);

    /// <summary>
    /// Releases the lock early so other instances can run (only if <paramref name="holderId"/> is current holder).
    /// </summary>
    void ReleaseIfHolder(string lockName, string holderId);
}

public sealed class SchedulerLockStore : ISchedulerLockStore
{
    private readonly ILogger _log = LoggingService.ForContext<SchedulerLockStore>();
    private readonly string _connectionString;

    public SchedulerLockStore(string oracleConnectionString)
    {
        _connectionString = oracleConnectionString ?? throw new ArgumentNullException(nameof(oracleConnectionString));
    }

    private static void Prep(OracleCommand cmd)
    {
        cmd.BindByName = true;
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
    }

    public bool TryAcquireOrRenew(string lockName, string holderId, TimeSpan leaseDuration)
    {
        if (string.IsNullOrWhiteSpace(lockName) || string.IsNullOrWhiteSpace(holderId))
            return false;

        var leaseUntil = DateTime.UtcNow.Add(leaseDuration);
        const int maxAttempts = 3;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using var conn = new OracleConnection(_connectionString);
                conn.Open();
                using var tx = conn.BeginTransaction(System.Data.IsolationLevel.Serializable);

                string? existingHolder = null;
                DateTime? existingUntil = null;

                using (var sel = conn.CreateCommand())
                {
                    sel.Transaction = tx;
                    sel.CommandText =
                        "SELECT holder_id, lease_until FROM workaudit_scheduler_locks WHERE lock_name = @n FOR UPDATE";
                    sel.Parameters.AddWithValue("n", lockName);
                    Prep(sel);

                    using var r = sel.ExecuteReader();
                    if (r.Read())
                    {
                        existingHolder = r.IsDBNull(0) ? null : Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture);
                        if (r.IsDBNull(1))
                            existingUntil = null;
                        else
                        {
                            var v = r.GetValue(1);
                            existingUntil = v is DateTime dt
                                ? (dt.Kind == DateTimeKind.Unspecified
                                    ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                                    : dt.ToUniversalTime())
                                : DateTime.Parse(v.ToString()!, null, System.Globalization.DateTimeStyles.RoundtripKind)
                                    .ToUniversalTime();
                        }
                    }
                }

                if (existingHolder == null)
                {
                    try
                    {
                        using var ins = conn.CreateCommand();
                        ins.Transaction = tx;
                        ins.CommandText =
                            "INSERT INTO workaudit_scheduler_locks (lock_name, holder_id, lease_until) VALUES (@n, @h, @u)";
                        ins.Parameters.AddWithValue("n", lockName);
                        ins.Parameters.AddWithValue("h", holderId);
                        ins.Parameters.Add(new OracleParameter("u", OracleDbType.TimeStamp) { Value = leaseUntil });
                        Prep(ins);
                        ins.ExecuteNonQuery();
                        tx.Commit();
                        return true;
                    }
                    catch (OracleException ex) when (ex.Number == 1)
                    {
                        tx.Rollback();
                        continue;
                    }
                }
                else
                {
                    var now = DateTime.UtcNow;
                    if (existingUntil.HasValue && existingUntil.Value > now &&
                        !string.Equals(existingHolder, holderId, StringComparison.Ordinal))
                    {
                        tx.Rollback();
                        return false;
                    }

                    using var upd = conn.CreateCommand();
                    upd.Transaction = tx;
                    upd.CommandText =
                        "UPDATE workaudit_scheduler_locks SET holder_id = @h, lease_until = @u WHERE lock_name = @n";
                    upd.Parameters.AddWithValue("n", lockName);
                    upd.Parameters.AddWithValue("h", holderId);
                    upd.Parameters.Add(new OracleParameter("u", OracleDbType.TimeStamp) { Value = leaseUntil });
                    Prep(upd);
                    var n = upd.ExecuteNonQuery();
                    if (n == 0)
                    {
                        tx.Rollback();
                        continue;
                    }

                    tx.Commit();
                    return true;
                }
            }
            catch (OracleException ex) when (ex.Number is 942 or 904)
            {
                _log.Warning(ex,
                    "workaudit_scheduler_locks missing; run migrations. Leader election disabled for this tick.");
                return true;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Scheduler lock acquire failed for {LockName}", lockName);
                return false;
            }
        }

        return false;
    }

    public void ReleaseIfHolder(string lockName, string holderId)
    {
        if (string.IsNullOrWhiteSpace(lockName) || string.IsNullOrWhiteSpace(holderId))
            return;

        try
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "UPDATE workaudit_scheduler_locks SET lease_until = @past WHERE lock_name = @n AND holder_id = @h";
            cmd.Parameters.AddWithValue("n", lockName);
            cmd.Parameters.AddWithValue("h", holderId);
            cmd.Parameters.Add(new OracleParameter("past", OracleDbType.TimeStamp)
                { Value = DateTime.UtcNow.AddSeconds(-2) });
            Prep(cmd);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "ReleaseIfHolder ignored for {LockName}", lockName);
        }
    }
}
