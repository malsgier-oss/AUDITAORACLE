using Oracle.ManagedDataAccess.Client;
using WorkAudit.Core.Services;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage;

public class NotificationStore : INotificationStore
{
    private readonly string _connectionString;
    private static void Prep(OracleCommand cmd)
    {
        cmd.BindByName = true;
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
    }

    public NotificationStore(AppConfiguration config)
    {
        _connectionString = config.OracleConnectionString;
    }

    public void Create(Notification n)
    {
        var now = DateTime.UtcNow;
        n.CreatedAt = now.ToString("O");
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO notifications (user_id, type, title, message, entity_type, entity_id, is_read, created_at, priority)
            VALUES (@p_uid, @p_type, @title, @msg, @et, @eid, 0, @created, @priority)";
        cmd.Parameters.AddWithValue("@p_uid", n.UserId);
        cmd.Parameters.AddWithValue("@p_type", n.Type);
        cmd.Parameters.AddWithValue("@title", n.Title);
        cmd.Parameters.AddWithValue("@msg", n.Message ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@et", n.EntityType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@eid", n.EntityId ?? (object)DBNull.Value);
        cmd.Parameters.Add(new OracleParameter("@created", OracleDbType.TimeStamp) { Value = now });
        cmd.Parameters.AddWithValue("@priority", "Normal");
        Prep(cmd);
        cmd.ExecuteNonQuery();
    }

    public List<Notification> GetByUser(int userId, bool unreadOnly = false, int limit = 50)
    {
        var list = new List<Notification>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM notifications WHERE user_id = @p_uid" + (unreadOnly ? " AND is_read = 0" : "") + " ORDER BY created_at DESC FETCH FIRST @limit ROWS ONLY";
        cmd.Parameters.AddWithValue("@p_uid", userId);
        cmd.Parameters.AddWithValue("@limit", limit);
        Prep(cmd); using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadNotification(r));
        return list;
    }

    public int GetUnreadCount(int userId)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM notifications WHERE user_id = @p_uid AND is_read = 0";
        cmd.Parameters.AddWithValue("@p_uid", userId);
        Prep(cmd);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void MarkRead(int id)
    {
        var now = DateTime.UtcNow;
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE notifications SET is_read = 1, read_at = @now WHERE id = @id";
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@id", id);
        Prep(cmd);
        cmd.ExecuteNonQuery();
    }

    public void MarkAllRead(int userId)
    {
        var now = DateTime.UtcNow;
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE notifications SET is_read = 1, read_at = @now WHERE user_id = @p_uid";
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@p_uid", userId);
        Prep(cmd);
        cmd.ExecuteNonQuery();
    }

    private static Notification ReadNotification(OracleDataReader r)
    {
        return new Notification
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            UserId = r.GetInt32(r.GetOrdinal("user_id")),
            Type = r.GetString(r.GetOrdinal("type")),
            Title = r.GetString(r.GetOrdinal("title")),
            Message = r.IsDBNull(r.GetOrdinal("message")) ? null : r.GetString(r.GetOrdinal("message")),
            EntityType = r.IsDBNull(r.GetOrdinal("entity_type")) ? null : r.GetString(r.GetOrdinal("entity_type")),
            EntityId = r.IsDBNull(r.GetOrdinal("entity_id")) ? null : r.GetInt32(r.GetOrdinal("entity_id")),
            IsRead = r.GetInt32(r.GetOrdinal("is_read")) == 1,
            CreatedAt = GetStringOrDateTimeStringOrNull(r, "created_at") ?? string.Empty,
            ReadAt = GetStringOrDateTimeStringOrNull(r, "read_at")
        };
    }

    private static string? GetStringOrDateTimeStringOrNull(OracleDataReader r, string column)
    {
        var ord = r.GetOrdinal(column);
        if (r.IsDBNull(ord))
            return null;

        try
        {
            return r.GetDateTime(ord).ToString("O");
        }
        catch
        {
            return r.GetString(ord);
        }
    }
}
