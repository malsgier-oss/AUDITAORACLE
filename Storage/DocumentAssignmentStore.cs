using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Core.Common;
using WorkAudit.Core.Services;
using WorkAudit.Domain;

namespace WorkAudit.Storage;

/// <summary>
/// Storage for document assignments. P4: Document Assignment System.
/// </summary>
public interface IDocumentAssignmentStore
{
    int Insert(DocumentAssignment a);
    /// <summary>Gets an assignment by ID with explicit error details on failure.</summary>
    Result<DocumentAssignment> GetResult(int id);
    DocumentAssignment? Get(int id);
    DocumentAssignment? GetByUuid(string uuid);
    List<DocumentAssignment> ListByUser(int userId, string? status = null, bool overdueOnly = false, int limit = 500);
    List<DocumentAssignment> ListByDocument(int documentId);
    List<DocumentAssignment> ListAll(string? assignedToUsername = null, string? status = null, int limit = 500);
    bool UpdateStatus(int id, string status, string? startedAt = null, string? completedAt = null, string? completionNotes = null);
    bool UpdateAssignedTo(int id, int newUserId, string newUsername);
    bool Cancel(int id);
}

public class DocumentAssignmentStore : IDocumentAssignmentStore
{
    private readonly ILogger _log = LoggingService.ForContext<DocumentAssignmentStore>();
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

    public DocumentAssignmentStore(AppConfiguration config)
    {
        _connectionString = config.OracleConnectionString;
    }

    public int Insert(DocumentAssignment a)
    {
        return ExecuteDbOperation(() =>
        {
            a.AssignedAt = DateTime.UtcNow.ToString("O");
            a.Uuid = Guid.NewGuid().ToString();

            using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO document_assignments (uuid, document_id, document_uuid, assigned_to_user_id, assigned_to_username,
                assigned_by_user_id, assigned_by_username, assigned_at, due_date, priority, status, notes)
            VALUES (@uuid, @doc_id, @doc_uuid, @to_id, @to_username, @by_id, @by_username, @assigned_at,
                @due_date, @priority, @status, @notes)";
        cmd.Parameters.AddWithValue("@uuid", a.Uuid);
        cmd.Parameters.AddWithValue("@doc_id", a.DocumentId);
        cmd.Parameters.AddWithValue("@doc_uuid", a.DocumentUuid);
        cmd.Parameters.AddWithValue("@to_id", a.AssignedToUserId);
        cmd.Parameters.AddWithValue("@to_username", a.AssignedToUsername);
        cmd.Parameters.AddWithValue("@by_id", a.AssignedByUserId);
        cmd.Parameters.AddWithValue("@by_username", a.AssignedByUsername);
        cmd.Parameters.AddWithValue("@assigned_at", a.AssignedAt);
        cmd.Parameters.AddWithValue("@due_date", a.DueDate ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@priority", a.Priority);
        cmd.Parameters.AddWithValue("@status", a.Status);
        cmd.Parameters.AddWithValue("@notes", a.Notes ?? (object)DBNull.Value);

        cmd.ExecuteNonQuery();

        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
            a.Id = Convert.ToInt32(idCmd.ExecuteScalar());
            return a.Id;
        }, nameof(Insert), 0);
    }

    public Result<DocumentAssignment> GetResult(int id)
    {
        try
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM document_assignments WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            if (r.Read())
                return Result<DocumentAssignment>.Success(ReadRow(r));
            return Result<DocumentAssignment>.Failure($"Assignment with ID {id} not found");
        }
        catch (OracleException ex)
        {
            _log.Error(ex, "Database error getting assignment {Id}: {Message}", id, ex.Message);
            return Result<DocumentAssignment>.Failure($"Database error: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error getting assignment {Id}: {Message}", id, ex.Message);
            return Result<DocumentAssignment>.Failure($"Unexpected error: {ex.Message}", ex);
        }
    }

    public DocumentAssignment? Get(int id)
    {
        var result = GetResult(id);
        return result.IsSuccess ? result.Value : null;
    }

    public DocumentAssignment? GetByUuid(string uuid)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM document_assignments WHERE uuid = @uuid";
            cmd.Parameters.AddWithValue("@uuid", uuid);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadRow(r) : null;
        }, nameof(GetByUuid), null);
    }

    public List<DocumentAssignment> ListByUser(int userId, string? status = null, bool overdueOnly = false, int limit = 500)
    {
        return ExecuteDbOperation(() =>
        {
            var list = new List<DocumentAssignment>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        var sql = "SELECT * FROM document_assignments WHERE assigned_to_user_id = @uid AND status != @cancelled";
        if (!string.IsNullOrEmpty(status)) sql += " AND status = @status";
        if (overdueOnly) sql += " AND due_date IS NOT NULL AND due_date < @now AND status IN (@pending, @inprogress)";
        sql += " ORDER BY CASE WHEN due_date IS NULL THEN 1 ELSE 0 END, due_date ASC LIMIT @limit";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@cancelled", AssignmentStatus.Cancelled);
        if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue("@status", status);
        if (overdueOnly) cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("yyyy-MM-dd"));
        if (overdueOnly) { cmd.Parameters.AddWithValue("@pending", AssignmentStatus.Pending); cmd.Parameters.AddWithValue("@inprogress", AssignmentStatus.InProgress); }
        cmd.Parameters.AddWithValue("@limit", limit);

        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadRow(r));
            return list;
        }, nameof(ListByUser), new List<DocumentAssignment>());
    }

    public List<DocumentAssignment> ListByDocument(int documentId)
    {
        return ExecuteDbOperation(() =>
        {
            var list = new List<DocumentAssignment>();
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM document_assignments WHERE document_id = @doc_id AND status != @cancelled ORDER BY assigned_at DESC";
            cmd.Parameters.AddWithValue("@doc_id", documentId);
            cmd.Parameters.AddWithValue("@cancelled", AssignmentStatus.Cancelled);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadRow(r));
            return list;
        }, nameof(ListByDocument), new List<DocumentAssignment>());
    }

    public List<DocumentAssignment> ListAll(string? assignedToUsername = null, string? status = null, int limit = 500)
    {
        return ExecuteDbOperation(() =>
        {
            var list = new List<DocumentAssignment>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        var sql = "SELECT * FROM document_assignments WHERE 1=1";
        if (!string.IsNullOrEmpty(assignedToUsername)) sql += " AND assigned_to_username = @username";
        if (!string.IsNullOrEmpty(status)) sql += " AND status = @status";
        sql += " ORDER BY assigned_at DESC LIMIT @limit";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (!string.IsNullOrEmpty(assignedToUsername)) cmd.Parameters.AddWithValue("@username", assignedToUsername);
        if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadRow(r));
            return list;
        }, nameof(ListAll), new List<DocumentAssignment>());
    }

    public bool UpdateStatus(int id, string status, string? startedAt = null, string? completedAt = null, string? completionNotes = null)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            var parts = new List<string> { "status = @status" };
            if (startedAt != null) parts.Add("started_at = @started_at");
            if (completedAt != null) parts.Add("completed_at = @completed_at");
            if (completionNotes != null) parts.Add("completion_notes = @completion_notes");

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE document_assignments SET {string.Join(", ", parts)} WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@status", status);
            if (startedAt != null) cmd.Parameters.AddWithValue("@started_at", startedAt);
            if (completedAt != null) cmd.Parameters.AddWithValue("@completed_at", completedAt);
            if (completionNotes != null) cmd.Parameters.AddWithValue("@completion_notes", completionNotes);
            return cmd.ExecuteNonQuery() > 0;
        }, nameof(UpdateStatus), false);
    }

    public bool UpdateAssignedTo(int id, int newUserId, string newUsername)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE document_assignments SET assigned_to_user_id = @uid, assigned_to_username = @username WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@uid", newUserId);
            cmd.Parameters.AddWithValue("@username", newUsername);
            return cmd.ExecuteNonQuery() > 0;
        }, nameof(UpdateAssignedTo), false);
    }

    public bool Cancel(int id)
    {
        return UpdateStatus(id, AssignmentStatus.Cancelled);
    }

    private static DocumentAssignment ReadRow(OracleDataReader r)
    {
        return new DocumentAssignment
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            Uuid = r.GetString(r.GetOrdinal("uuid")),
            DocumentId = r.GetInt32(r.GetOrdinal("document_id")),
            DocumentUuid = r.GetString(r.GetOrdinal("document_uuid")),
            AssignedToUserId = r.GetInt32(r.GetOrdinal("assigned_to_user_id")),
            AssignedToUsername = r.GetString(r.GetOrdinal("assigned_to_username")),
            AssignedByUserId = r.GetInt32(r.GetOrdinal("assigned_by_user_id")),
            AssignedByUsername = r.GetString(r.GetOrdinal("assigned_by_username")),
            AssignedAt = r.GetString(r.GetOrdinal("assigned_at")),
            DueDate = r.IsDBNull(r.GetOrdinal("due_date")) ? null : r.GetString(r.GetOrdinal("due_date")),
            Priority = r.GetString(r.GetOrdinal("priority")),
            Status = r.GetString(r.GetOrdinal("status")),
            Notes = r.IsDBNull(r.GetOrdinal("notes")) ? null : r.GetString(r.GetOrdinal("notes")),
            StartedAt = r.IsDBNull(r.GetOrdinal("started_at")) ? null : r.GetString(r.GetOrdinal("started_at")),
            CompletedAt = r.IsDBNull(r.GetOrdinal("completed_at")) ? null : r.GetString(r.GetOrdinal("completed_at")),
            CompletionNotes = r.IsDBNull(r.GetOrdinal("completion_notes")) ? null : r.GetString(r.GetOrdinal("completion_notes"))
        };
    }
}
