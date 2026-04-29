using System.Globalization;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Compliance;

/// <summary>
/// P0 Archive service: archive documents with retention expiry and application-level immutability.
/// </summary>
public interface IArchiveService
{
    /// <summary>Archive document(s): set status, retention expiry, make immutable, log audit.</summary>
    Task<int> ArchiveDocumentsAsync(IEnumerable<Document> documents);

    /// <summary>Get configured retention period in years (from app_settings, default 7).</summary>
    int GetRetentionYears();
}

public class ArchiveService : IArchiveService
{
    private readonly ILogger _log = LoggingService.ForContext<ArchiveService>();
    private readonly IDocumentStore _documentStore;
    private readonly IImmutabilityService _immutabilityService;
    private readonly IAuditTrailService _auditTrail;
    private readonly ISessionService _sessionService;
    private readonly IConfigStore _configStore;
    private readonly IPermissionService _permissionService;

    private const int DefaultRetentionYears = 7;

    public ArchiveService(
        IDocumentStore documentStore,
        IImmutabilityService immutabilityService,
        IAuditTrailService auditTrail,
        ISessionService sessionService,
        IConfigStore configStore,
        IPermissionService permissionService)
    {
        _documentStore = documentStore;
        _immutabilityService = immutabilityService;
        _auditTrail = auditTrail;
        _sessionService = sessionService;
        _configStore = configStore;
        _permissionService = permissionService;
    }

    public int GetRetentionYears()
    {
        return _configStore.GetSettingInt("archive_retention_years", DefaultRetentionYears);
    }

    public async Task<int> ArchiveDocumentsAsync(IEnumerable<Document> documents)
    {
        if (!_permissionService.HasPermission(Permissions.ArchiveCreate))
            throw new UnauthorizedAccessException("Permission denied: archive:create required to archive documents.");

        var session = _sessionService.CurrentSession;
        var userId = session?.UserId ?? 0;
        var now = DateTime.UtcNow.ToString("O");
        var retentionYears = GetRetentionYears();
        var expiryDate = DateTime.UtcNow.AddYears(retentionYears).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var updated = 0;
        foreach (var doc in documents)
        {
            if (doc.Status == Enums.Status.Archived)
                continue;

            doc.Status = Enums.Status.Archived;
            doc.ArchivedAt = now;
            doc.ArchivedBy = userId;
            doc.RetentionExpiryDate = expiryDate;

            if (_immutabilityService.MakeImmutable(doc))
            {
                if (_documentStore.Update(doc))
                {
                    updated++;
                    await _auditTrail.LogDocumentActionAsync(AuditAction.DocumentArchived, doc,
                        $"Retention: {retentionYears} years, expiry: {expiryDate}");
                }
            }
            else
            {
                // Still archive even if immutability fails (e.g. file not found)
                if (_documentStore.Update(doc))
                {
                    updated++;
                    await _auditTrail.LogDocumentActionAsync(AuditAction.DocumentArchived, doc,
                        $"Retention: {retentionYears} years, expiry: {expiryDate} (immutability not applied)");
                }
            }
        }

        if (updated > 0)
            _log.Information("Archived {Count} document(s)", updated);

        return updated;
    }
}
