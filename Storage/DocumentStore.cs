using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Core.Common;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage;

/// <summary>
/// Oracle document storage.
/// Full-text search uses like-based matching until Oracle Text is wired in.
/// </summary>
public class DocumentStore : IDocumentStore
{
    private readonly ILogger _log = LoggingService.ForContext<DocumentStore>();
    private readonly string _connectionString;
    private readonly string _actor;

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

    public DocumentStore(string oracleConnectionString, string actor = "local:unknown")
    {
        _connectionString = oracleConnectionString;
        _actor = actor;
    }

    private static OracleCommand Ora(OracleConnection c, string sql)
    {
        var cmd = c.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = OracleSql.ToOracleBindSyntax(sql);
        return cmd;
    }

    private static void PrepCmd(OracleCommand cmd)
    {
        cmd.BindByName = true;
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
    }

    public long Insert(Document doc)
    {
        return ExecuteDbOperation(() =>
        {
            doc.Uuid = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            doc.CaptureTime = now.ToString("O", CultureInfo.InvariantCulture);
            doc.UpdatedAt = now.ToString("O", CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(doc.Status)) doc.Status = Enums.Status.Draft;

            // Calculate file hash if file exists
            if (!string.IsNullOrEmpty(doc.FilePath) && File.Exists(doc.FilePath))
            {
                doc.FileHash = CalculateFileHash(doc.FilePath);
                doc.FileSize = new FileInfo(doc.FilePath).Length;
            }

            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = Ora(conn, @"
                INSERT INTO documents (uuid, file_path, document_type, extracted_date, amounts, account_name, account_number, transaction_reference, snippet, ocr_text,
                    capture_time, source, engagement, section, clearing_direction, clearing_status, notes,
                    confidence, status, reviewed_at, updated_at, branch, explanation, created_by, category,
                    file_hash, file_size, page_count, ocr_language, ocr_duration_ms, classification_confidence,
                    is_duplicate, duplicate_of, tags, custom_fields)
                VALUES (@uuid, @fp, @dt, @ed, @am, @acc_name, @acc_num, @txn_ref, @sn, @ocr, @ct, @src, @eng, @sec, @cd, @cs, @notes,
                    @conf, @status, @ra, @ua, @branch, @exp, @created_by, @category,
                    @file_hash, @file_size, @page_count, @ocr_lang, @ocr_duration, @class_conf,
                    @is_dup, @dup_of, @tags, @custom)
                RETURNING id INTO :rid");
            AddParams(cmd, doc);
            var rid = new OracleParameter("rid", OracleDbType.Int64)
                { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(rid);
            PrepCmd(cmd); cmd.ExecuteNonQuery();
            var id = Convert.ToInt64(rid.Value?.ToString() ?? "0", CultureInfo.InvariantCulture);

            _log.Information("Document inserted: {Id} ({Type})", id, doc.DocumentType ?? "Unknown");
            return id;
        }, nameof(Insert), -1L);
    }

    public Result<long> InsertResult(Document doc)
    {
        try
        {
            doc.Uuid = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            doc.CaptureTime = now.ToString("O", CultureInfo.InvariantCulture);
            doc.UpdatedAt = now.ToString("O", CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(doc.Status)) doc.Status = Enums.Status.Draft;

            if (!string.IsNullOrEmpty(doc.FilePath) && File.Exists(doc.FilePath))
            {
                doc.FileHash = CalculateFileHash(doc.FilePath);
                doc.FileSize = new FileInfo(doc.FilePath).Length;
            }

            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = Ora(conn, @"
                INSERT INTO documents (uuid, file_path, document_type, extracted_date, amounts, account_name, account_number, transaction_reference, snippet, ocr_text,
                    capture_time, source, engagement, section, clearing_direction, clearing_status, notes,
                    confidence, status, reviewed_at, updated_at, branch, explanation, created_by, category,
                    file_hash, file_size, page_count, ocr_language, ocr_duration_ms, classification_confidence,
                    is_duplicate, duplicate_of, tags, custom_fields)
                VALUES (@uuid, @fp, @dt, @ed, @am, @acc_name, @acc_num, @txn_ref, @sn, @ocr, @ct, @src, @eng, @sec, @cd, @cs, @notes,
                    @conf, @status, @ra, @ua, @branch, @exp, @created_by, @category,
                    @file_hash, @file_size, @page_count, @ocr_lang, @ocr_duration, @class_conf,
                    @is_dup, @dup_of, @tags, @custom)
                RETURNING id INTO :rid");
            AddParams(cmd, doc);
            var rid = new OracleParameter("rid", OracleDbType.Int64)
                { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(rid);
            PrepCmd(cmd); cmd.ExecuteNonQuery();
            var id = Convert.ToInt64(rid.Value?.ToString() ?? "0", CultureInfo.InvariantCulture);

            _log.Information("Document inserted: {Id} ({Type})", id, doc.DocumentType ?? "Unknown");
            return Result.Success(id);
        }
        catch (OracleException ex)
        {
            _log.Error(ex, "Database error inserting document: {Message}", ex.Message);
            return Result.Failure<long>($"Database error: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error inserting document: {Message}", ex.Message);
            return Result.Failure<long>($"Unexpected error: {ex.Message}", ex);
        }
    }

    public Result<Document> GetResult(int id)
    {
        try
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM documents WHERE id = @id";
            cmd.Parameters.AddWithValue("id", id);
            PrepCmd(cmd); using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                var doc = ReadDocument(r);
                return Result.Success(doc);
            }
            return Result.Failure<Document>($"Document with ID {id} not found");
        }
        catch (OracleException ex)
        {
            _log.Error(ex, "Database error getting document {Id}: {Message}", id, ex.Message);
            return Result.Failure<Document>($"Database error: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error getting document {Id}: {Message}", id, ex.Message);
            return Result.Failure<Document>($"Unexpected error: {ex.Message}", ex);
        }
    }

    public Document? GetById(int id)
    {
        var result = GetResult(id);
        return result.TryGetValue(out var doc) ? doc : null;
    }

    /// <summary>
    /// Batch retrieve documents by IDs. More efficient than calling GetById() multiple times.
    /// </summary>
    public List<Document> GetByIds(List<int> ids)
    {
        if (ids == null || ids.Count == 0)
            return new List<Document>();

        return ExecuteDbOperation(() =>
        {
            var documents = new List<Document>();
            using var conn = new OracleConnection(_connectionString);
            conn.Open();

            // Keep IN-clause parameter count bounded for stable Oracle execution plans.
            // Process in batches when callers provide large ID sets.
            const int batchSize = 500;
            for (int i = 0; i < ids.Count; i += batchSize)
            {
                var batch = ids.Skip(i).Take(batchSize).ToList();
                var placeholders = string.Join(",", batch.Select((_, idx) => $":id{idx}"));
                var sql = $"SELECT * FROM documents WHERE id IN ({placeholders})";

                using var cmd = conn.CreateCommand();
                cmd.BindByName = true;
                cmd.CommandText = sql;
                for (int j = 0; j < batch.Count; j++)
                {
                    cmd.Parameters.AddWithValue($"id{j}", batch[j]);
                }
                PrepCmd(cmd); using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    documents.Add(ReadDocument(r));
                }
            }

            return documents;
        }, nameof(GetByIds), new List<Document>());
    }

    public Document? GetByUuid(string uuid)
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM documents WHERE uuid = @uuid";
            cmd.Parameters.AddWithValue("uuid", uuid);
            PrepCmd(cmd); using var r = cmd.ExecuteReader();
            return r.Read() ? ReadDocument(r) : null;
        }, nameof(GetByUuid), null);
    }

    public Document? GetByFileHash(string fileHash)
    {
        if (string.IsNullOrEmpty(fileHash))
            return null;

        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM documents WHERE file_hash = @file_hash FETCH FIRST 1 ROWS ONLY";
            cmd.Parameters.AddWithValue("file_hash", fileHash);
            PrepCmd(cmd); using var r = cmd.ExecuteReader();
            return r.Read() ? ReadDocument(r) : null;
        }, nameof(GetByFileHash), null);
    }

    public List<Document> ListDocuments(string? branch = null, string? section = null,
        string? documentType = null, string? status = null, string? dateFrom = null, string? dateTo = null,
        string? textSearch = null, int limit = 500, int offset = 0,
        string? archivedAtFrom = null, string? archivedAtTo = null, bool? legalHoldOnly = null,
        string? retentionExpiryBefore = null, string? tagFilter = null,
        int? custodianId = null, string? disposalStatus = null,
        string? createdBy = null, string? reviewedBy = null, string? createdOrReviewedBy = null,
        string? engagement = null, string? dateFilterField = "extracted", bool newestFirst = false)
    {
        var sql = "SELECT * FROM documents WHERE 1=1";
        var pars = new List<OracleParameter>();

        if (!string.IsNullOrEmpty(branch)) { sql += " AND branch = @branch"; pars.Add(new OracleParameter("branch", branch)); }
        if (!string.IsNullOrEmpty(section)) { sql += " AND section = @sec"; pars.Add(new OracleParameter("sec", section)); }
        if (!string.IsNullOrEmpty(documentType))
        {
            var dt = documentType.Trim();
            // Unclassified-only filter: null/empty, legacy Other, or Unclassified.
            if (string.Equals(dt, "Other", StringComparison.OrdinalIgnoreCase)
                || string.Equals(dt, DocumentTypeInfo.UnclassifiedType, StringComparison.OrdinalIgnoreCase))
                sql += " AND (document_type IS NULL OR TRIM(document_type) = '' OR LOWER(TRIM(document_type)) IN ('other','unclassified'))";
            else
            {
                sql += " AND LOWER(TRIM(COALESCE(document_type,''))) = LOWER(TRIM(@dt))";
                pars.Add(new OracleParameter("dt", dt));
            }
        }
        if (!string.IsNullOrEmpty(status)) { sql += " AND status = @status"; pars.Add(new OracleParameter("status", status)); }
        if (!string.IsNullOrEmpty(dateFrom) || !string.IsNullOrEmpty(dateTo))
        {
            var docDate = (dateFilterField == "capture")
                ? "SUBSTR(capture_time,1,10)"
                : @"COALESCE(
                    CASE
                        WHEN extracted_date IS NULL OR TRIM(extracted_date) = '' THEN NULL
                        WHEN SUBSTR(TRIM(extracted_date), 5, 1) = '-' AND SUBSTR(TRIM(extracted_date), 8, 1) = '-'
                            THEN SUBSTR(TRIM(extracted_date), 1, 10)
                        WHEN SUBSTR(TRIM(extracted_date), 5, 1) = '/' AND SUBSTR(TRIM(extracted_date), 8, 1) = '/'
                            THEN REPLACE(SUBSTR(TRIM(extracted_date), 1, 10), '/', '-')
                        ELSE NULL
                    END,
                    SUBSTR(capture_time,1,10)
                )";
            var df = (dateFrom?.Length >= 10) ? dateFrom.Substring(0, 10) : dateFrom;
            var dto = (dateTo?.Length >= 10) ? dateTo.Substring(0, 10) : dateTo;
            if (!string.IsNullOrEmpty(df))
                { sql += " AND " + docDate + " >= @df"; pars.Add(new OracleParameter("df", df)); }
            if (!string.IsNullOrEmpty(dto))
                { sql += " AND " + docDate + " <= @dto"; pars.Add(new OracleParameter("dto", dto)); }
        }
        if (!string.IsNullOrEmpty(textSearch))
        {
            var trimmedTs = textSearch.Trim();
            var likePattern = "%" + trimmedTs.ToLowerInvariant() + "%";
            var idExactClause = "";
            if (trimmedTs.Length > 0 && trimmedTs.All(static c => c >= '0' && c <= '9')
                && int.TryParse(trimmedTs, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out var exactDocId))
            {
                idExactClause = " OR id = @exactDocId";
                pars.Add(new OracleParameter("exactDocId", exactDocId));
            }

            sql += @" AND (
(ocr_text IS NOT NULL AND LOWER(DBMS_LOB.SUBSTR(ocr_text, 1000, 1)) LIKE @txt) OR
(snippet IS NOT NULL AND LOWER(DBMS_LOB.SUBSTR(snippet, 1000, 1)) LIKE @txt) OR
(notes IS NOT NULL AND LOWER(DBMS_LOB.SUBSTR(notes, 1000, 1)) LIKE @txt) OR
LOWER(COALESCE(file_path,'')) LIKE @txt OR LOWER(COALESCE(tags,'')) LIKE @txt OR
(custom_fields IS NOT NULL AND LOWER(DBMS_LOB.SUBSTR(custom_fields, 1000, 1)) LIKE @txt) OR
LOWER(COALESCE(document_type,'')) LIKE @txt OR LOWER(COALESCE(account_name,'')) LIKE @txt OR LOWER(COALESCE(transaction_reference,'')) LIKE @txt OR LOWER(COALESCE(amounts,'')) LIKE @txt OR
TO_CHAR(id) LIKE @txt" + idExactClause + ")";
            pars.Add(new OracleParameter("txt", likePattern));
        }
        if (!string.IsNullOrEmpty(archivedAtFrom)) { sql += " AND SUBSTR(archived_at, 1, 10) >= @arch_from"; pars.Add(new OracleParameter("arch_from", archivedAtFrom.Length >= 10 ? archivedAtFrom.Substring(0, 10) : archivedAtFrom)); }
        if (!string.IsNullOrEmpty(archivedAtTo)) { sql += " AND SUBSTR(archived_at, 1, 10) <= @arch_to"; pars.Add(new OracleParameter("arch_to", archivedAtTo.Length >= 10 ? archivedAtTo.Substring(0, 10) : archivedAtTo)); }
        if (legalHoldOnly == true) { sql += " AND legal_hold = 1"; }
        if (!string.IsNullOrEmpty(retentionExpiryBefore)) { sql += " AND retention_expiry_date IS NOT NULL AND retention_expiry_date <= @ret_before"; pars.Add(new OracleParameter("ret_before", retentionExpiryBefore)); }
        if (!string.IsNullOrEmpty(tagFilter))
        {
            var tags = tagFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (tags.Count > 0)
            {
                var orParts = new List<string>();
                for (var i = 0; i < tags.Count; i++)
                {
                    var pName = ":tag" + i;
                    orParts.Add("(',' || COALESCE(tags,'') || ',' LIKE " + pName + ")");
                    pars.Add(new OracleParameter("tag" + i, "%," + tags[i].Trim() + ",%"));
                }
                sql += " AND (" + string.Join(" OR ", orParts) + ")";
            }
        }
        if (custodianId.HasValue) { sql += " AND custodian_id = @custodian"; pars.Add(new OracleParameter("custodian", custodianId.Value)); }
        if (!string.IsNullOrEmpty(disposalStatus)) { sql += " AND disposal_status = @disp"; pars.Add(new OracleParameter("disp", disposalStatus)); }
        if (!string.IsNullOrEmpty(createdBy)) { sql += " AND created_by = @created_by"; pars.Add(new OracleParameter("created_by", createdBy)); }
        if (!string.IsNullOrEmpty(reviewedBy)) { sql += " AND reviewed_by = @reviewed_by"; pars.Add(new OracleParameter("reviewed_by", reviewedBy)); }
        if (!string.IsNullOrEmpty(createdOrReviewedBy)) { sql += " AND (created_by = @cor OR reviewed_by = @cor)"; pars.Add(new OracleParameter("cor", createdOrReviewedBy)); }
        if (!string.IsNullOrEmpty(engagement)) { sql += " AND engagement = @engagement"; pars.Add(new OracleParameter("engagement", engagement)); }
        sql += newestFirst
            ? " ORDER BY COALESCE(archived_at, capture_time) DESC, id DESC OFFSET @p_offset ROWS FETCH NEXT @p_limit ROWS ONLY"
            : " ORDER BY COALESCE(archived_at, capture_time) ASC, id ASC OFFSET @p_offset ROWS FETCH NEXT @p_limit ROWS ONLY";
        pars.Add(new OracleParameter("p_limit", limit));
        pars.Add(new OracleParameter("p_offset", offset));

        var list = new List<Document>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        foreach (var p in pars) cmd.Parameters.Add(p);
        PrepCmd(cmd); using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadDocument(r));
        return list;
    }

    public List<Document> ListDocumentsByFolder(string folderPath, int limit = 1000, string? appBaseDirectory = null)
    {
        if (string.IsNullOrEmpty(folderPath))
            return ListDocuments(limit: limit);

        var patterns = BuildFolderPathLikePatterns(folderPath, appBaseDirectory);
        if (patterns.Count == 0)
            return new List<Document>();

        var list = new List<Document>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var orParts = new List<string>();
        for (var i = 0; i < patterns.Count; i++)
        {
            var pName = ":fp" + i;
            orParts.Add("file_path LIKE " + pName);
            cmd.Parameters.AddWithValue("fp" + i, patterns[i]);
        }
        cmd.BindByName = true;
        cmd.CommandText = "SELECT * FROM documents WHERE (" + string.Join(" OR ", orParts) + ") ORDER BY capture_time ASC, id ASC FETCH FIRST @limit ROWS ONLY";
        cmd.Parameters.AddWithValue("limit", limit);
        PrepCmd(cmd); using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadDocument(r));
        return list;
    }

    /// <summary>LIKE patterns for rows under a folder: full path with \ and /, plus relative prefix when base dir is known.</summary>
    private static List<string> BuildFolderPathLikePatterns(string folderPath, string? appBaseDirectory)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        void AddFolderPrefix(string prefix)
        {
            var trimmed = prefix.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(trimmed)) return;
            set.Add(trimmed + Path.DirectorySeparatorChar + "%");
            set.Add(trimmed + Path.AltDirectorySeparatorChar + "%");
        }

        try
        {
            AddFolderPrefix(Path.GetFullPath(folderPath));
        }
        catch (Exception)
        {
            AddFolderPrefix(folderPath);
        }

        if (!string.IsNullOrWhiteSpace(appBaseDirectory))
        {
            try
            {
                var baseFull = Path.GetFullPath(appBaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var folderFull = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var rel = Path.GetRelativePath(baseFull, folderFull);
                if (!string.IsNullOrEmpty(rel) && rel != "." && !rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !rel.Equals("..", StringComparison.Ordinal))
                    AddFolderPrefix(rel);
            }
            catch (Exception)
            {
                // ignore — full-path patterns still apply
            }
        }

        return set.ToList();
    }

    public List<string> GetDistinctBranches()
    {
        return GetDistinct("branch", null, null);
    }

    public List<string> GetDistinctSections(string? branch)
    {
        return GetDistinct("section", branch, null);
    }

    public List<string> GetDistinctDocumentTypes(string? branch, string? section)
    {
        return GetDistinct("document_type", branch, section);
    }

    public List<string> GetDistinctEngagements(string? branch = null)
    {
        var sql = "SELECT DISTINCT engagement FROM documents WHERE engagement IS NOT NULL AND TRIM(engagement) != ''";
        var pars = new List<OracleParameter>();
        if (!string.IsNullOrEmpty(branch)) { sql += " AND branch = @branch"; pars.Add(new OracleParameter("branch", branch)); }
        sql += " ORDER BY 1";
        var list = new List<string>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in pars) cmd.Parameters.Add(p);
        PrepCmd(cmd); using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var val = r.IsDBNull(0) ? null : r.GetString(0);
            if (!string.IsNullOrWhiteSpace(val))
                list.Add(val.Trim());
        }
        return list;
    }

    public List<string> GetDistinctTags(string? status = null)
    {
        var list = new List<string>();
        var sql = "SELECT tags FROM documents WHERE tags IS NOT NULL AND TRIM(tags) != ''";
        if (!string.IsNullOrEmpty(status)) { sql += " AND status = @status"; }
        sql += " ORDER BY 1";
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue("status", status);
        PrepCmd(cmd); using var r = cmd.ExecuteReader();
        var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (r.Read())
        {
            var tagsStr = r.IsDBNull(0) ? null : r.GetString(0);
            if (string.IsNullOrEmpty(tagsStr)) continue;
            foreach (var t in tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(t) && tagSet.Add(t.Trim()))
                    list.Add(t.Trim());
            }
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private List<string> GetDistinct(string column, string? branch, string? section)
    {
        // Whitelist of allowed columns to prevent SQL injection
        var allowedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "branch", "section", "document_type", "financial_year", "status",
            "assigned_to", "cleared_by", "reviewed_by", "approved_by"
        };

        if (!allowedColumns.Contains(column))
        {
            _log.Warning("Attempted to query invalid column: {Column}", column);
            return new List<string>();
        }

        var sql = $"SELECT DISTINCT {column} FROM documents WHERE {column} IS NOT NULL AND TRIM({column}) != ''";
        var pars = new List<OracleParameter>();
        if (!string.IsNullOrEmpty(branch)) { sql += " AND branch = @branch"; pars.Add(new OracleParameter("branch", branch)); }
        if (!string.IsNullOrEmpty(section)) { sql += " AND section = @sec"; pars.Add(new OracleParameter("sec", section)); }
        sql += " ORDER BY 1";

        var list = new List<string>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in pars) cmd.Parameters.Add(p);
        PrepCmd(cmd); using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var val = r.IsDBNull(0) ? null : r.GetString(0);
            if (!string.IsNullOrWhiteSpace(val))
                list.Add(val.Trim());
        }
        return list;
    }

    public List<Document> FullTextSearch(string query, int limit = 100, string? branch = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<Document>();

        var normalizedBranch = string.IsNullOrWhiteSpace(branch) ? null : branch.Trim();
        var safeLimit = Math.Clamp(limit, 1, 1000);
        var containsQuery = BuildContainsQuery(query);

        try
        {
            var docs = new List<Document>();
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT *
                FROM documents
                WHERE (
                    CONTAINS(ocr_text, @q, 1) > 0 OR
                    CONTAINS(snippet, @q, 2) > 0 OR
                    CONTAINS(notes, @q, 3) > 0 OR
                    CONTAINS(document_type, @q, 4) > 0
                )" + (normalizedBranch != null ? " AND branch = @branch" : "") + @"
                ORDER BY (NVL(SCORE(1), 0) + NVL(SCORE(2), 0) + NVL(SCORE(3), 0) + NVL(SCORE(4), 0)) DESC,
                         capture_time DESC
                FETCH FIRST @p_limit ROWS ONLY";
            cmd.Parameters.AddWithValue("@q", containsQuery);
            if (normalizedBranch != null) cmd.Parameters.AddWithValue("@branch", normalizedBranch);
            cmd.Parameters.AddWithValue("@p_limit", safeLimit);

            PrepCmd(cmd); using var r = cmd.ExecuteReader();
            while (r.Read())
                docs.Add(ReadDocument(r));
            return docs;
        }
        catch (OracleException ex)
        {
            _log.Warning(ex, "Oracle Text query failed; falling back to LIKE full-text search.");
            return ListDocuments(textSearch: query, branch: normalizedBranch, limit: safeLimit);
        }
    }

    public bool Update(Document doc)
    {
        return ExecuteDbOperation(() =>
        {
            doc.UpdatedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            UPDATE documents SET
                file_path = @fp,
                document_type = @dt,
                extracted_date = @ed,
                amounts = @am,
                account_name = @acc_name,
                account_number = @acc_num,
                transaction_reference = @txn_ref,
                snippet = @sn,
                ocr_text = @ocr,
                engagement = @eng,
                section = @sec,
                clearing_direction = @cd,
                clearing_status = @cs,
                notes = @notes,
                confidence = @conf,
                status = @status,
                reviewed_at = @ra,
                updated_at = @ua,
                branch = @branch,
                explanation = @exp,
                category = @category,
                classification_confidence = @class_conf,
                ocr_language = @ocr_lang,
                ocr_duration_ms = @ocr_duration,
                tags = @tags,
                custom_fields = @custom,
                custodian_id = @custodian_id,
                disposal_status = @disposal_status,
                disposal_requested_at = @disposal_requested_at,
                disposal_requested_by = @disposal_requested_by,
                disposal_approved_at = @disposal_approved_at,
                disposal_approved_by = @disposal_approved_by,
                disposal_rejected_at = @disposal_rejected_at,
                disposal_rejected_by = @disposal_rejected_by,
                disposal_rejection_reason = @disposal_rejection_reason,
                archived_at = @archived_at,
                archived_by = @archived_by,
                legal_hold = @legal_hold,
                legal_hold_reason = @legal_hold_reason,
                legal_hold_case_number = @legal_hold_case_number,
                legal_hold_applied_at = @legal_hold_applied_at,
                legal_hold_applied_by = @legal_hold_applied_by,
                retention_expiry_date = @retention_expiry_date,
                is_immutable = @is_immutable,
                immutable_hash = @immutable_hash,
                immutable_since = @immutable_since,
                hash_verification_count = @hash_verification_count,
                last_hash_verification = @last_hash_verification
            WHERE id = @id";

        cmd.Parameters.AddWithValue("id", doc.Id);
        cmd.Parameters.AddWithValue("fp", doc.FilePath);
        cmd.Parameters.AddWithValue("dt", doc.DocumentType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("ed", doc.ExtractedDate ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("am", doc.Amounts ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("acc_name", doc.AccountName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("acc_num", doc.AccountNumber ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("txn_ref", doc.TransactionReference ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("sn", doc.Snippet ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("ocr", doc.OcrText ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("ocr_lang", doc.OcrLanguage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("ocr_duration", doc.OcrDurationMs ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("eng", RequiredTextOrFallback(doc.Engagement, "General"));
        cmd.Parameters.AddWithValue("sec", RequiredTextOrFallback(doc.Section, "Unspecified"));
        cmd.Parameters.AddWithValue("cd", doc.ClearingDirection ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("cs", doc.ClearingStatus ?? (object)DBNull.Value);
#pragma warning disable CS0618 // Legacy notes column for DB compatibility
        cmd.Parameters.AddWithValue("notes", doc.Notes ?? (object)DBNull.Value);
#pragma warning restore CS0618
        cmd.Parameters.AddWithValue("conf", doc.Confidence ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("status", doc.Status);
        cmd.Parameters.Add(new OracleParameter("ra", ToDateTimeOrNull(doc.ReviewedAt) ?? (object)DBNull.Value) { OracleDbType = OracleDbType.TimeStamp });
        cmd.Parameters.Add(new OracleParameter("ua", ToDateTimeOrNow(doc.UpdatedAt)) { OracleDbType = OracleDbType.TimeStamp });
        cmd.Parameters.AddWithValue("branch", doc.Branch ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("exp", doc.Explanation ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("category", doc.Category ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("class_conf", doc.ClassificationConfidence ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("tags", doc.Tags ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("custom", doc.CustomFields ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("custodian_id", doc.CustodianId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("disposal_status", doc.DisposalStatus ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("disposal_requested_at", doc.DisposalRequestedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("disposal_requested_by", doc.DisposalRequestedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("disposal_approved_at", doc.DisposalApprovedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("disposal_approved_by", doc.DisposalApprovedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("disposal_rejected_at", doc.DisposalRejectedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("disposal_rejected_by", doc.DisposalRejectedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("disposal_rejection_reason", doc.DisposalRejectionReason ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("archived_at", doc.ArchivedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("archived_by", doc.ArchivedBy.HasValue ? doc.ArchivedBy.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("legal_hold", doc.LegalHold ? 1 : 0);
        cmd.Parameters.AddWithValue("legal_hold_reason", doc.LegalHoldReason ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("legal_hold_case_number", doc.LegalHoldCaseNumber ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("legal_hold_applied_at", doc.LegalHoldAppliedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("legal_hold_applied_by", doc.LegalHoldAppliedBy.HasValue ? doc.LegalHoldAppliedBy.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("retention_expiry_date", doc.RetentionExpiryDate ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("is_immutable", doc.IsImmutable ? 1 : 0);
        cmd.Parameters.AddWithValue("immutable_hash", doc.ImmutableHash ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("immutable_since", doc.ImmutableSince ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("hash_verification_count", doc.HashVerificationCount);
        cmd.Parameters.AddWithValue("last_hash_verification", doc.LastHashVerification ?? (object)DBNull.Value);

            PrepCmd(cmd); return cmd.ExecuteNonQuery() > 0;
        }, nameof(Update), false);
    }

    public bool UpdateOcrFields(Document doc)
    {
        return ExecuteDbOperation(() =>
        {
            var now = DateTime.UtcNow;
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            UPDATE documents SET
                ocr_text = @ocr,
                snippet = @sn,
                account_name = @acc_name,
                account_number = @acc_num,
                transaction_reference = @txn_ref,
                amounts = @am,
                ocr_language = @ocr_lang,
                ocr_duration_ms = @ocr_duration,
                updated_at = @ua
            WHERE id = @id";

            cmd.Parameters.AddWithValue("id", doc.Id);
            cmd.Parameters.AddWithValue("ocr", doc.OcrText ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("sn", doc.Snippet ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("acc_name", doc.AccountName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("acc_num", doc.AccountNumber ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("txn_ref", doc.TransactionReference ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("am", doc.Amounts ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("ocr_lang", doc.OcrLanguage ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("ocr_duration", doc.OcrDurationMs ?? (object)DBNull.Value);
            cmd.Parameters.Add(new OracleParameter("ua", OracleDbType.TimeStamp) { Value = now });

            PrepCmd(cmd); return cmd.ExecuteNonQuery() > 0;
        }, nameof(UpdateOcrFields), false);
    }

    public Result UpdateResult(Document doc, DateTime? expectedUpdatedAtUtc = null)
    {
        try
        {
            doc.UpdatedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            UPDATE documents SET
                file_path = @fp,
                document_type = @dt,
                extracted_date = @ed,
                amounts = @am,
                account_name = @acc_name,
                account_number = @acc_num,
                transaction_reference = @txn_ref,
                snippet = @sn,
                ocr_text = @ocr,
                engagement = @eng,
                section = @sec,
                clearing_direction = @cd,
                clearing_status = @cs,
                notes = @notes,
                confidence = @conf,
                status = @status,
                reviewed_at = @ra,
                updated_at = @ua,
                branch = @branch,
                explanation = @exp,
                category = @category,
                classification_confidence = @class_conf,
                ocr_language = @ocr_lang,
                ocr_duration_ms = @ocr_duration,
                tags = @tags,
                custom_fields = @custom,
                custodian_id = @custodian_id,
                disposal_status = @disposal_status,
                disposal_requested_at = @disposal_requested_at,
                disposal_requested_by = @disposal_requested_by,
                disposal_approved_at = @disposal_approved_at,
                disposal_approved_by = @disposal_approved_by,
                disposal_rejected_at = @disposal_rejected_at,
                disposal_rejected_by = @disposal_rejected_by,
                disposal_rejection_reason = @disposal_rejection_reason,
                archived_at = @archived_at,
                archived_by = @archived_by,
                legal_hold = @legal_hold,
                legal_hold_reason = @legal_hold_reason,
                legal_hold_case_number = @legal_hold_case_number,
                legal_hold_applied_at = @legal_hold_applied_at,
                legal_hold_applied_by = @legal_hold_applied_by,
                retention_expiry_date = @retention_expiry_date,
                is_immutable = @is_immutable,
                immutable_hash = @immutable_hash,
                immutable_since = @immutable_since,
                hash_verification_count = @hash_verification_count,
                last_hash_verification = @last_hash_verification
            WHERE id = @id";

            if (expectedUpdatedAtUtc.HasValue)
                cmd.CommandText += " AND updated_at = @exp_updated_at";

            cmd.Parameters.AddWithValue("id", doc.Id);
            cmd.Parameters.AddWithValue("fp", doc.FilePath);
            cmd.Parameters.AddWithValue("dt", doc.DocumentType ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("ed", doc.ExtractedDate ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("am", doc.Amounts ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("acc_name", doc.AccountName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("acc_num", doc.AccountNumber ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("txn_ref", doc.TransactionReference ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("sn", doc.Snippet ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("ocr", doc.OcrText ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("ocr_lang", doc.OcrLanguage ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("ocr_duration", doc.OcrDurationMs ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("eng", RequiredTextOrFallback(doc.Engagement, "General"));
            cmd.Parameters.AddWithValue("sec", RequiredTextOrFallback(doc.Section, "Unspecified"));
            cmd.Parameters.AddWithValue("cd", doc.ClearingDirection ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("cs", doc.ClearingStatus ?? (object)DBNull.Value);
#pragma warning disable CS0618
            cmd.Parameters.AddWithValue("notes", doc.Notes ?? (object)DBNull.Value);
#pragma warning restore CS0618
            cmd.Parameters.AddWithValue("conf", doc.Confidence ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("status", doc.Status);
            cmd.Parameters.Add(new OracleParameter("ra", ToDateTimeOrNull(doc.ReviewedAt) ?? (object)DBNull.Value) { OracleDbType = OracleDbType.TimeStamp });
            cmd.Parameters.Add(new OracleParameter("ua", ToDateTimeOrNow(doc.UpdatedAt)) { OracleDbType = OracleDbType.TimeStamp });
            cmd.Parameters.AddWithValue("branch", doc.Branch ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("exp", doc.Explanation ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("category", doc.Category ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("class_conf", doc.ClassificationConfidence ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("tags", doc.Tags ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("custom", doc.CustomFields ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("custodian_id", doc.CustodianId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("disposal_status", doc.DisposalStatus ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("disposal_requested_at", doc.DisposalRequestedAt ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("disposal_requested_by", doc.DisposalRequestedBy ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("disposal_approved_at", doc.DisposalApprovedAt ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("disposal_approved_by", doc.DisposalApprovedBy ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("disposal_rejected_at", doc.DisposalRejectedAt ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("disposal_rejected_by", doc.DisposalRejectedBy ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("disposal_rejection_reason", doc.DisposalRejectionReason ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("archived_at", doc.ArchivedAt ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("archived_by", doc.ArchivedBy.HasValue ? doc.ArchivedBy.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("legal_hold", doc.LegalHold ? 1 : 0);
            cmd.Parameters.AddWithValue("legal_hold_reason", doc.LegalHoldReason ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("legal_hold_case_number", doc.LegalHoldCaseNumber ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("legal_hold_applied_at", doc.LegalHoldAppliedAt ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("legal_hold_applied_by", doc.LegalHoldAppliedBy.HasValue ? doc.LegalHoldAppliedBy.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("retention_expiry_date", doc.RetentionExpiryDate ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("is_immutable", doc.IsImmutable ? 1 : 0);
            cmd.Parameters.AddWithValue("immutable_hash", doc.ImmutableHash ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("immutable_since", doc.ImmutableSince ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("hash_verification_count", doc.HashVerificationCount);
            cmd.Parameters.AddWithValue("last_hash_verification", doc.LastHashVerification ?? (object)DBNull.Value);

            if (expectedUpdatedAtUtc.HasValue)
            {
                var expUtc = expectedUpdatedAtUtc.Value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(expectedUpdatedAtUtc.Value, DateTimeKind.Utc)
                    : expectedUpdatedAtUtc.Value.ToUniversalTime();
                cmd.Parameters.Add(new OracleParameter("exp_updated_at", OracleDbType.TimeStamp) { Value = expUtc });
            }

            PrepCmd(cmd);
            var rows = cmd.ExecuteNonQuery();
            if (rows > 0)
                return Result.Success();
            if (expectedUpdatedAtUtc.HasValue)
                return Result.Failure(
                    "Concurrency conflict: document was updated by another user or instance. Reload and retry.");
            return Result.Failure("No rows updated");
        }
        catch (OracleException ex)
        {
            _log.Error(ex, "Database error updating document {Id}: {Message}", doc.Id, ex.Message);
            return Result.Failure($"Database error: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error updating document {Id}: {Message}", doc.Id, ex.Message);
            return Result.Failure($"Unexpected error: {ex.Message}", ex);
        }
    }

    public bool UpdateStatus(int id, string status)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE documents SET status = @s, updated_at = @ua WHERE id = @id";
        cmd.Parameters.AddWithValue("s", status);
        var now = DateTime.UtcNow;
        cmd.Parameters.Add(new OracleParameter("ua", OracleDbType.TimeStamp) { Value = now });
        cmd.Parameters.AddWithValue("id", id);
        PrepCmd(cmd); return cmd.ExecuteNonQuery() > 0;
    }

    [Obsolete("Use INotesStore instead for structured notes")]
    public bool UpdateNotes(int id, string notes)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE documents SET notes = @n, updated_at = @ua WHERE id = @id";
        cmd.Parameters.AddWithValue("n", notes ?? "");
        var now = DateTime.UtcNow;
        cmd.Parameters.Add(new OracleParameter("ua", OracleDbType.TimeStamp) { Value = now });
        cmd.Parameters.AddWithValue("id", id);
        PrepCmd(cmd); return cmd.ExecuteNonQuery() > 0;
    }

    public bool UpdateTags(int id, string tags)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE documents SET tags = @tags, updated_at = @ua WHERE id = @id";
        cmd.Parameters.AddWithValue("tags", tags ?? (object)DBNull.Value);
        var now = DateTime.UtcNow;
        cmd.Parameters.Add(new OracleParameter("ua", OracleDbType.TimeStamp) { Value = now });
        cmd.Parameters.AddWithValue("id", id);
        PrepCmd(cmd); return cmd.ExecuteNonQuery() > 0;
    }

    public bool UpdateCustodian(int id, int? custodianId)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE documents SET custodian_id = @cid, updated_at = @ua WHERE id = @id";
        cmd.Parameters.AddWithValue("cid", custodianId ?? (object)DBNull.Value);
        var now = DateTime.UtcNow;
        cmd.Parameters.Add(new OracleParameter("ua", OracleDbType.TimeStamp) { Value = now });
        cmd.Parameters.AddWithValue("id", id);
        PrepCmd(cmd); return cmd.ExecuteNonQuery() > 0;
    }

    public bool UpdateDisposalStatus(int id, string disposalStatus, int? requestedBy = null, int? approvedBy = null, int? rejectedBy = null, string? rejectionReason = null)
    {
        var doc = GetById(id);
        if (doc == null) return false;

            var now = DateTime.UtcNow;
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();

        if (disposalStatus == "Pending")
        {
            cmd.CommandText = "UPDATE documents SET disposal_status = 'Pending', disposal_requested_at = @now, disposal_requested_by = @by, disposal_approved_at = NULL, disposal_approved_by = NULL, disposal_rejected_at = NULL, disposal_rejected_by = NULL, disposal_rejection_reason = NULL, updated_at = @ua WHERE id = @id";
            cmd.Parameters.Add(new OracleParameter("now", OracleDbType.TimeStamp) { Value = now });
            cmd.Parameters.AddWithValue("by", requestedBy ?? (object)DBNull.Value);
        }
        else if (disposalStatus == "Approved")
        {
            cmd.CommandText = "UPDATE documents SET disposal_status = 'Approved', disposal_approved_at = @now, disposal_approved_by = @by, updated_at = @ua WHERE id = @id";
            cmd.Parameters.Add(new OracleParameter("now", OracleDbType.TimeStamp) { Value = now });
            cmd.Parameters.AddWithValue("by", approvedBy ?? (object)DBNull.Value);
        }
        else if (disposalStatus == "Rejected")
        {
            cmd.CommandText = "UPDATE documents SET disposal_status = 'Rejected', disposal_rejected_at = @now, disposal_rejected_by = @by, disposal_rejection_reason = @reason, updated_at = @ua WHERE id = @id";
            cmd.Parameters.Add(new OracleParameter("now", OracleDbType.TimeStamp) { Value = now });
            cmd.Parameters.AddWithValue("by", rejectedBy ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("reason", rejectionReason ?? (object)DBNull.Value);
        }
        else
            return false;

        cmd.Parameters.Add(new OracleParameter("ua", OracleDbType.TimeStamp) { Value = now });
        cmd.Parameters.AddWithValue("id", id);
        PrepCmd(cmd); return cmd.ExecuteNonQuery() > 0;
    }

    public bool UpdateDocumentType(int id, string documentType)
    {
        return UpdateDocumentType(id, documentType, null);
    }

    public bool UpdateDocumentType(int id, string documentType, IDbTransaction? transaction)
    {
        var ownConnection = transaction == null;
        OracleConnection? conn = null;
        
        try
        {
            conn = (transaction?.Connection as OracleConnection) ?? new OracleConnection(_connectionString);
            if (ownConnection) conn.Open();
            
            using var cmd = Ora(conn, "UPDATE documents SET document_type = @dt, updated_at = @ua WHERE id = @id");
            cmd.Transaction = transaction as OracleTransaction;
            cmd.Parameters.AddWithValue("dt", documentType ?? "");
            cmd.Parameters.Add(new OracleParameter("ua", OracleDbType.TimeStamp) { Value = DateTime.UtcNow });
            cmd.Parameters.AddWithValue("id", id);
            PrepCmd(cmd); return cmd.ExecuteNonQuery() > 0;
        }
        finally
        {
            if (ownConnection) conn?.Dispose();
        }
    }

    public bool UpdateDocumentSection(int id, string section)
    {
        return UpdateDocumentSection(id, section, null);
    }

    public bool UpdateDocumentSection(int id, string section, IDbTransaction? transaction)
    {
        var ownConnection = transaction == null;
        OracleConnection? conn = null;
        
        try
        {
            conn = (transaction?.Connection as OracleConnection) ?? new OracleConnection(_connectionString);
            if (ownConnection) conn.Open();
            
            using var cmd = Ora(conn, "UPDATE documents SET section = @sec, updated_at = @ua WHERE id = @id");
            cmd.Transaction = transaction as OracleTransaction;
            cmd.Parameters.AddWithValue("sec", section ?? "");
            cmd.Parameters.Add(new OracleParameter("ua", OracleDbType.TimeStamp) { Value = DateTime.UtcNow });
            cmd.Parameters.AddWithValue("id", id);
            PrepCmd(cmd); return cmd.ExecuteNonQuery() > 0;
        }
        finally
        {
            if (ownConnection) conn?.Dispose();
        }
    }

    public bool UpdateDocumentFilePath(int id, string filePath)
    {
        return UpdateDocumentFilePath(id, filePath, null);
    }

    public List<Document> GetDocumentsByFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return new List<Document>();

        return ExecuteDbOperation(() =>
        {
            var list = new List<Document>();
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM documents WHERE file_path = @fp";
            cmd.Parameters.AddWithValue("fp", filePath.Trim());
            PrepCmd(cmd); using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(ReadDocument(r));
            return list;
        }, nameof(GetDocumentsByFilePath), new List<Document>());
    }

    public bool UpdateDocumentFilePath(int id, string filePath, IDbTransaction? transaction)
    {
        var ownConnection = transaction == null;
        OracleConnection? conn = null;
        
        try
        {
            conn = (transaction?.Connection as OracleConnection) ?? new OracleConnection(_connectionString);
            if (ownConnection) conn.Open();
            
            using var cmd = Ora(conn, "UPDATE documents SET file_path = @fp, updated_at = @ua WHERE id = @id");
            cmd.Transaction = transaction as OracleTransaction;
            cmd.Parameters.AddWithValue("fp", filePath ?? "");
            cmd.Parameters.Add(new OracleParameter("ua", OracleDbType.TimeStamp) { Value = DateTime.UtcNow });
            cmd.Parameters.AddWithValue("id", id);
            PrepCmd(cmd); return cmd.ExecuteNonQuery() > 0;
        }
        finally
        {
            if (ownConnection) conn?.Dispose();
        }
    }

    public IDbConnection CreateConnection()
    {
        return new OracleConnection(_connectionString);
    }

    public Result DeleteResult(int id)
    {
        try
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM documents WHERE id = @id";
            cmd.Parameters.AddWithValue("id", id);
            PrepCmd(cmd); var rows = cmd.ExecuteNonQuery();
            return rows > 0 ? Result.Success() : Result.Failure($"Document with ID {id} not found");
        }
        catch (OracleException ex)
        {
            _log.Error(ex, "Database error deleting document {Id}: {Message}", id, ex.Message);
            return Result.Failure($"Database error: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error deleting document {Id}: {Message}", id, ex.Message);
            return Result.Failure($"Unexpected error: {ex.Message}", ex);
        }
    }

    public bool Delete(int id)
    {
        var result = DeleteResult(id);
        return result.IsSuccess;
    }

    public int CountDocumentsWithFilePath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return 0;
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM documents WHERE file_path = @fp";
            cmd.Parameters.AddWithValue("fp", filePath);
            PrepCmd(cmd); return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }, nameof(CountDocumentsWithFilePath), 0);
    }

    public int Count(string? section = null, string? status = null, string? branch = null)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        var sql = "SELECT COUNT(*) FROM documents WHERE 1=1";
        using var cmd = conn.CreateCommand();

        if (!string.IsNullOrEmpty(section))
        {
            sql += " AND section = @sec";
            cmd.Parameters.AddWithValue("sec", section);
        }

        if (!string.IsNullOrEmpty(status))
        {
            sql += " AND status = @status";
            cmd.Parameters.AddWithValue("status", status);
        }

        if (!string.IsNullOrEmpty(branch))
        {
            sql += " AND branch = @branch";
            cmd.Parameters.AddWithValue("branch", branch);
        }

        cmd.CommandText = sql;
        PrepCmd(cmd); return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public int GetTotalDocumentCount()
    {
        return ExecuteDbOperation(() =>
        {
            using var conn = new OracleConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM documents";
            PrepCmd(cmd); return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }, nameof(GetTotalDocumentCount), 0);
    }

    public DocumentStats GetStats(string? branch = null)
    {
        var stats = new DocumentStats();
        var branchFilter = !string.IsNullOrEmpty(branch) ? " AND branch = @branch" : "";

        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        // Total count
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM documents WHERE 1=1" + branchFilter;
            if (!string.IsNullOrEmpty(branch)) cmd.Parameters.AddWithValue("branch", branch);
            PrepCmd(cmd); stats.TotalDocuments = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        // Count by status
        stats.DraftCount = Count(status: Enums.Status.Draft, branch: branch);
        stats.ReviewedCount = Count(status: Enums.Status.Reviewed, branch: branch);
        stats.ReadyForAuditCount = Count(status: Enums.Status.ReadyForAudit, branch: branch);
        stats.IssueCount = Count(status: Enums.Status.Issue, branch: branch);
        stats.ClearedCount = Count(status: Enums.Status.Cleared, branch: branch);

        // Count by section
        stats.IndividualsCount = Count(section: Enums.Section.Individuals, branch: branch);
        stats.CompaniesCount = Count(section: Enums.Section.Companies, branch: branch);
        stats.ClearingCount = Count(section: Enums.Section.Clearing, branch: branch);

        // Time-based counts
        var now = DateTime.UtcNow;
        var todayStart = now.Date.ToString("O", CultureInfo.InvariantCulture);
        var weekStart = now.AddDays(-(int)now.DayOfWeek).Date.ToString("O", CultureInfo.InvariantCulture);
        var monthStart = new DateTime(now.Year, now.Month, 1).ToString("O", CultureInfo.InvariantCulture);

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM documents WHERE capture_time >= @p_date" + branchFilter;
            cmd.Parameters.AddWithValue("p_date", todayStart);
            if (!string.IsNullOrEmpty(branch)) cmd.Parameters.AddWithValue("branch", branch);
            PrepCmd(cmd); stats.TodayCount = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM documents WHERE capture_time >= @p_date" + branchFilter;
            cmd.Parameters.AddWithValue("p_date", weekStart);
            if (!string.IsNullOrEmpty(branch)) cmd.Parameters.AddWithValue("branch", branch);
            PrepCmd(cmd); stats.ThisWeekCount = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM documents WHERE capture_time >= @p_date" + branchFilter;
            cmd.Parameters.AddWithValue("p_date", monthStart);
            if (!string.IsNullOrEmpty(branch)) cmd.Parameters.AddWithValue("branch", branch);
            PrepCmd(cmd); stats.ThisMonthCount = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        // Count by document type
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT document_type, COUNT(*) as cnt FROM documents WHERE document_type IS NOT NULL" + branchFilter + " GROUP BY document_type";
            if (!string.IsNullOrEmpty(branch)) cmd.Parameters.AddWithValue("branch", branch);
            PrepCmd(cmd); using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var docType = reader.GetString(0);
                var count = reader.GetInt32(1);
                stats.ByDocumentType[docType] = count;
            }
        }

        // Archive-specific stats
        stats.ArchivedCount = Count(status: Enums.Status.Archived, branch: branch);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM documents WHERE status = @status AND legal_hold = 1" + branchFilter;
            cmd.Parameters.AddWithValue("status", Enums.Status.Archived);
            if (!string.IsNullOrEmpty(branch)) cmd.Parameters.AddWithValue("branch", branch);
            PrepCmd(cmd); stats.ArchivedLegalHoldCount = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        using (var cmd = conn.CreateCommand())
        {
            var expiry90 = DateTime.UtcNow.AddDays(90).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            cmd.CommandText = "SELECT COUNT(*) FROM documents WHERE status = @status AND retention_expiry_date IS NOT NULL AND retention_expiry_date <= @expiry" + branchFilter;
            cmd.Parameters.AddWithValue("status", Enums.Status.Archived);
            cmd.Parameters.AddWithValue("expiry", expiry90);
            if (!string.IsNullOrEmpty(branch)) cmd.Parameters.AddWithValue("branch", branch);
            PrepCmd(cmd); stats.ArchivedExpiringWithin90DaysCount = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM documents WHERE disposal_status = 'Pending'" + branchFilter;
            if (!string.IsNullOrEmpty(branch)) cmd.Parameters.AddWithValue("branch", branch);
            PrepCmd(cmd); stats.DisposalPendingCount = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        catch { stats.DisposalPendingCount = 0; }

        return stats;
    }

    private string? CalculateFileHash(string filePath)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return null;
        }
    }

    private static object ToDateTimeOrNow(string? value)
    {
        return ToDateTimeOrNull(value) ?? DateTime.UtcNow;
    }

    private static DateTime? ToDateTimeOrNull(string? value)
    {
        if (DateTime.TryParse(value, out var parsed))
            return parsed;
        return null;
    }

    private static void AddParams(OracleCommand cmd, Document doc)
    {
        cmd.Parameters.AddWithValue("uuid", doc.Uuid);
        cmd.Parameters.AddWithValue("fp", doc.FilePath);
        cmd.Parameters.AddWithValue("dt", doc.DocumentType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("ed", doc.ExtractedDate ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("am", doc.Amounts ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("acc_name", doc.AccountName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("acc_num", doc.AccountNumber ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("txn_ref", doc.TransactionReference ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("sn", doc.Snippet ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("ocr", doc.OcrText ?? (object)DBNull.Value);
        cmd.Parameters.Add(new OracleParameter("ct", ToDateTimeOrNow(doc.CaptureTime)) { OracleDbType = OracleDbType.TimeStamp });
        // Oracle treats empty string as NULL, so NOT NULL text columns need a non-empty fallback.
        cmd.Parameters.AddWithValue("src", RequiredTextOrFallback(doc.Source, "Unknown"));
        cmd.Parameters.AddWithValue("eng", RequiredTextOrFallback(doc.Engagement, "General"));
        cmd.Parameters.AddWithValue("sec", RequiredTextOrFallback(doc.Section, "Unspecified"));
        cmd.Parameters.AddWithValue("cd", doc.ClearingDirection ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("cs", doc.ClearingStatus ?? (object)DBNull.Value);
#pragma warning disable CS0618 // Legacy notes column for DB compatibility
        cmd.Parameters.AddWithValue("notes", doc.Notes ?? (object)DBNull.Value);
#pragma warning restore CS0618
        cmd.Parameters.AddWithValue("conf", doc.Confidence ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("status", doc.Status);
        cmd.Parameters.Add(new OracleParameter("ra", ToDateTimeOrNull(doc.ReviewedAt) ?? (object)DBNull.Value) { OracleDbType = OracleDbType.TimeStamp });
        cmd.Parameters.Add(new OracleParameter("ua", ToDateTimeOrNow(doc.UpdatedAt)) { OracleDbType = OracleDbType.TimeStamp });
        cmd.Parameters.AddWithValue("branch", doc.Branch ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("exp", doc.Explanation ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("created_by", doc.CreatedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("category", doc.Category ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("file_hash", doc.FileHash ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("file_size", doc.FileSize ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("page_count", doc.PageCount ?? 1);
        cmd.Parameters.AddWithValue("ocr_lang", doc.OcrLanguage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("ocr_duration", doc.OcrDurationMs ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("class_conf", doc.ClassificationConfidence ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("is_dup", doc.IsDuplicate ? 1 : 0);
        cmd.Parameters.AddWithValue("dup_of", doc.DuplicateOf ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("tags", doc.Tags ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("custom", doc.CustomFields ?? (object)DBNull.Value);
    }

    private static string RequiredTextOrFallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string BuildContainsQuery(string query)
    {
        var normalized = query.Trim();
        if (normalized.Length == 0)
            return "\"\"";

        // Keep it simple and safe for Oracle Text parser.
        normalized = normalized.Replace("\"", "\"\"");
        return $"\"{normalized}\"";
    }

    private static Document ReadDocument(OracleDataReader r)
    {
        return new Document
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            Uuid = GetString(r, "uuid"),
            FilePath = GetString(r, "file_path"),
            DocumentType = GetStringNull(r, "document_type"),
            ExtractedDate = GetStringNull(r, "extracted_date"),
            Amounts = GetStringNull(r, "amounts"),
            AccountName = GetStringNullSafe(r, "account_name"),
            AccountNumber = GetStringNullSafe(r, "account_number"),
            TransactionReference = GetStringNullSafe(r, "transaction_reference"),
            Snippet = GetStringNull(r, "snippet"),
            OcrText = GetStringNull(r, "ocr_text"),
            CaptureTime = GetString(r, "capture_time"),
            Source = GetString(r, "source"),
            Engagement = GetString(r, "engagement"),
            Section = GetString(r, "section"),
            ClearingDirection = GetStringNull(r, "clearing_direction"),
            ClearingStatus = GetStringNull(r, "clearing_status"),
#pragma warning disable CS0618 // Legacy notes column for DB compatibility
            Notes = GetStringNull(r, "notes"),
#pragma warning restore CS0618
            Confidence = GetFloatNull(r, "confidence"),
            Status = GetString(r, "status"),
            ReviewedAt = GetStringNull(r, "reviewed_at"),
            UpdatedAt = GetStringNull(r, "updated_at"),
            Branch = GetStringNull(r, "branch"),
            Explanation = GetStringNull(r, "explanation"),
            CreatedBy = GetStringNullSafe(r, "created_by"),
            ReviewedBy = GetStringNullSafe(r, "reviewed_by"),
            Category = GetStringNullSafe(r, "category"),
            FileHash = GetStringNullSafe(r, "file_hash"),
            FileSize = GetLongNullSafe(r, "file_size"),
            PageCount = GetIntNullSafe(r, "page_count") ?? 1,
            OcrLanguage = GetStringNullSafe(r, "ocr_language"),
            OcrDurationMs = GetIntNullSafe(r, "ocr_duration_ms"),
            ClassificationConfidence = GetFloatNullSafe(r, "classification_confidence"),
            IsDuplicate = GetIntNullSafe(r, "is_duplicate") == 1,
            DuplicateOf = GetStringNullSafe(r, "duplicate_of"),
            Tags = GetStringNullSafe(r, "tags"),
            CustomFields = GetStringNullSafe(r, "custom_fields"),
            // P0 Archive
            ArchivedAt = GetStringNullSafe(r, "archived_at"),
            ArchivedBy = GetIntNullSafe(r, "archived_by"),
            LegalHold = GetIntNullSafe(r, "legal_hold") == 1,
            LegalHoldReason = GetStringNullSafe(r, "legal_hold_reason"),
            LegalHoldCaseNumber = GetStringNullSafe(r, "legal_hold_case_number"),
            LegalHoldAppliedAt = GetStringNullSafe(r, "legal_hold_applied_at"),
            LegalHoldAppliedBy = GetIntNullSafe(r, "legal_hold_applied_by"),
            RetentionExpiryDate = GetStringNullSafe(r, "retention_expiry_date"),
            IsImmutable = GetIntNullSafe(r, "is_immutable") == 1,
            ImmutableHash = GetStringNullSafe(r, "immutable_hash"),
            ImmutableSince = GetStringNullSafe(r, "immutable_since"),
            HashVerificationCount = GetIntNullSafe(r, "hash_verification_count") ?? 0,
            LastHashVerification = GetStringNullSafe(r, "last_hash_verification"),
            CustodianId = GetIntNullSafe(r, "custodian_id"),
            DisposalStatus = GetStringNullSafe(r, "disposal_status"),
            DisposalRequestedAt = GetStringNullSafe(r, "disposal_requested_at"),
            DisposalRequestedBy = GetIntNullSafe(r, "disposal_requested_by"),
            DisposalApprovedAt = GetStringNullSafe(r, "disposal_approved_at"),
            DisposalApprovedBy = GetIntNullSafe(r, "disposal_approved_by"),
            DisposalRejectedAt = GetStringNullSafe(r, "disposal_rejected_at"),
            DisposalRejectedBy = GetIntNullSafe(r, "disposal_rejected_by"),
            DisposalRejectionReason = GetStringNullSafe(r, "disposal_rejection_reason")
        };
    }

    private static string GetString(OracleDataReader r, string col) => r.IsDBNull(r.GetOrdinal(col)) ? "" : r.GetString(r.GetOrdinal(col));
    private static string? GetStringNull(OracleDataReader r, string col) => r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetString(r.GetOrdinal(col));
    private static float? GetFloatNull(OracleDataReader r, string col) => r.IsDBNull(r.GetOrdinal(col)) ? null : (float?)r.GetDouble(r.GetOrdinal(col));

    // Safe methods that check if column exists
    private static string? GetStringNullSafe(OracleDataReader r, string col)
    {
        try { return HasColumn(r, col) ? GetStringNull(r, col) : null; }
        catch { return null; }
    }

    private static long? GetLongNullSafe(OracleDataReader r, string col)
    {
        try
        {
            if (!HasColumn(r, col)) return null;
            var ordinal = r.GetOrdinal(col);
            return r.IsDBNull(ordinal) ? null : r.GetInt64(ordinal);
        }
        catch { return null; }
    }

    private static int? GetIntNullSafe(OracleDataReader r, string col)
    {
        try
        {
            if (!HasColumn(r, col)) return null;
            var ordinal = r.GetOrdinal(col);
            return r.IsDBNull(ordinal) ? null : r.GetInt32(ordinal);
        }
        catch { return null; }
    }

    private static float? GetFloatNullSafe(OracleDataReader r, string col)
    {
        try
        {
            if (!HasColumn(r, col)) return null;
            return GetFloatNull(r, col);
        }
        catch { return null; }
    }

    private static bool HasColumn(OracleDataReader r, string col)
    {
        for (int i = 0; i < r.FieldCount; i++)
        {
            if (r.GetName(i).Equals(col, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}


