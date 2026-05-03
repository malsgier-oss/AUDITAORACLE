using System.Globalization;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Security;

/// <summary>
/// Service for logging all auditable actions in the system.
/// Provides complete audit trail for compliance.
/// </summary>
public interface IAuditTrailService
{
    Task LogAsync(string action, string category, string entityType, string? entityId,
        string? oldValue = null, string? newValue = null, string? details = null, bool success = true, string? errorDetail = null);

    Task LogDocumentActionAsync(string action, Document document, string? details = null,
        string? oldValue = null, string? newValue = null);
    Task LogUserActionAsync(string action, User user, string? details = null);
    Task LogSystemActionAsync(string action, string? details = null);

    Task<List<AuditLogEntry>> GetLogsAsync(
        DateTime? from = null, DateTime? rangeEnd = null,
        string? userId = null, string? action = null, string? category = null,
        int limit = 1000);

    Task<List<AuditLogEntry>> GetDocumentHistoryAsync(string documentId);
    Task<List<AuditLogEntry>> GetUserActivityAsync(string userId, int days = 30);
}

public class AuditTrailService : IAuditTrailService
{
    private readonly ILogger _log = LoggingService.ForContext<AuditTrailService>();
    private readonly IAuditLogStore _auditStore;
    private readonly Func<ISessionService> _sessionServiceFactory;

    public AuditTrailService(IAuditLogStore auditStore, Func<ISessionService>? sessionServiceFactory = null)
    {
        _auditStore = auditStore;
        _sessionServiceFactory = sessionServiceFactory ?? (() => null!);
    }

    public Task LogAsync(string action, string category, string entityType, string? entityId,
        string? oldValue = null, string? newValue = null, string? details = null, bool success = true, string? errorDetail = null)
    {
        var session = GetCurrentSession();

        var entry = new AuditLogEntry
        {
            Uuid = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow.ToString("O"),
            UserId = session?.UserId.ToString(CultureInfo.InvariantCulture) ?? "system",
            Username = session?.Username ?? "system",
            UserRole = session?.UserRole ?? "system",
            Action = action,
            Category = category,
            EntityType = entityType,
            EntityId = entityId,
            OldValue = oldValue,
            NewValue = newValue,
            Details = details,
            Success = success,
            ErrorMessage = errorDetail
        };

        try
        {
            _auditStore.Insert(entry);
            _log.Debug("Audit logged: {Action} on {EntityType} by {Username}",
                action, entityType, entry.Username);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to write audit log: {Action}", action);
        }

        return Task.CompletedTask;
    }

    public Task LogDocumentActionAsync(string action, Document document, string? details = null,
        string? oldValue = null, string? newValue = null)
    {
        return LogAsync(
            action,
            AuditCategory.Document,
            "Document",
            document.Uuid,
            oldValue: oldValue,
            newValue: newValue,
            details: details ?? $"Document: {document.DocumentType ?? "Unknown"}, Status: {document.Status}"
        );
    }

    public Task LogUserActionAsync(string action, User user, string? details = null)
    {
        return LogAsync(
            action,
            AuditCategory.User,
            "User",
            user.Uuid,
            details: details ?? $"User: {user.Username}, Role: {user.Role}"
        );
    }

    public Task LogSystemActionAsync(string action, string? details = null)
    {
        return LogAsync(
            action,
            AuditCategory.System,
            "System",
            null,
            details: details
        );
    }

    public Task<List<AuditLogEntry>> GetLogsAsync(
        DateTime? from = null, DateTime? rangeEnd = null,
        string? userId = null, string? action = null, string? category = null,
        int limit = 1000)
    {
        var logs = _auditStore.Query(from, rangeEnd, userId, action, category, archivedOnly: false, limit);
        return Task.FromResult(logs);
    }

    public Task<List<AuditLogEntry>> GetDocumentHistoryAsync(string documentId)
    {
        var logs = _auditStore.GetByEntityId("Document", documentId);
        return Task.FromResult(logs);
    }

    public Task<List<AuditLogEntry>> GetUserActivityAsync(string userId, int days = 30)
    {
        var from = DateTime.UtcNow.AddDays(-days);
        var logs = _auditStore.Query(from, null, userId, null, null, false, limit: 1000);
        return Task.FromResult(logs);
    }

    private Session? GetCurrentSession()
    {
        try
        {
            var sessionService = _sessionServiceFactory();
            return sessionService?.CurrentSession;
        }
        catch
        {
            return null;
        }
    }
}
