using Oracle.ManagedDataAccess.Client;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage;

public sealed class UserAuditorUiPreferencesStore : IUserAuditorUiPreferencesStore
{
    private readonly string _connectionString;

    public UserAuditorUiPreferencesStore(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public string? TryGetPreferencesJson(string userUuid, string role)
    {
        if (string.IsNullOrWhiteSpace(userUuid) || string.IsNullOrWhiteSpace(role))
            return null;

        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = OracleSql.CreateCommand(conn,
            """
            SELECT preferences_json FROM user_auditor_ui_preferences
            WHERE user_uuid = @p_uuid AND role = @p_role
            """);
        OracleSql.AddParameter(cmd, "p_uuid", userUuid.Trim());
        OracleSql.AddParameter(cmd, "p_role", role.Trim());

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var ord = reader.GetOrdinal("preferences_json");
        if (reader.IsDBNull(ord)) return null;
        return reader.GetString(ord);
    }

    public void UpsertPreferencesJson(string userUuid, string role, string preferencesJson)
    {
        if (string.IsNullOrWhiteSpace(userUuid)) throw new ArgumentException("userUuid is required.", nameof(userUuid));
        if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("role is required.", nameof(role));
        ArgumentNullException.ThrowIfNull(preferencesJson);

        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = OracleSql.CreateCommand(conn,
            """
            MERGE INTO user_auditor_ui_preferences t
            USING (SELECT @p_uuid AS u, @p_role AS r, @p_json AS j, @p_ts AS ts FROM dual) s
            ON (t.user_uuid = s.u AND t.role = s.r)
            WHEN MATCHED THEN
              UPDATE SET t.preferences_json = s.j, t.updated_at = s.ts
            WHEN NOT MATCHED THEN
              INSERT (user_uuid, role, preferences_json, updated_at)
              VALUES (s.u, s.r, s.j, s.ts)
            """);
        OracleSql.AddParameter(cmd, "p_uuid", userUuid.Trim());
        OracleSql.AddParameter(cmd, "p_role", role.Trim());
        var clobParam = new OracleParameter("p_json", OracleDbType.Clob)
        {
            Value = preferencesJson
        };
        cmd.Parameters.Add(clobParam);
        OracleSql.AddParameter(cmd, "p_ts", DateTime.UtcNow);
        cmd.ExecuteNonQuery();
    }
}
