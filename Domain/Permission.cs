namespace WorkAudit.Domain;

/// <summary>
/// Permission definitions for RBAC system.
/// </summary>
public static class Permissions
{
    // Document Permissions
    public const string DocumentView = "document:view";
    public const string DocumentCreate = "document:create";
    public const string DocumentEdit = "document:edit";
    public const string DocumentDelete = "document:delete";
    public const string DocumentExport = "document:export";
    public const string DocumentClassify = "document:classify";
    public const string DocumentApprove = "document:approve";

    // User Permissions
    public const string UserView = "user:view";
    public const string UserCreate = "user:create";
    public const string UserEdit = "user:edit";
    public const string UserDelete = "user:delete";
    public const string UserManageRoles = "user:manage_roles";

    // Report Permissions
    public const string ReportView = "report:view";
    public const string ReportGenerate = "report:generate";
    public const string ReportExport = "report:export";

    // P0 Archive Permissions
    public const string ArchiveView = "archive:view";
    public const string ArchiveCreate = "archive:create";
    public const string ArchiveExport = "archive:export";
    public const string ArchiveLegalHold = "archive:legal_hold";
    public const string ArchiveDispose = "archive:dispose";
    public const string ArchiveDelete = "archive:delete";

    // System Permissions
    public const string SettingsView = "settings:view";
    public const string SettingsEdit = "settings:edit";
    public const string AuditLogView = "audit:view";
    public const string AuditLogExport = "audit:export";
    public const string BackupCreate = "backup:create";
    public const string BackupRestore = "backup:restore";

    /// <summary>Define and assign recurring team checklist tasks (Manager+).</summary>
    public const string TeamTasksManage = "team_tasks:manage";

    /// <summary>
    /// Get permissions for a specific role.
    /// </summary>
    public static string[] GetRolePermissions(string role)
    {
        return role switch
        {
            Roles.Viewer => new[]
            {
                DocumentView,
                ReportView,
                ArchiveView
            },
            Roles.Auditor => new[]
            {
                DocumentView, DocumentCreate, DocumentEdit, DocumentDelete, DocumentClassify,
                ReportView,
                ArchiveView, ArchiveCreate, ArchiveExport
            },
            Roles.Reviewer => new[]
            {
                DocumentView, DocumentCreate, DocumentEdit, DocumentDelete, DocumentClassify, DocumentApprove,
                ReportView, ReportGenerate,
                ArchiveView, ArchiveCreate, ArchiveExport
            },
            Roles.Manager => new[]
            {
                DocumentView, DocumentCreate, DocumentEdit, DocumentDelete, DocumentExport, DocumentClassify, DocumentApprove,
                UserView,
                ReportView, ReportGenerate, ReportExport,
                SettingsView,
                AuditLogView,
                ArchiveView, ArchiveCreate, ArchiveExport, ArchiveLegalHold, ArchiveDispose,
                TeamTasksManage
            },
            Roles.Administrator => new[]
            {
                // All permissions
                DocumentView, DocumentCreate, DocumentEdit, DocumentDelete, DocumentExport, DocumentClassify, DocumentApprove,
                UserView, UserCreate, UserEdit, UserDelete, UserManageRoles,
                ReportView, ReportGenerate, ReportExport,
                SettingsView, SettingsEdit,
                AuditLogView, AuditLogExport,
                BackupCreate, BackupRestore,
                ArchiveView, ArchiveCreate, ArchiveExport, ArchiveLegalHold, ArchiveDispose, ArchiveDelete,
                TeamTasksManage
            },
            _ => Array.Empty<string>()
        };
    }

    /// <summary>
    /// Check if a role has a specific permission.
    /// </summary>
    public static bool RoleHasPermission(string role, string permission)
    {
        var permissions = GetRolePermissions(role);
        return permissions.Contains(permission);
    }
}
