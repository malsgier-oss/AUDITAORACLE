using System.Globalization;
using Oracle.ManagedDataAccess.Client;
using WorkAudit.Domain;
using WorkAudit.Storage;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Core.Services;

public interface ISessionMonitor
{
    SessionMetrics GetSessionMetrics();
}

public sealed class SessionMonitor : ISessionMonitor
{
    private readonly AppConfiguration _config;
    private readonly IAuditLogStore _auditLogStore;
    private readonly IUserStore _userStore;

    public SessionMonitor(AppConfiguration config, IAuditLogStore auditLogStore, IUserStore userStore)
    {
        _config = config;
        _auditLogStore = auditLogStore;
        _userStore = userStore;
    }

    public SessionMetrics GetSessionMetrics()
    {
        var m = new SessionMetrics();
        var from = DateTime.UtcNow.AddHours(-24);
        var failed = _auditLogStore.Query(from, null, null, AuditAction.LoginFailed, AuditCategory.Authentication, false, 2000, 0);
        m.FailedLoginsLast24h = failed.Count;

        var users = _userStore.ListUsers(isActive: null);
        m.LockedOutUsers = users.Count(u => u.IsLocked);

        try
        {
            using var conn = new OracleConnection(_config.OracleConnectionString);
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = OracleSql.ToOracleBindSyntax(
                    "SELECT COUNT(*) FROM sessions WHERE is_active = 1");
                m.ActiveSessions = Convert.ToInt32(cmd.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = OracleSql.ToOracleBindSyntax(
                    @"SELECT username, created_at FROM sessions WHERE is_active = 1 ORDER BY created_at ASC FETCH FIRST 500 ROWS ONLY");
                using var r = cmd.ExecuteReader();
                DateTime? oldest = null;
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (r.Read())
                {
                    var u = r.GetString(0);
                    if (!string.IsNullOrEmpty(u)) names.Add(u);
                    if (!r.IsDBNull(1))
                    {
                        var created = r.GetDateTime(1);
                        var uUtc = created.Kind == DateTimeKind.Unspecified
                            ? DateTime.SpecifyKind(created, DateTimeKind.Utc)
                            : created.ToUniversalTime();
                        oldest = oldest == null || uUtc < oldest ? uUtc : oldest;
                    }
                }

                m.ActiveUsernames = names.OrderBy(x => x).ToList();
                m.OldestActiveSessionUtc = oldest;
            }
        }
        catch
        {
            m.ActiveSessions = 0;
        }

        return m;
    }
}
