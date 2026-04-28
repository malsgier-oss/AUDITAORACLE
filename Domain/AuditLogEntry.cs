namespace WorkAudit.Domain;

/// <summary>
/// Audit log entry for tracking all system actions.
/// Provides complete audit trail for compliance.
/// </summary>
public class AuditLogEntry
{
    public long Id { get; set; }
    public string Uuid { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string UserRole { get; set; } = "";
    public string Action { get; set; } = "";
    public string Category { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public string? EntityName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? IpAddress { get; set; }
    public string? Details { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Audit action categories.
/// </summary>
public static class AuditCategory
{
    public const string Authentication = "Authentication";
    public const string Authorization = "Authorization";
    public const string Document = "Document";
    public const string Notes = "Notes";
    public const string User = "User";
    public const string Settings = "Settings";
    public const string Report = "Report";
    public const string Export = "Export";
    public const string System = "System";
}

/// <summary>
/// Audit action types.
/// </summary>
public static class AuditAction
{
    // Authentication
    public const string Login = "Login";
    public const string LoginEmergencyCode = "LoginEmergencyCode";
    public const string Logout = "Logout";
    public const string LoginFailed = "LoginFailed";
    public const string PasswordChanged = "PasswordChanged";
    public const string SessionExpired = "SessionExpired";

    // Document
    public const string DocumentAssigned = "DocumentAssigned";
    public const string AssignmentStarted = "AssignmentStarted";
    public const string AssignmentCompleted = "AssignmentCompleted";
    public const string AssignmentCancelled = "AssignmentCancelled";
    public const string TeamTaskCreated = "TeamTaskCreated";
    public const string TeamTaskUpdated = "TeamTaskUpdated";
    public const string TeamTaskDeleted = "TeamTaskDeleted";
    public const string TeamTaskCompletionToggled = "TeamTaskCompletionToggled";
    public const string DocumentCreated = "DocumentCreated";
    public const string DocumentViewed = "DocumentViewed";
    public const string DocumentUpdated = "DocumentUpdated";
    public const string DocumentDeleted = "DocumentDeleted";
    public const string DocumentStatusChanged = "DocumentStatusChanged";
    public const string DocumentExported = "DocumentExported";
    public const string DocumentClassified = "DocumentClassified";
    public const string DocumentOcrProcessed = "DocumentOcrProcessed";
    public const string DocumentMarkupSaved = "DocumentMarkupSaved";

    // P0 Archive
    public const string DocumentArchived = "DocumentArchived";
    public const string LegalHoldApplied = "LegalHoldApplied";
    public const string LegalHoldReleased = "LegalHoldReleased";
    public const string ArchiveExported = "ArchiveExported";
    public const string HashVerificationFailed = "HashVerificationFailed";

    // Notes
    public const string NoteCreated = "NoteCreated";
    public const string NoteUpdated = "NoteUpdated";
    public const string NoteStatusChanged = "NoteStatusChanged";
    public const string NoteResolved = "NoteResolved";
    public const string NoteReopened = "NoteReopened";

    // User
    public const string UserCreated = "UserCreated";
    public const string UserUpdated = "UserUpdated";
    public const string UserDeleted = "UserDeleted";
    public const string UserLocked = "UserLocked";
    public const string UserUnlocked = "UserUnlocked";
    public const string UserActivated = "UserActivated";
    public const string UserDeactivated = "UserDeactivated";
    public const string PasswordReset = "PasswordReset";
    public const string EmergencyCodesRegenerated = "EmergencyCodesRegenerated";
    public const string RoleChanged = "RoleChanged";

    // Report
    public const string ReportReviewed = "ReportReviewed";
    public const string ReportApproved = "ReportApproved";

    // System
    public const string ApplicationStarted = "ApplicationStarted";
    public const string ApplicationShutdown = "ApplicationShutdown";
    public const string BackupCreated = "BackupCreated";
    public const string BackupRestored = "BackupRestored";
    public const string SettingsChanged = "SettingsChanged";
    public const string ReportGenerated = "ReportGenerated";
}
