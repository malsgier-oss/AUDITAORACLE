using Oracle.ManagedDataAccess.Client;
using Serilog;
using System.Data;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage;

/// <summary>
/// Storage for report generation history.
/// </summary>
public interface IReportHistoryStore
{
    int Insert(ReportHistory entry);
    List<ReportHistory> List(DateTime? from = null, DateTime? to = null, int limit = 50);
}

public class ReportHistoryStore : IReportHistoryStore
{
    private readonly ILogger _log = LoggingService.ForContext<ReportHistoryStore>();
    private readonly string _connectionString;
    private static void Prep(OracleCommand cmd)
    {
        cmd.BindByName = true;
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
    }

    public ReportHistoryStore(AppConfiguration config)
    {
        _connectionString = config.OracleConnectionString;
    }

    public int Insert(ReportHistory entry)
    {
        entry.Uuid = Guid.NewGuid().ToString();
        entry.GeneratedAt = DateTime.UtcNow.ToString("O");

        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO report_history (uuid, user_id, username, report_type, file_path, generated_at, config_json, 
                tags, purpose, description, version, parent_report_id, app_version)
            VALUES (@uuid, @user_id, @username, @report_type, @file_path, @generated_at, @config_json,
                @tags, @purpose, @description, @version, @parent_report_id, @app_version)";
        cmd.Parameters.AddWithValue("@uuid", entry.Uuid);
        cmd.Parameters.AddWithValue("@user_id", entry.UserId ?? "");
        cmd.Parameters.AddWithValue("@username", entry.Username ?? "");
        cmd.Parameters.AddWithValue("@report_type", entry.ReportType);
        cmd.Parameters.AddWithValue("@file_path", entry.FilePath);
        cmd.Parameters.AddWithValue("@generated_at", entry.GeneratedAt);
        cmd.Parameters.AddWithValue("@config_json", entry.ConfigJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@tags", entry.Tags ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@purpose", entry.Purpose ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@description", entry.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@version", entry.Version ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@parent_report_id", entry.ParentReportId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@app_version", entry.AppVersion ?? (object)DBNull.Value);
        var idParam = new OracleParameter("rid", OracleDbType.Int32, ParameterDirection.Output);
        cmd.Parameters.Add(idParam);
        cmd.CommandText += " RETURNING id INTO @rid";
        Prep(cmd);
        cmd.ExecuteNonQuery();
        entry.Id = Convert.ToInt32(idParam.Value);
        return entry.Id;
    }

    public List<ReportHistory> List(DateTime? from = null, DateTime? to = null, int limit = 50)
    {
        var list = new List<ReportHistory>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        var sql = "SELECT * FROM report_history WHERE 1=1";
        if (from.HasValue) sql += " AND generated_at >= @p_from";
        if (to.HasValue) sql += " AND generated_at <= @p_to";
        sql += " ORDER BY generated_at DESC FETCH FIRST @limit ROWS ONLY";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (from.HasValue) cmd.Parameters.AddWithValue("@p_from", from.Value.ToUniversalTime().ToString("O"));
        if (to.HasValue) cmd.Parameters.AddWithValue("@p_to", to.Value.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@limit", limit);

        Prep(cmd); using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadRow(r));
        return list;
    }

    private static ReportHistory ReadRow(OracleDataReader r)
    {
        return new ReportHistory
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            Uuid = r.GetString(r.GetOrdinal("uuid")),
            UserId = r.GetString(r.GetOrdinal("user_id")),
            Username = r.GetString(r.GetOrdinal("username")),
            ReportType = r.GetString(r.GetOrdinal("report_type")),
            FilePath = r.GetString(r.GetOrdinal("file_path")),
            GeneratedAt = r.GetString(r.GetOrdinal("generated_at")),
            ConfigJson = r.IsDBNull(r.GetOrdinal("config_json")) ? null : r.GetString(r.GetOrdinal("config_json")),
            Tags = GetStringOrNull(r, "tags"),
            Purpose = GetStringOrNull(r, "purpose"),
            Description = GetStringOrNull(r, "description"),
            Version = GetIntOrNull(r, "version"),
            ParentReportId = GetStringOrNull(r, "parent_report_id"),
            AppVersion = GetStringOrNull(r, "app_version")
        };
    }

    private static string? GetStringOrNull(OracleDataReader r, string columnName)
    {
        try
        {
            var ordinal = r.GetOrdinal(columnName);
            return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
        }
        catch
        {
            return null; // Column doesn't exist yet (pre-migration)
        }
    }

    private static int? GetIntOrNull(OracleDataReader r, string columnName)
    {
        try
        {
            var ordinal = r.GetOrdinal(columnName);
            return r.IsDBNull(ordinal) ? null : r.GetInt32(ordinal);
        }
        catch
        {
            return null; // Column doesn't exist yet (pre-migration)
        }
    }
}
