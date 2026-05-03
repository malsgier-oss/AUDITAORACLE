using System.Collections.Generic;
using System.Globalization;
using System.Data;
using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage;

/// <summary>
/// Storage service for users and sessions.
/// </summary>
public interface IUserStore
{
    // User CRUD
    long Insert(User user);
    /// <param name="requirePasswordChangeOnNextLogin">When true, sets <c>must_change_password</c> to 1; when false, sets to 0; when null, leaves the column unchanged.</param>
    bool UpdatePassword(int id, string passwordHash, string? updatedBy = null, bool? requirePasswordChangeOnNextLogin = null);
    User? GetById(int id);
    User? GetByUuid(string uuid);
    User? GetByUsername(string username);
    List<User> ListUsers(string? role = null, bool? isActive = null, int limit = 1000);
    bool Update(User user);
    bool Delete(int id);
    int Count();

    // Emergency access codes (Administrator recovery; stored as BCrypt hashes)
    IReadOnlyList<(int Id, string Hash)> GetUnusedEmergencyCodeHashes(int userId);
    bool MarkEmergencyCodeUsed(int codeId, int userId);
    void ReplaceEmergencyCodes(int userId, IReadOnlyList<string> codeHashes);

    // Session management
    void CreateSession(Session session);
    Session? GetSession(string token);
    void InvalidateSession(string token);
    void InvalidateUserSessions(int userId);
    List<Session> GetUserSessions(int userId);
}

public class UserStore : IUserStore
{
    private readonly ILogger _log = LoggingService.ForContext<UserStore>();
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

    public UserStore(AppConfiguration config)
    {
        _connectionString = config.OracleConnectionString;
    }

    private static void Prep(OracleCommand cmd)
    {
        cmd.BindByName = true;
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
    }

    public long Insert(User user)
    {
        return ExecuteDbOperation(() =>
        {
            user.Uuid = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            user.CreatedAt = now.ToString("O");

            using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO users (uuid, username, display_name, email, password_hash, role, branch, department,
                is_active, is_locked, must_change_password, failed_login_attempts, created_at, created_by)
            VALUES (@uuid, @username, @display_name, @email, @password_hash, @role, @branch, @department,
                @is_active, @is_locked, @must_change_password, @failed_login_attempts, @created_at, @created_by)
            RETURNING id INTO :rid";

        cmd.Parameters.AddWithValue("uuid", user.Uuid);
        cmd.Parameters.AddWithValue("username", user.Username);
        cmd.Parameters.AddWithValue("display_name", user.DisplayName);
        cmd.Parameters.AddWithValue("email", user.Email ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("password_hash", user.PasswordHash);
        cmd.Parameters.AddWithValue("role", user.Role);
        cmd.Parameters.AddWithValue("branch", user.Branch ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("department", user.Department ?? (object)DBNull.Value);
        cmd.Parameters.Add(new OracleParameter("is_active", OracleDbType.Int16) { Value = user.IsActive ? 1 : 0 });
        cmd.Parameters.Add(new OracleParameter("is_locked", OracleDbType.Int16) { Value = user.IsLocked ? 1 : 0 });
        cmd.Parameters.Add(new OracleParameter("must_change_password", OracleDbType.Int16) { Value = user.MustChangePassword ? 1 : 0 });
        cmd.Parameters.AddWithValue("failed_login_attempts", user.FailedLoginAttempts);
        cmd.Parameters.Add(new OracleParameter("created_at", OracleDbType.TimeStamp) { Value = now });
        cmd.Parameters.AddWithValue("created_by", user.CreatedBy ?? (object)DBNull.Value);
        var rid = new OracleParameter("rid", OracleDbType.Int64) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(rid);
        Prep(cmd); cmd.ExecuteNonQuery();
        var id = Convert.ToInt64(rid.Value?.ToString() ?? "0", CultureInfo.InvariantCulture);
        user.Id = (int)id;

        _log.Information("Created user: {Username} ({Role})", user.Username, user.Role);
            return id;
        }, nameof(Insert), -1L);
    }

    public bool UpdatePassword(int id, string passwordHash, string? updatedBy = null, bool? requirePasswordChangeOnNextLogin = null)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var mustChangeSql = requirePasswordChangeOnNextLogin.HasValue
            ? ", must_change_password = @must_change_password"
            : "";
        cmd.CommandText = $@"
            UPDATE users SET
                password_hash = @password_hash,
                password_changed_at = @password_changed_at,
                is_locked = 0,
                failed_login_attempts = 0,
                updated_at = @updated_at,
                updated_by = @updated_by
                {mustChangeSql}
            WHERE id = @id";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("password_hash", passwordHash);
        var now = DateTime.UtcNow;
        cmd.Parameters.Add(new OracleParameter("password_changed_at", OracleDbType.TimeStamp) { Value = now });
        cmd.Parameters.Add(new OracleParameter("updated_at", OracleDbType.TimeStamp) { Value = now });
        cmd.Parameters.AddWithValue("updated_by", updatedBy ?? (object)DBNull.Value);
        if (requirePasswordChangeOnNextLogin.HasValue)
            cmd.Parameters.Add(new OracleParameter("must_change_password", OracleDbType.Int16) { Value = requirePasswordChangeOnNextLogin.Value ? 1 : 0 });
            Prep(cmd); return cmd.ExecuteNonQuery() > 0;
        }, nameof(UpdatePassword), false);
    }

    public User? GetById(int id)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM users WHERE id = @id";
            cmd.Parameters.AddWithValue("id", id);
            Prep(cmd); using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadUser(reader) : null;
        }, nameof(GetById), null);
    }

    public User? GetByUuid(string uuid)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM users WHERE uuid = @uuid";
            cmd.Parameters.AddWithValue("uuid", uuid);
            Prep(cmd); using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadUser(reader) : null;
        }, nameof(GetByUuid), null);
    }

    public User? GetByUsername(string username)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM users WHERE LOWER(username) = LOWER(@username)";
            cmd.Parameters.AddWithValue("username", username);
            Prep(cmd); using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadUser(reader) : null;
        }, nameof(GetByUsername), null);
    }

    public List<User> ListUsers(string? role = null, bool? isActive = null, int limit = 1000)
    {
        return ExecuteDbOperation(() =>
        {
            var users = new List<User>();
        var sql = "SELECT * FROM users WHERE 1=1";
        var parameters = new List<OracleParameter>();

        if (!string.IsNullOrEmpty(role))
        {
            sql += " AND role = @role";
            parameters.Add(new OracleParameter("role", role));
        }

        if (isActive.HasValue)
        {
            sql += " AND is_active = @is_active";
            parameters.Add(new OracleParameter("is_active", isActive.Value ? 1 : 0));
        }

        sql += " ORDER BY username FETCH FIRST @limit ROWS ONLY";
        parameters.Add(new OracleParameter("limit", OracleDbType.Int32) { Value = limit });

        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.Add(p);

        Prep(cmd); using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            users.Add(ReadUser(reader));
        }

            return users;
        }, nameof(ListUsers), new List<User>());
    }

    public bool Update(User user)
    {
        return ExecuteDbOperation(() =>
        {
            var now = DateTime.UtcNow;
            user.UpdatedAt = now.ToString("O");

            using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE users SET
                display_name = @display_name,
                email = @email,
                role = @role,
                branch = @branch,
                department = @department,
                is_active = @is_active,
                is_locked = @is_locked,
                must_change_password = @must_change_password,
                failed_login_attempts = @failed_login_attempts,
                last_login_at = @last_login_at,
                last_login_ip = @last_login_ip,
                password_changed_at = @password_changed_at,
                updated_at = @updated_at,
                updated_by = @updated_by
            WHERE id = @id";

        cmd.Parameters.AddWithValue("id", user.Id);
        cmd.Parameters.AddWithValue("display_name", user.DisplayName);
        cmd.Parameters.AddWithValue("email", user.Email ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("role", user.Role);
        cmd.Parameters.AddWithValue("branch", user.Branch ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("department", user.Department ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("is_active", user.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("is_locked", user.IsLocked ? 1 : 0);
        cmd.Parameters.AddWithValue("must_change_password", user.MustChangePassword ? 1 : 0);
        cmd.Parameters.AddWithValue("failed_login_attempts", user.FailedLoginAttempts);
        cmd.Parameters.Add(new OracleParameter("last_login_at", ParseDateTimeOrNull(user.LastLoginAt) ?? (object)DBNull.Value) { OracleDbType = OracleDbType.TimeStamp });
        cmd.Parameters.AddWithValue("last_login_ip", user.LastLoginIp ?? (object)DBNull.Value);
        cmd.Parameters.Add(new OracleParameter("password_changed_at", ParseDateTimeOrNull(user.PasswordChangedAt) ?? (object)DBNull.Value) { OracleDbType = OracleDbType.TimeStamp });
        cmd.Parameters.Add(new OracleParameter("updated_at", now) { OracleDbType = OracleDbType.TimeStamp });
        cmd.Parameters.AddWithValue("updated_by", user.UpdatedBy ?? (object)DBNull.Value);
            Prep(cmd); return cmd.ExecuteNonQuery() > 0;
        }, nameof(Update), false);
    }

    public bool Delete(int id)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();

            // First invalidate all sessions
            InvalidateUserSessions(id);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM users WHERE id = @id";
            cmd.Parameters.AddWithValue("id", id);
            Prep(cmd); return cmd.ExecuteNonQuery() > 0;
        }, nameof(Delete), false);
    }

    public int Count()
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM users";
            Prep(cmd); return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }, nameof(Count), 0);
    }

    public IReadOnlyList<(int Id, string Hash)> GetUnusedEmergencyCodeHashes(int userId)
    {
        return ExecuteDbOperation(() =>
        {
            var list = new List<(int Id, string Hash)>();
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT id, code_hash FROM user_emergency_codes WHERE user_id = @p_uid AND used_at IS NULL ORDER BY id";
            cmd.Parameters.AddWithValue("p_uid", userId);
            Prep(cmd); using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add((reader.GetInt32(0), reader.GetString(1)));
            return list;
        }, nameof(GetUnusedEmergencyCodeHashes), (IReadOnlyList<(int Id, string Hash)>)Array.Empty<(int, string)>());
    }

    public bool MarkEmergencyCodeUsed(int codeId, int userId)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE user_emergency_codes
                SET used_at = @used
                WHERE id = @id AND user_id = @p_uid AND used_at IS NULL";
            cmd.Parameters.Add(new OracleParameter("used", OracleDbType.TimeStamp) { Value = DateTime.UtcNow });
            cmd.Parameters.AddWithValue("id", codeId);
            cmd.Parameters.AddWithValue("p_uid", userId);
            Prep(cmd); return cmd.ExecuteNonQuery() > 0;
        }, nameof(MarkEmergencyCodeUsed), false);
    }

    public void ReplaceEmergencyCodes(int userId, IReadOnlyList<string> codeHashes)
    {
        if (codeHashes.Count != 10)
            throw new ArgumentOutOfRangeException(nameof(codeHashes), "Exactly 10 code hashes are required.");

        ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();
            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM user_emergency_codes WHERE user_id = @p_uid";
                del.Parameters.AddWithValue("p_uid", userId);
                Prep(del); del.ExecuteNonQuery();
            }

            var createdAt = DateTime.UtcNow;
            foreach (var hash in codeHashes)
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText =
                    "INSERT INTO user_emergency_codes (user_id, code_hash, created_at, used_at) VALUES (@p_uid, @h, @c, NULL)";
                ins.Parameters.AddWithValue("p_uid", userId);
                ins.Parameters.AddWithValue("h", hash);
                ins.Parameters.Add(new OracleParameter("c", OracleDbType.TimeStamp) { Value = createdAt });
                Prep(ins); ins.ExecuteNonQuery();
            }

            tx.Commit();
        }, nameof(ReplaceEmergencyCodes));
    }

    public void CreateSession(Session session)
    {
        ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sessions (token, user_id, username, user_role, created_at, expires_at, ip_address, user_agent, is_active)
            VALUES (@token, @user_id, @username, @user_role, @created_at, @expires_at, @ip_address, @user_agent, @is_active)";

        cmd.Parameters.AddWithValue("token", session.Token);
        cmd.Parameters.AddWithValue("user_id", session.UserId);
        cmd.Parameters.AddWithValue("username", session.Username);
        cmd.Parameters.AddWithValue("user_role", session.UserRole);
        cmd.Parameters.Add(new OracleParameter("created_at", ParseDateTimeOrNull(session.CreatedAt) ?? (object)DBNull.Value) { OracleDbType = OracleDbType.TimeStamp });
        cmd.Parameters.Add(new OracleParameter("expires_at", ParseDateTimeOrNull(session.ExpiresAt) ?? (object)DBNull.Value) { OracleDbType = OracleDbType.TimeStamp });
        cmd.Parameters.AddWithValue("ip_address", session.IpAddress ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("user_agent", session.UserAgent ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("is_active", session.IsActive ? 1 : 0);
            Prep(cmd); cmd.ExecuteNonQuery();
        }, nameof(CreateSession));
    }

    public Session? GetSession(string token)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM sessions WHERE token = @token AND is_active = 1";
            cmd.Parameters.AddWithValue("token", token);
            Prep(cmd); using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadSession(reader) : null;
        }, nameof(GetSession), null);
    }

    public void InvalidateSession(string token)
    {
        ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE sessions SET is_active = 0 WHERE token = @token";
            cmd.Parameters.AddWithValue("token", token);
            Prep(cmd); cmd.ExecuteNonQuery();
        }, nameof(InvalidateSession));
    }

    public void InvalidateUserSessions(int userId)
    {
        ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE sessions SET is_active = 0 WHERE user_id = @user_id";
            cmd.Parameters.AddWithValue("user_id", userId);
            Prep(cmd); cmd.ExecuteNonQuery();
        }, nameof(InvalidateUserSessions));
    }

    public List<Session> GetUserSessions(int userId)
    {
        return ExecuteDbOperation(() =>
        {
            var sessions = new List<Session>();

            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM sessions WHERE user_id = @user_id AND is_active = 1 ORDER BY created_at DESC";
            cmd.Parameters.AddWithValue("user_id", userId);
            Prep(cmd); using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sessions.Add(ReadSession(reader));
            }

            return sessions;
        }, nameof(GetUserSessions), new List<Session>());
    }

    private static User ReadUser(OracleDataReader r)
    {
        return new User
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            Uuid = r.GetString(r.GetOrdinal("uuid")),
            Username = r.GetString(r.GetOrdinal("username")),
            DisplayName = r.GetString(r.GetOrdinal("display_name")),
            Email = GetStringOrNull(r, "email") ?? "",
            PasswordHash = r.GetString(r.GetOrdinal("password_hash")),
            Role = r.GetString(r.GetOrdinal("role")),
            Branch = GetStringOrNull(r, "branch"),
            Department = GetStringOrNull(r, "department"),
            IsActive = r.GetInt32(r.GetOrdinal("is_active")) == 1,
            IsLocked = r.GetInt32(r.GetOrdinal("is_locked")) == 1,
            MustChangePassword = GetIntOrDefault(r, "must_change_password", 0) == 1,
            FailedLoginAttempts = r.GetInt32(r.GetOrdinal("failed_login_attempts")),
            LastLoginAt = GetStringOrNull(r, "last_login_at"),
            LastLoginIp = GetStringOrNull(r, "last_login_ip"),
            PasswordChangedAt = GetStringOrNull(r, "password_changed_at"),
            CreatedAt = GetStringOrNull(r, "created_at") ?? string.Empty,
            CreatedBy = GetStringOrNull(r, "created_by"),
            UpdatedAt = GetStringOrNull(r, "updated_at"),
            UpdatedBy = GetStringOrNull(r, "updated_by")
        };
    }

    private static Session ReadSession(OracleDataReader r)
    {
        return new Session
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            Token = r.GetString(r.GetOrdinal("token")),
            UserId = r.GetInt32(r.GetOrdinal("user_id")),
            Username = r.GetString(r.GetOrdinal("username")),
            UserRole = r.GetString(r.GetOrdinal("user_role")),
            CreatedAt = GetStringOrNull(r, "created_at") ?? string.Empty,
            ExpiresAt = GetStringOrNull(r, "expires_at") ?? string.Empty,
            IpAddress = GetStringOrNull(r, "ip_address"),
            UserAgent = GetStringOrNull(r, "user_agent"),
            IsActive = r.GetInt32(r.GetOrdinal("is_active")) == 1
        };
    }

    private static string? GetStringOrNull(OracleDataReader r, string column)
    {
        var ordinal = r.GetOrdinal(column);
        if (r.IsDBNull(ordinal))
            return null;

        try
        {
            return r.GetDateTime(ordinal).ToString("O");
        }
        catch
        {
            return r.GetString(ordinal);
        }
    }

    private static DateTime? ParseDateTimeOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (DateTime.TryParse(value, out var dt))
            return dt;
        return null;
    }

    private static int GetIntOrDefault(OracleDataReader r, string column, int defaultValue)
    {
        try
        {
            var ordinal = r.GetOrdinal(column);
            return r.IsDBNull(ordinal) ? defaultValue : r.GetInt32(ordinal);
        }
        catch
        {
            return defaultValue;
        }
    }
}
