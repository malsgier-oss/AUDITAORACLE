using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Compliance;

public interface INotificationService
{
    void NotifyExpiringSoon(int userId, int documentId, string documentType, string expiryDate);
    void NotifyLegalHold(int userId, int documentId, string caseNumber);
    void NotifyDisposalPending(int userId, int count);
    List<Notification> GetUserNotifications(int userId, bool unreadOnly = false);
    int GetUnreadCount(int userId);
    void MarkRead(int notificationId);
    void MarkAllRead(int userId);
}

public class NotificationService : INotificationService
{
    private readonly INotificationStore _store;

    public NotificationService(INotificationStore store)
    {
        _store = store;
    }

    public void NotifyExpiringSoon(int userId, int documentId, string documentType, string expiryDate)
    {
        _store.Create(new Notification
        {
            UserId = userId,
            Type = "ExpiringSoon",
            Title = "Document expiring soon",
            Message = $"{documentType} expires on {expiryDate}",
            EntityType = "Document",
            EntityId = documentId
        });
    }

    public void NotifyLegalHold(int userId, int documentId, string caseNumber)
    {
        _store.Create(new Notification
        {
            UserId = userId,
            Type = "LegalHold",
            Title = "Legal hold applied",
            Message = $"Case {caseNumber}",
            EntityType = "Document",
            EntityId = documentId
        });
    }

    public void NotifyDisposalPending(int userId, int count)
    {
        _store.Create(new Notification
        {
            UserId = userId,
            Type = "DisposalPending",
            Title = "Disposal approval required",
            Message = $"{count} document(s) pending disposal approval"
        });
    }

    public List<Notification> GetUserNotifications(int userId, bool unreadOnly = false)
        => _store.GetByUser(userId, unreadOnly);

    public int GetUnreadCount(int userId) => _store.GetUnreadCount(userId);

    public void MarkRead(int notificationId) => _store.MarkRead(notificationId);

    public void MarkAllRead(int userId) => _store.MarkAllRead(userId);
}
