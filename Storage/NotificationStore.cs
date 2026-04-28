using Oracle.ManagedDataAccess.Client;
using WorkAudit.Core.Services;

namespace WorkAudit.Storage;

public class NotificationStore : INotificationStore
{
    private readonly string _connectionString;

    public NotificationStore(AppConfiguration config)
    {
        _connectionString = config.OracleConnectionString;
    }

    public void Create(Notification n)
    {
        n.CreatedAt = DateTime.UtcNow.ToString("O");
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO notifications (user_id, type, title, message, entity_type, entity_id, is_read, created_at, priority)
            VALUES (@uid, @type, @title, @msg, @et, @eid, 0, @created, @priority)";
        cmd.Parameters.AddWithValue("@uid", n.UserId);
        cmd.Parameters.AddWithValue("@type", n.Type);
        cmd.Parameters.AddWithValue("@title", n.Title);
        cmd.Parameters.AddWithValue("@msg", n.Message ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@et", n.EntityType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@eid", n.EntityId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@created", n.CreatedAt);
        cmd.Parameters.AddWithValue("@priority", "Normal");
        cmd.ExecuteNonQuery();
    }

    public List<Notification> GetByUser(int userId, bool unreadOnly = false, int limit = 50)
    {
        var list = new List<Notification>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM notifications WHERE user_id = @uid" + (unreadOnly ? " AND is_read = 0" : "") + " ORDER BY created_at DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@limit", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadNotification(r));
        return list;
    }

    public int GetUnreadCount(int userId)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM notifications WHERE user_id = @uid AND is_read = 0";
        cmd.Parameters.AddWithValue("@uid", userId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void MarkRead(int id)
    {
        var now = DateTime.UtcNow.ToString("O");
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE notifications SET is_read = 1, read_at = @now WHERE id = @id";
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void MarkAllRead(int userId)
    {
        var now = DateTime.UtcNow.ToString("O");
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE notifications SET is_read = 1, read_at = @now WHERE user_id = @uid";
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@uid", userId);
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
            CreatedAt = r.GetString(r.GetOrdinal("created_at")),
            ReadAt = r.IsDBNull(r.GetOrdinal("read_at")) ? null : r.GetString(r.GetOrdinal("read_at"))
        };
    }
}
