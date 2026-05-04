using System.Globalization;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

public interface IActivityTracker
{
    ActivitySummary GetRecentActivity();
}

public sealed class ActivityTracker : IActivityTracker
{
    private readonly IDocumentStore _documentStore;
    private readonly IReportHistoryStore _reportHistory;
    private readonly IBackupService _backupService;
    private readonly IAuditLogStore _auditLogStore;

    public ActivityTracker(
        IDocumentStore documentStore,
        IReportHistoryStore reportHistory,
        IBackupService backupService,
        IAuditLogStore auditLogStore)
    {
        _documentStore = documentStore;
        _reportHistory = reportHistory;
        _backupService = backupService;
        _auditLogStore = auditLogStore;
    }

    public ActivitySummary GetRecentActivity()
    {
        var summary = new ActivitySummary();
        var stats = _documentStore.GetStats();
        summary.DocumentsImportedToday = stats.TodayCount;

        var newest = _documentStore.ListDocuments(limit: 1, newestFirst: true);
        if (newest.Count > 0 && TryParseUtc(newest[0].CaptureTime, out var cap))
            summary.LastDocumentImportUtc = cap;

        var withOcr = _documentStore.ListDocuments(limit: 80, newestFirst: true)
            .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.OcrText));
        if (withOcr != null && TryParseUtc(withOcr.UpdatedAt ?? withOcr.CaptureTime, out var ocrTs))
            summary.LastOcrCompletionUtc = ocrTs;

        var backups = _backupService.GetBackupHistory();
        if (backups.Count > 0)
        {
            summary.LastBackupUtc = backups[0].CreatedAt;
            summary.LastBackupStatus = "OK";
        }

        var reports = _reportHistory.List(null, null, 1, null);
        if (reports.Count > 0)
        {
            if (TryParseUtc(reports[0].GeneratedAt, out var rpt))
                summary.LastReportGeneratedUtc = rpt;
            summary.LastReportType = reports[0].ReportType;
        }

        var todayStart = DateTime.UtcNow.Date;
        var logins = _auditLogStore.Query(todayStart, null, null, AuditAction.Login, AuditCategory.Authentication, false, 500, 0);
        summary.ActiveUsersToday = logins.Select(e => e.Username).Where(u => !string.IsNullOrEmpty(u)).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        return summary;
    }

    private static bool TryParseUtc(string? iso, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(iso)) return false;
        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            utc = dt.ToUniversalTime();
            return true;
        }

        return false;
    }
}
