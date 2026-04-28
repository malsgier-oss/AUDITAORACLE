using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Domain;

namespace WorkAudit.Storage;

public class MarkupStore : IMarkupStore
{
    private readonly ILogger _log = LoggingService.ForContext<MarkupStore>();
    private readonly string _connectionString;

    public MarkupStore(string dbPath)
    {
        _connectionString = dbPath;
    }

    public List<MarkupRegion> GetByDocumentId(int documentId)
    {
        var list = new List<MarkupRegion>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, document_id, kind, color, norm_x, norm_y, norm_w, norm_h, page_index, preview_surface, note_id, label, created_at, created_by
                            FROM document_markup_regions WHERE document_id = @docId ORDER BY id";
        cmd.Parameters.AddWithValue("@docId", documentId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new MarkupRegion
            {
                Id = r.GetInt64(0),
                DocumentId = r.GetInt32(1),
                Kind = r.GetString(2),
                Color = r.GetString(3),
                NormX = r.GetDouble(4),
                NormY = r.GetDouble(5),
                NormW = r.GetDouble(6),
                NormH = r.GetDouble(7),
                PageIndex = r.GetInt32(8),
                PreviewSurface = r.GetString(9),
                NoteId = r.IsDBNull(10) ? null : r.GetInt32(10),
                Label = r.IsDBNull(11) ? null : r.GetString(11),
                CreatedAt = r.GetString(12),
                CreatedBy = r.GetString(13)
            });
        }
        return list;
    }

    public void ReplaceForDocument(int documentId, IReadOnlyList<MarkupRegion> regions, string createdBy)
    {
        var now = DateTime.UtcNow.ToString("O");
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM document_markup_regions WHERE document_id = @docId";
                del.Parameters.AddWithValue("@docId", documentId);
                del.ExecuteNonQuery();
            }

            foreach (var m in regions)
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = @"INSERT INTO document_markup_regions
                    (document_id, kind, color, norm_x, norm_y, norm_w, norm_h, page_index, preview_surface, note_id, label, created_at, created_by)
                    VALUES (@docId, @kind, @color, @nx, @ny, @nw, @nh, @page, @surface, @noteId, @label, @createdAt, @createdBy)";
                ins.Parameters.AddWithValue("@docId", documentId);
                ins.Parameters.AddWithValue("@kind", m.Kind);
                ins.Parameters.AddWithValue("@color", m.Color);
                ins.Parameters.AddWithValue("@nx", m.NormX);
                ins.Parameters.AddWithValue("@ny", m.NormY);
                ins.Parameters.AddWithValue("@nw", m.NormW);
                ins.Parameters.AddWithValue("@nh", m.NormH);
                ins.Parameters.AddWithValue("@page", m.PageIndex);
                ins.Parameters.AddWithValue("@surface", m.PreviewSurface);
                ins.Parameters.AddWithValue("@noteId", m.NoteId.HasValue ? m.NoteId.Value : DBNull.Value);
                ins.Parameters.AddWithValue("@label", string.IsNullOrEmpty(m.Label) ? DBNull.Value : m.Label);
                ins.Parameters.AddWithValue("@createdAt", string.IsNullOrEmpty(m.CreatedAt) ? now : m.CreatedAt);
                ins.Parameters.AddWithValue("@createdBy", string.IsNullOrEmpty(m.CreatedBy) ? createdBy : m.CreatedBy);
                ins.ExecuteNonQuery();
            }

            tx.Commit();
            _log.Debug("Replaced {Count} markup regions for document {DocId}", regions.Count, documentId);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _log.Error(ex, "ReplaceForDocument failed for {DocId}", documentId);
            throw;
        }
    }
}
