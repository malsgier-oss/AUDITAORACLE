using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Domain;

namespace WorkAudit.Storage;

/// <summary>
/// SQLite-based storage implementation for enhanced notes system.
/// Provides CRUD operations with performance-optimized batch queries.
/// </summary>
public class NotesStore : INotesStore
{
    private readonly ILogger _log = LoggingService.ForContext<NotesStore>();
    private readonly string _connectionString;

    public NotesStore(string dbPath)
    {
        _connectionString = dbPath;
    }

    public Note Add(Note note)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO notes (uuid, document_id, document_uuid, content, type, severity, category,
                created_at, created_by, created_by_user_id, status, attachments, tags)
            VALUES (@uuid, @docId, @docUuid, @content, @type, @severity, @category,
                @createdAt, @createdBy, @createdByUserId, @status, @attachments, @tags)";

        note.Uuid = string.IsNullOrEmpty(note.Uuid) ? Guid.NewGuid().ToString() : note.Uuid;
        note.CreatedAt = DateTime.UtcNow.ToString("O");

        cmd.Parameters.AddWithValue("@uuid", note.Uuid);
        cmd.Parameters.AddWithValue("@docId", note.DocumentId);
        cmd.Parameters.AddWithValue("@docUuid", note.DocumentUuid);
        cmd.Parameters.AddWithValue("@content", note.Content);
        cmd.Parameters.AddWithValue("@type", note.Type);
        cmd.Parameters.AddWithValue("@severity", note.Severity);
        cmd.Parameters.AddWithValue("@category", note.Category ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", note.CreatedAt);
        cmd.Parameters.AddWithValue("@createdBy", note.CreatedBy);
        cmd.Parameters.AddWithValue("@createdByUserId", note.CreatedByUserId);
        cmd.Parameters.AddWithValue("@status", note.Status);
        cmd.Parameters.AddWithValue("@attachments", note.Attachments ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@tags", note.Tags ?? (object)DBNull.Value);

        cmd.ExecuteNonQuery();

        // Get the last inserted row ID
        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        note.Id = Convert.ToInt32(idCmd.ExecuteScalar());

        _log.Information("Added note {NoteId} for document {DocumentId}", note.Id, note.DocumentId);
        return note;
    }

    public Note? Get(int id)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM notes WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapNote(reader) : null;
    }

    public Note? GetByUuid(string uuid)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM notes WHERE uuid = @uuid";
        cmd.Parameters.AddWithValue("@uuid", uuid);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapNote(reader) : null;
    }

    public List<Note> GetByDocumentId(int documentId)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM notes WHERE document_id = @docId ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@docId", documentId);

        var notes = new List<Note>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            notes.Add(MapNote(reader));

        return notes;
    }

    public List<Note> GetByDocumentUuid(string documentUuid)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM notes WHERE document_uuid = @docUuid ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@docUuid", documentUuid);

        var notes = new List<Note>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            notes.Add(MapNote(reader));

        return notes;
    }

    public List<Note> List(int limit = 1000, int offset = 0)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM notes ORDER BY created_at DESC LIMIT @limit OFFSET @offset";
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        var notes = new List<Note>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            notes.Add(MapNote(reader));

        return notes;
    }

    public List<Note> Search(
        string? type = null,
        string? severity = null,
        string? status = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? createdBy = null,
        int limit = 1000)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        var where = new List<string>();
        if (!string.IsNullOrEmpty(type)) where.Add("type = @type");
        if (!string.IsNullOrEmpty(severity)) where.Add("severity = @severity");
        if (!string.IsNullOrEmpty(status)) where.Add("status = @status");
        if (fromDate.HasValue) where.Add("created_at >= @fromDate");
        if (toDate.HasValue) where.Add("created_at <= @toDate");
        if (!string.IsNullOrEmpty(createdBy)) where.Add("created_by = @createdBy");

        var sql = "SELECT * FROM notes" +
            (where.Any() ? " WHERE " + string.Join(" AND ", where) : "") +
            " ORDER BY created_at DESC LIMIT @limit";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        if (!string.IsNullOrEmpty(type)) cmd.Parameters.AddWithValue("@type", type);
        if (!string.IsNullOrEmpty(severity)) cmd.Parameters.AddWithValue("@severity", severity);
        if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue("@status", status);
        if (fromDate.HasValue) cmd.Parameters.AddWithValue("@fromDate", fromDate.Value.ToString("O"));
        if (toDate.HasValue) cmd.Parameters.AddWithValue("@toDate", toDate.Value.ToString("O"));
        if (!string.IsNullOrEmpty(createdBy)) cmd.Parameters.AddWithValue("@createdBy", createdBy);
        cmd.Parameters.AddWithValue("@limit", limit);

        var notes = new List<Note>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            notes.Add(MapNote(reader));

        return notes;
    }

    public bool Update(Note note)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        note.UpdatedAt = DateTime.UtcNow.ToString("O");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE notes SET content = @content, type = @type, severity = @severity,
                category = @category, updated_at = @updatedAt, updated_by = @updatedBy,
                status = @status, resolved_at = @resolvedAt, resolved_by = @resolvedBy,
                resolution_comment = @resolutionComment, attachments = @attachments, tags = @tags
            WHERE id = @id";

        cmd.Parameters.AddWithValue("@content", note.Content);
        cmd.Parameters.AddWithValue("@type", note.Type);
        cmd.Parameters.AddWithValue("@severity", note.Severity);
        cmd.Parameters.AddWithValue("@category", note.Category ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@updatedAt", note.UpdatedAt);
        cmd.Parameters.AddWithValue("@updatedBy", note.UpdatedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@status", note.Status);
        cmd.Parameters.AddWithValue("@resolvedAt", note.ResolvedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@resolvedBy", note.ResolvedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@resolutionComment", note.ResolutionComment ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@attachments", note.Attachments ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@tags", note.Tags ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@id", note.Id);

        var rowsAffected = cmd.ExecuteNonQuery();
        if (rowsAffected > 0)
            _log.Information("Updated note {NoteId}", note.Id);

        return rowsAffected > 0;
    }

    public bool Delete(int id)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM notes WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        var rowsAffected = cmd.ExecuteNonQuery();
        if (rowsAffected > 0)
            _log.Information("Deleted note {NoteId}", id);

        return rowsAffected > 0;
    }

    public int GetCountByDocument(int documentId)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM notes WHERE document_id = @docId";
        cmd.Parameters.AddWithValue("@docId", documentId);

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// CRITICAL PERFORMANCE OPTIMIZATION:
    /// Retrieves note counts for multiple documents in a single query.
    /// Prevents N+1 query problem when loading dashboard with 1000+ documents.
    /// </summary>
    public Dictionary<int, int> GetCountsByDocuments(List<int> documentIds)
    {
        if (!documentIds.Any()) return new Dictionary<int, int>();

        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        // Build IN clause with parameterized values for SQL injection safety
        var parameters = new List<OracleParameter>();
        var paramNames = new List<string>();

        for (int i = 0; i < documentIds.Count; i++)
        {
            var paramName = $"@id{i}";
            paramNames.Add(paramName);
            parameters.Add(new OracleParameter(paramName, documentIds[i]));
        }

        var inClause = string.Join(",", paramNames);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT document_id, COUNT(*) FROM notes WHERE document_id IN ({inClause}) GROUP BY document_id";

        foreach (var param in parameters)
            cmd.Parameters.Add(param);

        var counts = new Dictionary<int, int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            counts[reader.GetInt32(0)] = reader.GetInt32(1);

        return counts;
    }

    /// <inheritdoc />
    public Dictionary<int, List<Note>> GetIssueNotesByDocumentIds(IReadOnlyList<int> documentIds)
    {
        var ids = documentIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<int, List<Note>>();

        var byDoc = new Dictionary<int, List<Note>>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        const int chunkSize = 400;
        for (var offset = 0; offset < ids.Length; offset += chunkSize)
        {
            var count = Math.Min(chunkSize, ids.Length - offset);
            var paramNames = new string[count];
            var parameters = new OracleParameter[count];
            for (var i = 0; i < count; i++)
            {
                paramNames[i] = "@d" + i;
                parameters[i] = new OracleParameter(paramNames[i], ids[offset + i]);
            }

            var inClause = string.Join(",", paramNames);
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"SELECT * FROM notes WHERE type = @type AND document_id IN ({inClause}) ORDER BY created_at DESC";
            cmd.Parameters.AddWithValue("@type", NoteType.Issue);
            foreach (var p in parameters)
                cmd.Parameters.Add(p);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var note = MapNote(reader);
                if (!byDoc.TryGetValue(note.DocumentId, out var list))
                {
                    list = new List<Note>();
                    byDoc[note.DocumentId] = list;
                }

                list.Add(note);
            }
        }

        return byDoc;
    }

    /// <summary>
    /// Maps OracleDataReader to Note object, handling nullable columns.
    /// </summary>
    private Note MapNote(OracleDataReader reader)
    {
        return new Note
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            Uuid = reader.GetString(reader.GetOrdinal("uuid")),
            DocumentId = reader.GetInt32(reader.GetOrdinal("document_id")),
            DocumentUuid = reader.GetString(reader.GetOrdinal("document_uuid")),
            Content = reader.GetString(reader.GetOrdinal("content")),
            Type = reader.GetString(reader.GetOrdinal("type")),
            Severity = reader.GetString(reader.GetOrdinal("severity")),
            Category = reader.IsDBNull(reader.GetOrdinal("category")) ? "" : reader.GetString(reader.GetOrdinal("category")),
            CreatedAt = reader.GetString(reader.GetOrdinal("created_at")),
            CreatedBy = reader.GetString(reader.GetOrdinal("created_by")),
            CreatedByUserId = reader.GetInt32(reader.GetOrdinal("created_by_user_id")),
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("updated_at")) ? null : reader.GetString(reader.GetOrdinal("updated_at")),
            UpdatedBy = reader.IsDBNull(reader.GetOrdinal("updated_by")) ? null : reader.GetString(reader.GetOrdinal("updated_by")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            ResolvedAt = reader.IsDBNull(reader.GetOrdinal("resolved_at")) ? null : reader.GetString(reader.GetOrdinal("resolved_at")),
            ResolvedBy = reader.IsDBNull(reader.GetOrdinal("resolved_by")) ? null : reader.GetString(reader.GetOrdinal("resolved_by")),
            ResolutionComment = reader.IsDBNull(reader.GetOrdinal("resolution_comment")) ? null : reader.GetString(reader.GetOrdinal("resolution_comment")),
            Attachments = reader.IsDBNull(reader.GetOrdinal("attachments")) ? null : reader.GetString(reader.GetOrdinal("attachments")),
            Tags = reader.IsDBNull(reader.GetOrdinal("tags")) ? null : reader.GetString(reader.GetOrdinal("tags"))
        };
    }
}
