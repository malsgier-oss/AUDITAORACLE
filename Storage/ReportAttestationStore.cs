using Oracle.ManagedDataAccess.Client;
using Serilog;
using System.Data;
using WorkAudit.Core.Common;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage;

/// <summary>
/// Storage for report attestations (digital sign-offs, approval workflow).
/// </summary>
public interface IReportAttestationStore
{
    long Insert(ReportAttestation a);
    void Update(ReportAttestation a);
    /// <summary>Gets an attestation by row id with explicit error details on failure.</summary>
    Result<ReportAttestation> GetResult(long id);
    ReportAttestation? Get(long id);
    ReportAttestation? GetByReportPath(string reportPath);
    List<ReportAttestation> List(string? reportType = null, string? status = null, DateTime? from = null, DateTime? to = null, int limit = 100);
}

public class ReportAttestationStore : IReportAttestationStore
{
    private readonly ILogger _log = LoggingService.ForContext<ReportAttestationStore>();
    private readonly string _connectionString;
    private static void Prep(OracleCommand cmd)
    {
        cmd.BindByName = true;
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
    }

    public ReportAttestationStore(AppConfiguration config)
    {
        _connectionString = config.OracleConnectionString;
    }

    public long Insert(ReportAttestation a)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO report_attestations (uuid, report_type, report_path, date_from, date_to, branch, section,
                sha256_hash, status, generated_at, generated_by_user_id, generated_by_username,
                reviewed_at, reviewed_by_user_id, reviewed_by_username,
                approved_at, approved_by_user_id, approved_by_username, notes)
            VALUES (@uuid, @report_type, @report_path, @date_from, @date_to, @branch, @section,
                @sha256_hash, @status, @generated_at, @generated_by_user_id, @generated_by_username,
                @reviewed_at, @reviewed_by_user_id, @reviewed_by_username,
                @approved_at, @approved_by_user_id, @approved_by_username, @notes)";
        cmd.Parameters.AddWithValue("@uuid", a.Uuid);
        cmd.Parameters.AddWithValue("@report_type", a.ReportType);
        cmd.Parameters.AddWithValue("@report_path", a.ReportPath);
        cmd.Parameters.AddWithValue("@date_from", a.DateFrom);
        cmd.Parameters.AddWithValue("@date_to", a.DateTo);
        cmd.Parameters.AddWithValue("@branch", a.Branch ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@section", a.Section ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@sha256_hash", a.Sha256Hash);
        cmd.Parameters.AddWithValue("@status", a.Status);
        cmd.Parameters.AddWithValue("@generated_at", a.GeneratedAt);
        cmd.Parameters.AddWithValue("@generated_by_user_id", a.GeneratedByUserId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@generated_by_username", a.GeneratedByUsername ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@reviewed_at", a.ReviewedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@reviewed_by_user_id", a.ReviewedByUserId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@reviewed_by_username", a.ReviewedByUsername ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@approved_at", a.ApprovedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@approved_by_user_id", a.ApprovedByUserId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@approved_by_username", a.ApprovedByUsername ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", a.Notes ?? (object)DBNull.Value);
        var idParam = new OracleParameter("rid", OracleDbType.Int64, ParameterDirection.Output);
        cmd.Parameters.Add(idParam);
        cmd.CommandText += " RETURNING id INTO @rid";
        Prep(cmd);
        cmd.ExecuteNonQuery();
        return Convert.ToInt64(idParam.Value);
    }

    public void Update(ReportAttestation a)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE report_attestations SET
                sha256_hash = @sha256_hash, status = @status,
                reviewed_at = @reviewed_at, reviewed_by_user_id = @reviewed_by_user_id, reviewed_by_username = @reviewed_by_username,
                approved_at = @approved_at, approved_by_user_id = @approved_by_user_id, approved_by_username = @approved_by_username,
                notes = @notes
            WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", a.Id);
        cmd.Parameters.AddWithValue("@sha256_hash", a.Sha256Hash);
        cmd.Parameters.AddWithValue("@status", a.Status);
        cmd.Parameters.AddWithValue("@reviewed_at", a.ReviewedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@reviewed_by_user_id", a.ReviewedByUserId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@reviewed_by_username", a.ReviewedByUsername ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@approved_at", a.ApprovedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@approved_by_user_id", a.ApprovedByUserId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@approved_by_username", a.ApprovedByUsername ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", a.Notes ?? (object)DBNull.Value);
        Prep(cmd);
        cmd.ExecuteNonQuery();
    }

    public Result<ReportAttestation> GetResult(long id)
    {
        try
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM report_attestations WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            Prep(cmd); using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return Result<ReportAttestation>.Success(ReadAttestation(reader));
            return Result<ReportAttestation>.Failure($"Report attestation with ID {id} not found");
        }
        catch (OracleException ex)
        {
            _log.Error(ex, "Database error getting report attestation {Id}: {Message}", id, ex.Message);
            return Result<ReportAttestation>.Failure($"Database error: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error getting report attestation {Id}: {Message}", id, ex.Message);
            return Result<ReportAttestation>.Failure($"Unexpected error: {ex.Message}", ex);
        }
    }

    public ReportAttestation? Get(long id)
    {
        var result = GetResult(id);
        return result.IsSuccess ? result.Value : null;
    }

    public ReportAttestation? GetByReportPath(string reportPath)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM report_attestations WHERE report_path = @path ORDER BY id DESC FETCH FIRST 1 ROWS ONLY";
        cmd.Parameters.AddWithValue("@path", reportPath);
        Prep(cmd); using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadAttestation(reader) : null;
    }

    public List<ReportAttestation> List(string? reportType = null, string? status = null, DateTime? from = null, DateTime? to = null, int limit = 100)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        var sql = "SELECT * FROM report_attestations WHERE 1=1";
        if (!string.IsNullOrEmpty(reportType)) sql += " AND report_type = @report_type";
        if (!string.IsNullOrEmpty(status)) sql += " AND status = @status";
        if (from.HasValue) sql += " AND generated_at >= @p_from";
        if (to.HasValue) sql += " AND generated_at <= @p_to";
        sql += " ORDER BY id DESC FETCH FIRST @limit ROWS ONLY";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (!string.IsNullOrEmpty(reportType)) cmd.Parameters.AddWithValue("@report_type", reportType);
        if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue("@status", status);
        if (from.HasValue) cmd.Parameters.AddWithValue("@p_from", from.Value.ToString("O"));
        if (to.HasValue) cmd.Parameters.AddWithValue("@p_to", to.Value.ToString("O"));
        cmd.Parameters.AddWithValue("@limit", limit);

        var list = new List<ReportAttestation>();
        Prep(cmd); using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadAttestation(reader));
        return list;
    }

    private static ReportAttestation ReadAttestation(OracleDataReader r)
    {
        return new ReportAttestation
        {
            Id = r.GetInt64(r.GetOrdinal("id")),
            Uuid = r.GetString(r.GetOrdinal("uuid")),
            ReportType = r.GetString(r.GetOrdinal("report_type")),
            ReportPath = r.GetString(r.GetOrdinal("report_path")),
            DateFrom = r.GetString(r.GetOrdinal("date_from")),
            DateTo = r.GetString(r.GetOrdinal("date_to")),
            Branch = r.IsDBNull(r.GetOrdinal("branch")) ? null : r.GetString(r.GetOrdinal("branch")),
            Section = r.IsDBNull(r.GetOrdinal("section")) ? null : r.GetString(r.GetOrdinal("section")),
            Sha256Hash = r.GetString(r.GetOrdinal("sha256_hash")),
            Status = r.GetString(r.GetOrdinal("status")),
            GeneratedAt = r.GetString(r.GetOrdinal("generated_at")),
            GeneratedByUserId = r.IsDBNull(r.GetOrdinal("generated_by_user_id")) ? null : r.GetString(r.GetOrdinal("generated_by_user_id")),
            GeneratedByUsername = r.IsDBNull(r.GetOrdinal("generated_by_username")) ? null : r.GetString(r.GetOrdinal("generated_by_username")),
            ReviewedAt = r.IsDBNull(r.GetOrdinal("reviewed_at")) ? null : r.GetString(r.GetOrdinal("reviewed_at")),
            ReviewedByUserId = r.IsDBNull(r.GetOrdinal("reviewed_by_user_id")) ? null : r.GetString(r.GetOrdinal("reviewed_by_user_id")),
            ReviewedByUsername = r.IsDBNull(r.GetOrdinal("reviewed_by_username")) ? null : r.GetString(r.GetOrdinal("reviewed_by_username")),
            ApprovedAt = r.IsDBNull(r.GetOrdinal("approved_at")) ? null : r.GetString(r.GetOrdinal("approved_at")),
            ApprovedByUserId = r.IsDBNull(r.GetOrdinal("approved_by_user_id")) ? null : r.GetString(r.GetOrdinal("approved_by_user_id")),
            ApprovedByUsername = r.IsDBNull(r.GetOrdinal("approved_by_username")) ? null : r.GetString(r.GetOrdinal("approved_by_username")),
            Notes = r.IsDBNull(r.GetOrdinal("notes")) ? null : r.GetString(r.GetOrdinal("notes"))
        };
    }
}
