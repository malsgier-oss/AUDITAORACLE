using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Assignment;

/// <summary>
/// Service for document assignment workflow. P4: Document Assignment System.
/// </summary>
public interface IDocumentAssignmentService
{
    IReadOnlyList<DocumentAssignment> GetMyAssignments(string? status = null, bool overdueOnly = false);
    IReadOnlyList<DocumentAssignment> GetAllAssignments(string? username = null, string? status = null);
    DocumentAssignment? Assign(Document doc, User assignTo, User assignedBy, DateTime? dueDate = null, string priority = AssignmentPriority.Normal, string? notes = null, bool notify = true);
    void AssignMany(IEnumerable<Document> docs, User assignTo, User assignedBy, DateTime? dueDate = null, string priority = AssignmentPriority.Normal, string? notes = null);
    bool StartAssignment(int assignmentId, User user);
    bool CompleteAssignment(int assignmentId, User user, string? completionNotes = null);
    bool CancelAssignment(int assignmentId, User user);
    bool ReassignTo(int assignmentId, User newAssignee, User performedBy);
    bool IsOverdue(DocumentAssignment a);
}

public class DocumentAssignmentService : IDocumentAssignmentService
{
    private readonly ILogger _log = LoggingService.ForContext<DocumentAssignmentService>();
    private readonly IDocumentAssignmentStore _store;
    private readonly IUserStore _userStore;
    private readonly IAuditTrailService _auditTrail;
    private readonly IAssignmentNotificationService? _notificationService;

    public DocumentAssignmentService(IDocumentAssignmentStore store, IUserStore userStore, IAuditTrailService auditTrail, IAssignmentNotificationService? notificationService = null)
    {
        _store = store;
        _userStore = userStore;
        _auditTrail = auditTrail;
        _notificationService = notificationService;
    }

    public IReadOnlyList<DocumentAssignment> GetMyAssignments(string? status = null, bool overdueOnly = false)
    {
        var user = GetCurrentUser();
        if (user == null) return [];
        return _store.ListByUser(user.Id, status, overdueOnly);
    }

    public IReadOnlyList<DocumentAssignment> GetAllAssignments(string? username = null, string? status = null)
    {
        return _store.ListAll(username, status);
    }

    public DocumentAssignment? Assign(Document doc, User assignTo, User assignedBy, DateTime? dueDate = null, string priority = AssignmentPriority.Normal, string? notes = null, bool notify = true)
    {
        var a = new DocumentAssignment
        {
            DocumentId = doc.Id,
            DocumentUuid = doc.Uuid,
            AssignedToUserId = assignTo.Id,
            AssignedToUsername = assignTo.DisplayName ?? assignTo.Username,
            AssignedByUserId = assignedBy.Id,
            AssignedByUsername = assignedBy.DisplayName ?? assignedBy.Username,
            DueDate = dueDate?.ToString("yyyy-MM-dd"),
            Priority = priority,
            Notes = notes
        };
        _store.Insert(a);

        _ = _auditTrail.LogAsync(
            AuditAction.DocumentAssigned,
            AuditCategory.Document,
            "DocumentAssignment",
            a.Uuid,
            null,
            doc.FilePath,
            $"Assigned to {assignTo.Username}, due {a.DueDate ?? "N/A"}",
            true);

        if (notify)
            _notificationService?.NotifyNewAssignment(assignTo.Id, a.Id, doc.Id, assignedBy.DisplayName ?? assignedBy.Username, 1);

        _log.Information("Document {DocId} assigned to {User} by {By}", doc.Id, assignTo.Username, assignedBy.Username);
        return a;
    }

    public void AssignMany(IEnumerable<Document> docs, User assignTo, User assignedBy, DateTime? dueDate = null, string priority = AssignmentPriority.Normal, string? notes = null)
    {
        var list = docs.ToList();
        if (list.Count == 0) return;
        if (list.Count == 1)
        {
            Assign(list[0], assignTo, assignedBy, dueDate, priority, notes);
            return;
        }
        DocumentAssignment? last = null;
        foreach (var doc in list)
            last = Assign(doc, assignTo, assignedBy, dueDate, priority, notes, notify: false);
        if (last != null)
            _notificationService?.NotifyNewAssignment(assignTo.Id, last.Id, list[0].Id, assignedBy.DisplayName ?? assignedBy.Username, list.Count);
    }

    public bool StartAssignment(int assignmentId, User user)
    {
        var gr = _store.GetResult(assignmentId);
        if (!gr.IsSuccess)
        {
            _log.Warning("StartAssignment: could not load assignment {Id}: {Error}", assignmentId, gr.Error);
            return false;
        }
        var a = gr.Value!;
        if (a.AssignedToUserId != user.Id || a.Status != AssignmentStatus.Pending)
            return false;

        var now = DateTime.UtcNow.ToString("O");
        if (!_store.UpdateStatus(assignmentId, AssignmentStatus.InProgress, startedAt: now))
            return false;

        _ = _auditTrail.LogAsync(AuditAction.AssignmentStarted, AuditCategory.Document, "DocumentAssignment", a.Uuid, null, now, $"Started by {user.Username}", true);
        return true;
    }

    public bool CompleteAssignment(int assignmentId, User user, string? completionNotes = null)
    {
        var gr = _store.GetResult(assignmentId);
        if (!gr.IsSuccess)
        {
            _log.Warning("CompleteAssignment: could not load assignment {Id}: {Error}", assignmentId, gr.Error);
            return false;
        }
        var a = gr.Value!;
        if (a.AssignedToUserId != user.Id || a.Status == AssignmentStatus.Completed || a.Status == AssignmentStatus.Cancelled)
            return false;

        var now = DateTime.UtcNow.ToString("O");
        if (!_store.UpdateStatus(assignmentId, AssignmentStatus.Completed, completedAt: now, completionNotes: completionNotes))
            return false;

        _ = _auditTrail.LogAsync(AuditAction.AssignmentCompleted, AuditCategory.Document, "DocumentAssignment", a.Uuid, null, now, completionNotes ?? "Completed", true);
        return true;
    }

    public bool CancelAssignment(int assignmentId, User user)
    {
        var gr = _store.GetResult(assignmentId);
        if (!gr.IsSuccess)
        {
            _log.Warning("CancelAssignment: could not load assignment {Id}: {Error}", assignmentId, gr.Error);
            return false;
        }
        var a = gr.Value!;
        if (a.AssignedToUserId != user.Id && a.AssignedByUserId != user.Id)
        {
            if (!Roles.HasMinimumRole(user.Role, Roles.Manager))
                return false;
        }

        if (!_store.Cancel(assignmentId))
            return false;

        _ = _auditTrail.LogAsync(AuditAction.AssignmentCancelled, AuditCategory.Document, "DocumentAssignment", a.Uuid, null, null, $"Cancelled by {user.Username}", true);
        return true;
    }

    public bool ReassignTo(int assignmentId, User newAssignee, User performedBy)
    {
        var gr = _store.GetResult(assignmentId);
        if (!gr.IsSuccess)
        {
            _log.Warning("ReassignTo: could not load assignment {Id}: {Error}", assignmentId, gr.Error);
            return false;
        }
        var a = gr.Value!;
        if (a.Status == AssignmentStatus.Completed || a.Status == AssignmentStatus.Cancelled)
            return false;
        if (!Roles.HasMinimumRole(performedBy.Role, Roles.Manager))
            return false;

        if (!_store.UpdateAssignedTo(assignmentId, newAssignee.Id, newAssignee.DisplayName ?? newAssignee.Username))
            return false;

        _ = _auditTrail.LogAsync(
            AuditAction.DocumentAssigned,
            AuditCategory.Document,
            "DocumentAssignment",
            a.Uuid,
            a.AssignedToUsername,
            newAssignee.Username,
            $"Reassigned from {a.AssignedToUsername} to {newAssignee.Username} by {performedBy.Username}",
            true);
        _log.Information("Assignment {Id} reassigned from {From} to {To} by {By}", assignmentId, a.AssignedToUsername, newAssignee.Username, performedBy.Username);
        return true;
    }

    public bool IsOverdue(DocumentAssignment a)
    {
        if (string.IsNullOrEmpty(a.DueDate) || a.Status == AssignmentStatus.Completed || a.Status == AssignmentStatus.Cancelled)
            return false;
        return DateTime.TryParse(a.DueDate, out var due) && due.Date < DateTime.Today;
    }

    private static User? GetCurrentUser()
    {
        if (!ServiceContainer.IsInitialized) return null;
        var session = ServiceContainer.GetService<ISessionService>();
        return session?.CurrentUser;
    }
}
