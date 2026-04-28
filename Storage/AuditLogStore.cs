using System.Data;
using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage;

/// <summary>
/// Storage service for audit log entries.
/// </summary>
public interface IAuditLogStore
{
    long Insert(AuditLogEntry entry);
    AuditLogEntry? Get(long id);
    List<AuditLogEntry> Query(
        DateTime? from = null, DateTime? to = null,
        string? userId = null, string? action = null, string? category = null,
        bool archivedOnly = false,
        int limit = 1000, int offset = 0);
    List<AuditLogEntry> GetByEntityId(string entityType, string entityId);
    int Count(DateTime? from = null, DateTime? to = null);
    void Cleanup(DateTime olderThan);
}

public class AuditLogStore : IAuditLogStore
{
    private readonly ILogger _log = LoggingService.ForContext<AuditLogStore>();
    private readonly string _connectionString;

    private T ExecuteDbOperation<T>(Func<T> operation, string operationName, T defaultValue = default!)
    {
        try
        {
            return operation();
        }
        catch (OracleException ex)
        {
            _log.Error(ex, "Database error in {Operation}: {Message}", operationName, ex.Message);
            return defaultValue;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error in {Operation}: {Message}", operationName, ex.Message);
            return defaultValue;
        }
    }

    private void ExecuteDbOperation(Action operation, string operationName)
    {
        try
        {
            operation();
        }
        catch (OracleException ex)
        {
            _log.Error(ex, "Database error in {Operation}: {Message}", operationName, ex.Message);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error in {Operation}: {Message}", operationName, ex.Message);
        }
    }

    public AuditLogStore(AppConfiguration config)
    {
        _connectionString = config.OracleConnectionString;
    }

    private static void Prep(OracleCommand cmd)
    {
        cmd.BindByName = true;
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
    }

    public long Insert(AuditLogEntry entry)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO audit_log (uuid, event_time, user_id, username, user_role, action, category,
                entity_type, entity_id, entity_name, old_value, new_value, ip_address, details, success, error_message)
            VALUES (@uuid, @event_time, @user_id, @username, @user_role, @action, @category,
                @entity_type, @entity_id, @entity_name, @old_value, @new_value, @ip_address, @details, @success, @error_message)
            RETURNING id INTO :rid";

        cmd.Parameters.AddWithValue("uuid", entry.Uuid);
        var eventTime = DateTime.TryParse(entry.Timestamp, out var parsedEventTime)
            ? parsedEventTime
            : DateTime.UtcNow;
        cmd.Parameters.Add(new OracleParameter("event_time", OracleDbType.TimeStamp) { Value = eventTime });
        cmd.Parameters.AddWithValue("user_id", entry.UserId);
        cmd.Parameters.AddWithValue("username", entry.Username);
        cmd.Parameters.AddWithValue("user_role", entry.UserRole);
        cmd.Parameters.AddWithValue("action", entry.Action);
        cmd.Parameters.AddWithValue("category", entry.Category);
        cmd.Parameters.AddWithValue("entity_type", entry.EntityType);
        cmd.Parameters.AddWithValue("entity_id", entry.EntityId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("entity_name", entry.EntityName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("old_value", entry.OldValue ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("new_value", entry.NewValue ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("ip_address", entry.IpAddress ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("details", entry.Details ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("success", entry.Success ? 1 : 0);
        cmd.Parameters.AddWithValue("error_message", entry.ErrorMessage ?? (object)DBNull.Value);
        var rid = new OracleParameter("rid", OracleDbType.Int64) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(rid);
        Prep(cmd); cmd.ExecuteNonQuery();
        return Convert.ToInt64(rid.Value?.ToString() ?? "0");
        }, nameof(Insert), -1L);
    }

    public AuditLogEntry? Get(long id)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM audit_log WHERE id = @id";
            cmd.Parameters.AddWithValue("id", id);
            Prep(cmd); using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadEntry(reader) : null;
        }, nameof(Get), null);
    }

    public List<AuditLogEntry> Query(
        DateTime? from = null, DateTime? to = null,
        string? userId = null, string? action = null, string? category = null,
        bool archivedOnly = false,
        int limit = 1000, int offset = 0)
    {
        return ExecuteDbOperation(() =>
        {
        var entries = new List<AuditLogEntry>();
        var sql = "SELECT * FROM audit_log WHERE 1=1";
        var parameters = new List<OracleParameter>();

        if (from.HasValue)
        {
            sql += " AND event_time >= @p_from";
            parameters.Add(new OracleParameter("p_from", OracleDbType.TimeStamp) { Value = from.Value });
        }

        if (to.HasValue)
        {
            sql += " AND event_time <= @p_to";
            parameters.Add(new OracleParameter("p_to", OracleDbType.TimeStamp) { Value = to.Value });
        }

        if (!string.IsNullOrEmpty(userId))
        {
            sql += " AND user_id = @user_id";
            parameters.Add(new OracleParameter("user_id", userId));
        }

        if (!string.IsNullOrEmpty(action))
        {
            sql += " AND action = @action";
            parameters.Add(new OracleParameter("action", action));
        }

        if (!string.IsNullOrEmpty(category))
        {
            sql += " AND category = @category";
            parameters.Add(new OracleParameter("category", category));
        }

        if (archivedOnly)
        {
            sql += " AND action IN ('DocumentArchived','LegalHoldApplied','LegalHoldReleased','ArchiveExported','HashVerificationFailed')";
        }

        sql += " ORDER BY event_time DESC OFFSET @p_offset ROWS FETCH NEXT @p_limit ROWS ONLY";
        parameters.Add(new OracleParameter("p_limit", limit));
        parameters.Add(new OracleParameter("p_offset", offset));

        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.Add(p);

        Prep(cmd); using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
        }, nameof(Query), new List<AuditLogEntry>());
    }

    public List<AuditLogEntry> GetByEntityId(string entityType, string entityId)
    {
        return ExecuteDbOperation(() =>
        {
            var entries = new List<AuditLogEntry>();

            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM audit_log WHERE entity_type = @p_type AND entity_id = @id ORDER BY event_time DESC";
            cmd.Parameters.AddWithValue("p_type", entityType);
            cmd.Parameters.AddWithValue("id", entityId);
            Prep(cmd); using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(ReadEntry(reader));
            }

            return entries;
        }, nameof(GetByEntityId), new List<AuditLogEntry>());
    }

    public int Count(DateTime? from = null, DateTime? to = null)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();

            var sql = "SELECT COUNT(*) FROM audit_log WHERE 1=1";
            using var cmd = conn.CreateCommand();

            if (from.HasValue)
            {
                sql += " AND event_time >= @p_from";
                cmd.Parameters.Add(new OracleParameter("p_from", OracleDbType.TimeStamp) { Value = from.Value });
            }

            if (to.HasValue)
            {
                sql += " AND event_time <= @p_to";
                cmd.Parameters.Add(new OracleParameter("p_to", OracleDbType.TimeStamp) { Value = to.Value });
            }

            cmd.CommandText = sql;
            Prep(cmd); return Convert.ToInt32(cmd.ExecuteScalar());
        }, nameof(Count), 0);
    }

    public void Cleanup(DateTime olderThan)
    {
        ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM audit_log WHERE event_time < @date";
            cmd.Parameters.Add(new OracleParameter("date", OracleDbType.TimeStamp) { Value = olderThan });
            Prep(cmd); var deleted = cmd.ExecuteNonQuery();
            _log.Information("Cleaned up {Count} old audit log entries", deleted);
        }, nameof(Cleanup));
    }

    private static AuditLogEntry ReadEntry(OracleDataReader r)
    {
        return new AuditLogEntry
        {
            Id = r.GetInt64(r.GetOrdinal("id")),
            Uuid = r.GetString(r.GetOrdinal("uuid")),
            Timestamp = r.GetDateTime(r.GetOrdinal("event_time")).ToString("O"),
            UserId = r.GetString(r.GetOrdinal("user_id")),
            Username = r.GetString(r.GetOrdinal("username")),
            UserRole = r.GetString(r.GetOrdinal("user_role")),
            Action = r.GetString(r.GetOrdinal("action")),
            Category = r.GetString(r.GetOrdinal("category")),
            EntityType = r.GetString(r.GetOrdinal("entity_type")),
            EntityId = GetStringOrNull(r, "entity_id"),
            EntityName = GetStringOrNull(r, "entity_name"),
            OldValue = GetStringOrNull(r, "old_value"),
            NewValue = GetStringOrNull(r, "new_value"),
            IpAddress = GetStringOrNull(r, "ip_address"),
            Details = GetStringOrNull(r, "details"),
            Success = r.GetInt32(r.GetOrdinal("success")) == 1,
            ErrorMessage = GetStringOrNull(r, "error_message")
        };
    }

    private static string? GetStringOrNull(OracleDataReader r, string column)
    {
        var ordinal = r.GetOrdinal(column);
        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }
}
