using System.Globalization;
using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Domain;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage.Oracle.Migrations;

/// <summary>
/// Seeds a synthetic <c>documents</c> row so daily journal notes satisfy <c>NOTES</c> FK and NOT NULL
/// <c>document_uuid</c> (Oracle stores empty string as NULL).
/// </summary>
internal sealed class Migration_057_JournalAnchorDocument : IOracleMigration
{
    public int Version => 57;

    public string Name => "Journal anchor document for notes";

    public void Apply(OracleConnection connection, OracleTransaction transaction, ILogger log)
    {
        var nowIso = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        const string sentinel = "__WORKAUDIT_JOURNAL_ANCHOR__";

        using var cmd = OracleSql.CreateCommand(connection, """
            INSERT INTO documents (
                uuid,
                file_path,
                capture_time,
                source,
                engagement,
                section,
                status,
                updated_at)
            SELECT :uuid, :fp, :ct, :src, :eng, :sec, :status, :ua
            FROM DUAL
            WHERE NOT EXISTS (SELECT 1 FROM documents d WHERE d.uuid = :uuid2)
            """);
        cmd.Transaction = transaction;
        OracleSql.AddParameter(cmd, "uuid", NoteAnchors.JournalDocumentUuid);
        OracleSql.AddParameter(cmd, "uuid2", NoteAnchors.JournalDocumentUuid);
        OracleSql.AddParameter(cmd, "fp", sentinel);
        OracleSql.AddParameter(cmd, "ct", nowIso);
        OracleSql.AddParameter(cmd, "src", sentinel);
        OracleSql.AddParameter(cmd, "eng", sentinel);
        OracleSql.AddParameter(cmd, "sec", sentinel);
        OracleSql.AddParameter(cmd, "status", Enums.Status.Archived);
        OracleSql.AddParameter(cmd, "ua", nowIso);

        var inserted = cmd.ExecuteNonQuery();
        if (inserted > 0)
            log.Information("Inserted journal anchor document (uuid={Uuid})", NoteAnchors.JournalDocumentUuid);
        else
            log.Debug("Journal anchor document already present");
    }
}
