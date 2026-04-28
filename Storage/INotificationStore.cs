namespace WorkAudit.Storage;

public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Type { get; set; } = ""; // ExpiringSoon, LegalHold, DisposalPending, etc.
    public string Title { get; set; } = "";
    public string? Message { get; set; }
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public bool IsRead { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? ReadAt { get; set; }
}

public interface INotificationStore
{
    void Create(Notification n);
    List<Notification> GetByUser(int userId, bool unreadOnly = false, int limit = 50);
    int GetUnreadCount(int userId);
    void MarkRead(int id);
    void MarkAllRead(int userId);
}
