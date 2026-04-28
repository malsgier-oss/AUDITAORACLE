using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Security;

/// <summary>
/// Service for checking user permissions based on RBAC.
/// </summary>
public interface IPermissionService
{
    bool HasPermission(string permission);
    bool HasPermission(string userRole, string permission);
    bool HasMinimumRole(string requiredRole);
    bool HasMinimumRole(string userRole, string requiredRole);
    /// <summary>Branch argument for <see cref="IDocumentStore.ListDocuments"/>: null = all branches (Manager+ only); otherwise concrete home branch.</summary>
    string? GetEffectiveDocumentListBranchFilter();
    bool CanAccessDocument(Document document);
    bool CanEditDocument(Document document);
    bool CanDeleteDocument(Document document);
    bool CanApproveDocument(Document document);
    string[] GetCurrentPermissions();
}

public class PermissionService : IPermissionService
{
    private const string ArchiveAccessWindowDaysKey = "archive_access_window_days";
    private const int DefaultArchiveAccessWindowDays = 30;

    private readonly ILogger _log = LoggingService.ForContext<PermissionService>();
    private readonly ISessionService _sessionService;
    private readonly IConfigStore _configStore;
    private readonly IDocumentAssignmentStore _assignmentStore;

    public PermissionService(ISessionService sessionService, IConfigStore configStore, IDocumentAssignmentStore assignmentStore)
    {
        _sessionService = sessionService;
        _configStore = configStore;
        _assignmentStore = assignmentStore;
    }

    public string? GetEffectiveDocumentListBranchFilter()
    {
        if (!_sessionService.IsAuthenticated || _sessionService.CurrentUser == null)
            return Branches.ToConcreteBranchOrDefault(null);
        if (HasMinimumRole(Roles.Manager))
            return null;
        return Branches.ToConcreteBranchOrDefault(_sessionService.CurrentUser.Branch);
    }

    public bool HasPermission(string permission)
    {
        if (!_sessionService.IsAuthenticated)
        {
            _log.Warning("Permission check failed: Not authenticated");
            return false;
        }

        return HasPermission(_sessionService.CurrentUser!.Role, permission);
    }

    public bool HasPermission(string userRole, string permission)
    {
        var hasPermission = Permissions.RoleHasPermission(userRole, permission);
        _log.Debug("Permission check: Role={Role}, Permission={Permission}, Result={Result}",
            userRole, permission, hasPermission);
        return hasPermission;
    }

    public bool HasMinimumRole(string requiredRole)
    {
        if (!_sessionService.IsAuthenticated)
            return false;

        return HasMinimumRole(_sessionService.CurrentUser!.Role, requiredRole);
    }

    public bool HasMinimumRole(string userRole, string requiredRole)
    {
        return Roles.HasMinimumRole(userRole, requiredRole);
    }

    public bool CanAccessDocument(Document document)
    {
        if (!_sessionService.IsAuthenticated)
            return false;

        if (!HasPermission(Permissions.DocumentView))
            return false;

        var user = _sessionService.CurrentUser;
        if (user == null) return false;

        // Manager and Admin bypass branch scope and archive aging gate
        if (HasMinimumRole(Roles.Manager))
            return true;

        // Branch scope: non-managers use concrete home branch; active assignee on this document overrides branch mismatch.
        var userBranch = Branches.ToConcreteBranchOrDefault(user.Branch);
        var docBranch = document.Branch ?? Domain.Branches.Default;
        if (!string.Equals(userBranch, docBranch, StringComparison.OrdinalIgnoreCase))
        {
            if (!HasActiveAssigneeAccess(document.Id, user))
            {
                _log.Debug("Access denied: document branch {DocBranch} does not match user branch {UserBranch} and no active assignment", docBranch, userBranch);
                return false;
            }
        }

        // Archive aging gate: if archived_at is null/missing, treat as NOT archived - allow
        if (string.IsNullOrEmpty(document.ArchivedAt))
            return true;

        // Archived document: Auditor/User can access only if within window
        var windowDays = _configStore.GetSettingInt(ArchiveAccessWindowDaysKey, DefaultArchiveAccessWindowDays);
        if (windowDays <= 0) windowDays = DefaultArchiveAccessWindowDays;

        if (!DateTime.TryParse(document.ArchivedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var archivedAt))
            return true; // unparseable - allow (fail open for edge cases)

        var cutoff = DateTime.UtcNow.AddDays(-windowDays);
        if (archivedAt.ToUniversalTime() < cutoff)
        {
            _log.Debug("Access denied: document {DocId} archived at {ArchivedAt} is outside {WindowDays}-day window", document.Id, document.ArchivedAt, windowDays);
            return false;
        }

        return true;
    }

    private bool HasActiveAssigneeAccess(int documentId, User user)
    {
        if (documentId <= 0) return false;
        foreach (var a in _assignmentStore.ListByDocument(documentId))
        {
            if (a.AssignedToUserId != user.Id) continue;
            if (a.Status == AssignmentStatus.Pending || a.Status == AssignmentStatus.InProgress)
                return true;
        }
        return false;
    }

    public bool CanEditDocument(Document document)
    {
        if (!_sessionService.IsAuthenticated)
            return false;

        // Cannot edit cleared documents unless you're a manager+
        if (document.Status == Enums.Status.Cleared && !HasMinimumRole(Roles.Manager))
        {
            _log.Debug("Cannot edit cleared document without Manager role");
            return false;
        }

        return HasPermission(Permissions.DocumentEdit);
    }

    public bool CanDeleteDocument(Document document)
    {
        if (!_sessionService.IsAuthenticated)
            return false;

        // Cannot delete approved documents
        if (document.Status == Enums.Status.Cleared || document.Status == Enums.Status.ReadyForAudit)
        {
            if (!HasMinimumRole(Roles.Manager))
            {
                _log.Debug("Cannot delete approved/cleared document without Manager role");
                return false;
            }
        }

        return HasPermission(Permissions.DocumentDelete);
    }

    public bool CanApproveDocument(Document document)
    {
        if (!_sessionService.IsAuthenticated)
            return false;

        // Only reviewers and above can approve
        return HasPermission(Permissions.DocumentApprove);
    }

    public string[] GetCurrentPermissions()
    {
        if (!_sessionService.IsAuthenticated)
            return Array.Empty<string>();

        return Permissions.GetRolePermissions(_sessionService.CurrentUser!.Role);
    }
}
