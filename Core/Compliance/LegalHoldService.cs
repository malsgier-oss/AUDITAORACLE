using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Compliance;

/// <summary>
/// Legal hold management for archived documents.
/// Prevents disposal of documents under legal hold (litigation/investigation).
/// </summary>
public interface ILegalHoldService
{
    /// <summary>Apply legal hold to document(s). Case number and reason required.</summary>
    Task<int> ApplyLegalHoldAsync(IEnumerable<Document> documents, string caseNumber, string reason);

    /// <summary>Release legal hold from document(s).</summary>
    Task<int> ReleaseLegalHoldAsync(IEnumerable<Document> documents);

    /// <summary>Check if document can be disposed (not under legal hold).</summary>
    bool CanDispose(Document doc);
}

public class LegalHoldService : ILegalHoldService
{
    private readonly ILogger _log = LoggingService.ForContext<LegalHoldService>();
    private readonly IDocumentStore _documentStore;
    private readonly IAuditTrailService _auditTrail;
    private readonly ISessionService _sessionService;
    private readonly IPermissionService _permissionService;
    private readonly INotificationService? _notificationService;

    public LegalHoldService(IDocumentStore documentStore, IAuditTrailService auditTrail, ISessionService sessionService, IPermissionService permissionService, INotificationService? notificationService = null)
    {
        _documentStore = documentStore;
        _auditTrail = auditTrail;
        _sessionService = sessionService;
        _permissionService = permissionService;
        _notificationService = notificationService;
    }

    public async Task<int> ApplyLegalHoldAsync(IEnumerable<Document> documents, string caseNumber, string reason)
    {
        if (!_permissionService.HasPermission(Permissions.ArchiveLegalHold))
            throw new UnauthorizedAccessException("Permission denied: archive:legal_hold required to apply legal hold.");

        if (string.IsNullOrWhiteSpace(caseNumber))
            throw new ArgumentException("Case number is required", nameof(caseNumber));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required", nameof(reason));

        var session = _sessionService.CurrentSession;
        var userId = session?.UserId ?? 0;
        var now = DateTime.UtcNow.ToString("O");

        var updated = 0;
        foreach (var doc in documents)
        {
            if (doc.LegalHold)
                continue;

            doc.LegalHold = true;
            doc.LegalHoldCaseNumber = caseNumber.Trim();
            doc.LegalHoldReason = reason.Trim();
            doc.LegalHoldAppliedAt = now;
            doc.LegalHoldAppliedBy = userId;

            if (_documentStore.Update(doc))
            {
                updated++;
                await _auditTrail.LogDocumentActionAsync(AuditAction.LegalHoldApplied, doc,
                    $"Case: {caseNumber}, Reason: {reason}");
                if (doc.CustodianId.HasValue && _notificationService != null)
                    _notificationService.NotifyLegalHold(doc.CustodianId.Value, doc.Id, caseNumber.Trim());
            }
        }

        if (updated > 0)
            _log.Information("Applied legal hold to {Count} document(s), case {Case}", updated, caseNumber);

        return updated;
    }

    public async Task<int> ReleaseLegalHoldAsync(IEnumerable<Document> documents)
    {
        if (!_permissionService.HasPermission(Permissions.ArchiveLegalHold))
            throw new UnauthorizedAccessException("Permission denied: archive:legal_hold required to release legal hold.");

        var updated = 0;
        foreach (var doc in documents)
        {
            if (!doc.LegalHold)
                continue;

            var caseNum = doc.LegalHoldCaseNumber ?? "unknown";
            doc.LegalHold = false;
            doc.LegalHoldCaseNumber = null;
            doc.LegalHoldReason = null;
            doc.LegalHoldAppliedAt = null;
            doc.LegalHoldAppliedBy = null;

            if (_documentStore.Update(doc))
            {
                updated++;
                await _auditTrail.LogDocumentActionAsync(AuditAction.LegalHoldReleased, doc,
                    $"Released from case: {caseNum}");
            }
        }

        if (updated > 0)
            _log.Information("Released legal hold from {Count} document(s)", updated);

        return updated;
    }

    public bool CanDispose(Document doc) => !doc.LegalHold;
}
