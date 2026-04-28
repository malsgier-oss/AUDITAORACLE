using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Assignment;

/// <summary>
/// Creates in-app notifications when documents are assigned to users.
/// P4: Document Assignment System.
/// </summary>
public interface IAssignmentNotificationService
{
    /// <summary>Notify assignee that document(s) have been assigned to them.</summary>
    void NotifyNewAssignment(int assigneeUserId, int assignmentId, int documentId, string assignedByUsername, int count = 1);
}

public class AssignmentNotificationService : IAssignmentNotificationService
{
    private readonly INotificationStore _store;

    public AssignmentNotificationService(INotificationStore store)
    {
        _store = store;
    }

    public void NotifyNewAssignment(int assigneeUserId, int assignmentId, int documentId, string assignedByUsername, int count = 1)
    {
        var title = count > 1 ? "New assignments" : "New assignment";
        var message = count > 1
            ? $"{count} document(s) assigned by {assignedByUsername}"
            : $"Document assigned by {assignedByUsername}";
        _store.Create(new Notification
        {
            UserId = assigneeUserId,
            Type = "DocumentAssigned",
            Title = title,
            Message = message,
            EntityType = "DocumentAssignment",
            EntityId = assignmentId
        });
    }
}
