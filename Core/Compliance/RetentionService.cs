using System.Globalization;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Compliance;

/// <summary>
/// Data retention policy: identify and auto-archive documents older than the retention period.
/// Phase 7.2 Compliance: Auto-archive old documents.
/// </summary>
public interface IRetentionService
{
    /// <summary>Retention period in days (e.g. 2555 = 7 years for audit).</summary>
    int RetentionDays { get; set; }

    /// <summary>Documents with capture_time older than (now - RetentionDays).</summary>
    List<Document> GetDocumentsBeyondRetention(int? limit = null);

    /// <summary>Count of documents beyond retention.</summary>
    int CountBeyondRetention();

    /// <summary>Mark documents beyond retention as Archived. Returns number updated.</summary>
    int ArchiveDocumentsBeyondRetention(int? limit = null);
}

public class RetentionService : IRetentionService
{
    private readonly ILogger _log = LoggingService.ForContext<RetentionService>();
    private readonly IDocumentStore _documentStore;

    public RetentionService(IDocumentStore documentStore)
    {
        _documentStore = documentStore;
    }

    /// <inheritdoc />
    public int RetentionDays { get; set; } = 2555; // 7 years default for audit

    public List<Document> GetDocumentsBeyondRetention(int? limit = null)
    {
        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var list = _documentStore.ListDocuments(
            status: null,
            dateFrom: null,
            dateTo: cutoff,
            limit: limit ?? 5000);

        return list.Where(d => IsBeyondRetention(d)).ToList();
    }

    public int CountBeyondRetention()
    {
        return GetDocumentsBeyondRetention(limit: 50_000).Count;
    }

    public int ArchiveDocumentsBeyondRetention(int? limit = null)
    {
        var beyond = GetDocumentsBeyondRetention(limit);
        var updated = 0;
        foreach (var doc in beyond)
        {
            if (doc.Status == Enums.Status.Archived) continue;
            doc.Status = Enums.Status.Archived;
            if (_documentStore.Update(doc))
                updated++;
        }
        if (updated > 0)
            _log.Information("Archived {Count} documents beyond retention ({RetentionDays} days)", updated, RetentionDays);
        return updated;
    }

    private bool IsBeyondRetention(Document doc)
    {
        if (string.IsNullOrEmpty(doc.CaptureTime)) return false;
        if (!DateTime.TryParse(doc.CaptureTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var capture))
            return false;
        return capture < DateTime.UtcNow.AddDays(-RetentionDays);
    }
}
