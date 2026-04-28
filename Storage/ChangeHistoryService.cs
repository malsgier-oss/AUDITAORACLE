using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage;

/// <summary>
/// Tracks field-level document modifications for audit and compliance.
/// </summary>
public interface IChangeHistoryService
{
    void RecordFieldChange(string documentUuid, int documentId, string fieldName, string? oldValue, string? newValue);
    IReadOnlyList<DocumentChangeRecord> GetDocumentHistory(string documentUuid, int limit = 100);
}

public class DocumentChangeRecord
{
    public string FieldName { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string ChangedAt { get; set; } = "";
    public string? ChangedBy { get; set; }
}

public class ChangeHistoryService : IChangeHistoryService
{
    private readonly ILogger _log = LoggingService.ForContext<ChangeHistoryService>();
    private readonly string _connectionString;
    private readonly Func<ISessionService?> _sessionFactory;
    private static void Prep(OracleCommand cmd)
    {
        cmd.BindByName = true;
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
    }

    public ChangeHistoryService(string dbPath, Func<ISessionService?> sessionFactory)
    {
        _connectionString = dbPath;
        _sessionFactory = sessionFactory ?? (() => null);
    }

    public void RecordFieldChange(string documentUuid, int documentId, string fieldName, string? oldValue, string? newValue)
    {
        if (string.Equals(oldValue ?? "", newValue ?? "", StringComparison.Ordinal))
            return;

        var changedBy = _sessionFactory()?.CurrentUser?.Username ?? "system";

        try
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO document_change_history (document_uuid, document_id, field_name, old_value, new_value, changed_at, changed_by)
                VALUES (@uuid, @docId, @field, @old, @new, @at, @by)";
            cmd.Parameters.AddWithValue("@uuid", documentUuid);
            cmd.Parameters.AddWithValue("@docId", documentId);
            cmd.Parameters.AddWithValue("@field", fieldName);
            cmd.Parameters.AddWithValue("@old", oldValue ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@new", newValue ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@by", changedBy);
            Prep(cmd);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to record change history: {Field} on {Doc}", fieldName, documentUuid);
        }
    }

    public IReadOnlyList<DocumentChangeRecord> GetDocumentHistory(string documentUuid, int limit = 100)
    {
        var list = new List<DocumentChangeRecord>();
        try
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT field_name, old_value, new_value, changed_at, changed_by FROM document_change_history WHERE document_uuid = @uuid ORDER BY changed_at DESC FETCH FIRST @limit ROWS ONLY";
            cmd.Parameters.AddWithValue("@uuid", documentUuid);
            cmd.Parameters.AddWithValue("@limit", limit);
            Prep(cmd); using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new DocumentChangeRecord
                {
                    FieldName = r.GetString(0),
                    OldValue = r.IsDBNull(1) ? null : r.GetString(1),
                    NewValue = r.IsDBNull(2) ? null : r.GetString(2),
                    ChangedAt = r.GetString(3),
                    ChangedBy = r.IsDBNull(4) ? null : r.GetString(4)
                });
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to get change history: {Doc}", documentUuid);
        }
        return list;
    }
}
