using Oracle.ManagedDataAccess.Client;
using System.Data;
using WorkAudit.Domain;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage;

public interface IReportDraftStore
{
    int Insert(ReportDraft draft);
    void Update(ReportDraft draft);
    void Delete(int id);
    ReportDraft? Get(int id);
    ReportDraft? GetByUuid(string uuid);
    List<ReportDraft> GetByUserId(string userId);
    List<ReportDraft> GetAll();
    List<ReportDraft> GetUnfinalized();
}

public class ReportDraftStore : IReportDraftStore
{
    private readonly string _connectionString;
    private static void Prep(OracleCommand cmd)
    {
        cmd.BindByName = true;
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
    }

    public ReportDraftStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public int Insert(ReportDraft draft)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        
        var sql = @"
            INSERT INTO report_drafts 
            (uuid, user_id, username, report_type, created_at, last_modified_at, config_json, 
             draft_file_path, title, notes, tags, is_finalized, exported_report_history_id)
            VALUES 
            (@uuid, @userId, @username, @reportType, @createdAt, @lastModifiedAt, @configJson,
             @draftFilePath, @title, @notes, @tags, @isFinalized, @exportedReportHistoryId)";
        
        using var cmd = new OracleCommand(sql, conn);
        cmd.Parameters.AddWithValue("@uuid", draft.Uuid);
        cmd.Parameters.AddWithValue("@userId", draft.UserId);
        cmd.Parameters.AddWithValue("@username", draft.Username);
        cmd.Parameters.AddWithValue("@reportType", draft.ReportType);
        cmd.Parameters.AddWithValue("@createdAt", draft.CreatedAt);
        cmd.Parameters.AddWithValue("@lastModifiedAt", draft.LastModifiedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@configJson", draft.ConfigJson);
        cmd.Parameters.AddWithValue("@draftFilePath", draft.DraftFilePath);
        cmd.Parameters.AddWithValue("@title", draft.Title ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", draft.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@tags", draft.Tags ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@isFinalized", draft.IsFinalized ? 1 : 0);
        cmd.Parameters.AddWithValue("@exportedReportHistoryId", draft.ExportedReportHistoryId ?? (object)DBNull.Value);
        var idParam = new OracleParameter("rid", OracleDbType.Int32, ParameterDirection.Output);
        cmd.Parameters.Add(idParam);
        cmd.CommandText += " RETURNING id INTO @rid";

        Prep(cmd);
        cmd.ExecuteNonQuery();
        return Convert.ToInt32(idParam.Value);
    }

    public void Update(ReportDraft draft)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        
        var sql = @"
            UPDATE report_drafts SET
                last_modified_at = @lastModifiedAt,
                config_json = @configJson,
                draft_file_path = @draftFilePath,
                title = @title,
                notes = @notes,
                tags = @tags,
                is_finalized = @isFinalized,
                exported_report_history_id = @exportedReportHistoryId
            WHERE id = @id";
        
        using var cmd = new OracleCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", draft.Id);
        cmd.Parameters.Add(new OracleParameter("@lastModifiedAt", OracleDbType.TimeStamp) { Value = DateTime.UtcNow });
        cmd.Parameters.AddWithValue("@configJson", draft.ConfigJson);
        cmd.Parameters.AddWithValue("@draftFilePath", draft.DraftFilePath);
        cmd.Parameters.AddWithValue("@title", draft.Title ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", draft.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@tags", draft.Tags ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@isFinalized", draft.IsFinalized ? 1 : 0);
        cmd.Parameters.AddWithValue("@exportedReportHistoryId", draft.ExportedReportHistoryId ?? (object)DBNull.Value);

        Prep(cmd);
        cmd.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        
        using var cmd = new OracleCommand("DELETE FROM report_drafts WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        Prep(cmd);
        cmd.ExecuteNonQuery();
    }

    public ReportDraft? Get(int id)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        
        using var cmd = new OracleCommand("SELECT * FROM report_drafts WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        Prep(cmd);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadRow(reader) : null;
    }

    public ReportDraft? GetByUuid(string uuid)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        
        using var cmd = new OracleCommand("SELECT * FROM report_drafts WHERE uuid = @uuid", conn);
        cmd.Parameters.AddWithValue("@uuid", uuid);
        Prep(cmd);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadRow(reader) : null;
    }

    public List<ReportDraft> GetByUserId(string userId)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        
        using var cmd = new OracleCommand(
            "SELECT * FROM report_drafts WHERE user_id = @userId ORDER BY created_at DESC", conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        Prep(cmd);
        using var reader = cmd.ExecuteReader();
        var drafts = new List<ReportDraft>();
        while (reader.Read())
            drafts.Add(ReadRow(reader));
        return drafts;
    }

    public List<ReportDraft> GetAll()
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        
        using var cmd = new OracleCommand("SELECT * FROM report_drafts ORDER BY created_at DESC", conn);
        Prep(cmd);
        using var reader = cmd.ExecuteReader();
        
        var drafts = new List<ReportDraft>();
        while (reader.Read())
            drafts.Add(ReadRow(reader));
        return drafts;
    }

    public List<ReportDraft> GetUnfinalized()
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        
        using var cmd = new OracleCommand(
            "SELECT * FROM report_drafts WHERE is_finalized = 0 ORDER BY created_at DESC", conn);
        Prep(cmd);
        using var reader = cmd.ExecuteReader();
        var drafts = new List<ReportDraft>();
        while (reader.Read())
            drafts.Add(ReadRow(reader));
        return drafts;
    }

    private ReportDraft ReadRow(OracleDataReader r)
    {
        return new ReportDraft
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            Uuid = r.GetString(r.GetOrdinal("uuid")),
            UserId = r.GetString(r.GetOrdinal("user_id")),
            Username = r.GetString(r.GetOrdinal("username")),
            ReportType = r.GetString(r.GetOrdinal("report_type")),
            CreatedAt = r.GetString(r.GetOrdinal("created_at")),
            LastModifiedAt = GetStringOrNull(r, "last_modified_at"),
            ConfigJson = r.GetString(r.GetOrdinal("config_json")),
            DraftFilePath = r.GetString(r.GetOrdinal("draft_file_path")),
            Title = GetStringOrNull(r, "title"),
            Notes = GetStringOrNull(r, "notes"),
            Tags = GetStringOrNull(r, "tags"),
            IsFinalized = r.GetInt32(r.GetOrdinal("is_finalized")) == 1,
            ExportedReportHistoryId = GetStringOrNull(r, "exported_report_history_id")
        };
    }

    private static string? GetStringOrNull(OracleDataReader r, string colName)
    {
        var idx = r.GetOrdinal(colName);
        return r.IsDBNull(idx) ? null : r.GetString(idx);
    }
}
