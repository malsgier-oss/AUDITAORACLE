using Oracle.ManagedDataAccess.Client;
using Serilog;
using System.Globalization;
using System.Data;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage;

/// <summary>
/// Storage for report distribution/access tracking.
/// </summary>
public interface IReportDistributionStore
{
    long Log(string reportPath, string reportType, string eventType, string userId, string username, string? details = null);
    List<ReportDistribution> List(string? reportPath = null, string? userId = null, DateTime? from = null, DateTime? to = null, int limit = 500);
}

public class ReportDistributionStore : IReportDistributionStore
{
    private readonly ILogger _log = LoggingService.ForContext<ReportDistributionStore>();
    private readonly string _connectionString;
    private static void Prep(OracleCommand cmd)
    {
        cmd.BindByName = true;
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
    }

    public ReportDistributionStore(AppConfiguration config)
    {
        _connectionString = config.OracleConnectionString;
    }

    public long Log(string reportPath, string reportType, string eventType, string userId, string username, string? details = null)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO report_distributions (uuid, report_path, report_type, event_type, user_id, username, event_time, details)
            VALUES (@uuid, @report_path, @report_type, @event_type, @user_id, @username, @event_time, @details)";
        cmd.Parameters.AddWithValue("@uuid", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("@report_path", reportPath);
        cmd.Parameters.AddWithValue("@report_type", reportType);
        cmd.Parameters.AddWithValue("@event_type", eventType);
        cmd.Parameters.AddWithValue("@user_id", userId);
        cmd.Parameters.AddWithValue("@username", username);
        cmd.Parameters.Add(new OracleParameter("event_time", OracleDbType.TimeStamp) { Value = DateTime.UtcNow });
        cmd.Parameters.AddWithValue("@details", details ?? (object)DBNull.Value);
        var idParam = new OracleParameter("rid", OracleDbType.Int64, ParameterDirection.Output);
        cmd.Parameters.Add(idParam);
        cmd.CommandText += " RETURNING id INTO @rid";
        Prep(cmd);
        cmd.ExecuteNonQuery();
        return Convert.ToInt64(idParam.Value, CultureInfo.InvariantCulture);
    }

    public List<ReportDistribution> List(string? reportPath = null, string? userId = null, DateTime? from = null, DateTime? to = null, int limit = 500)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        var sql = "SELECT * FROM report_distributions WHERE 1=1";
        if (!string.IsNullOrEmpty(reportPath)) sql += " AND report_path = @report_path";
        if (!string.IsNullOrEmpty(userId)) sql += " AND user_id = @user_id";
        if (from.HasValue) sql += " AND event_time >= @p_from";
        if (to.HasValue) sql += " AND event_time <= @p_to";
        sql += " ORDER BY id DESC FETCH FIRST @limit ROWS ONLY";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (!string.IsNullOrEmpty(reportPath)) cmd.Parameters.AddWithValue("@report_path", reportPath);
        if (!string.IsNullOrEmpty(userId)) cmd.Parameters.AddWithValue("@user_id", userId);
        if (from.HasValue) cmd.Parameters.Add(new OracleParameter("p_from", OracleDbType.TimeStamp) { Value = from.Value });
        if (to.HasValue) cmd.Parameters.Add(new OracleParameter("p_to", OracleDbType.TimeStamp) { Value = to.Value });
        cmd.Parameters.AddWithValue("@limit", limit);

        var list = new List<ReportDistribution>();
        Prep(cmd); using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadDistribution(reader));
        return list;
    }

    private static ReportDistribution ReadDistribution(OracleDataReader r)
    {
        return new ReportDistribution
        {
            Id = r.GetInt64(r.GetOrdinal("id")),
            Uuid = r.GetString(r.GetOrdinal("uuid")),
            ReportPath = r.GetString(r.GetOrdinal("report_path")),
            ReportType = r.GetString(r.GetOrdinal("report_type")),
            EventType = r.GetString(r.GetOrdinal("event_type")),
            UserId = r.GetString(r.GetOrdinal("user_id")),
            Username = r.GetString(r.GetOrdinal("username")),
            Timestamp = r.GetDateTime(r.GetOrdinal("event_time")).ToString("O"),
            Details = r.IsDBNull(r.GetOrdinal("details")) ? null : r.GetString(r.GetOrdinal("details"))
        };
    }
}
