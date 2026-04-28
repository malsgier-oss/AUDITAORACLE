using Serilog;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.TeamTasks;

public interface ITeamTaskService
{
    IReadOnlyList<TeamTask> ListAllForManagement(int? assignedToUserId = null);
    TeamTask Create(string title, string? description, int assignedToUserId, string recurrence, DateTime startDateLocal,
        DateTime? endDateLocal, bool isActive);
    bool Update(TeamTask task);
    bool Delete(int id);
    IReadOnlyList<TeamTaskWithState> GetMyTasksWithState();
    /// <summary>Toggles completion for the current period. Returns new completed state, or null if forbidden/not found.</summary>
    bool? ToggleCompletion(int teamTaskId);
    /// <summary>Assignee note for the current period, or null if none.</summary>
    string? GetMyNote(int teamTaskId);
    /// <summary>Saves or clears the assignee note for the current period.</summary>
    bool SaveMyNote(int teamTaskId, string? noteText);
}

public class TeamTaskService : ITeamTaskService
{
    private readonly ILogger _log = LoggingService.ForContext<TeamTaskService>();
    private readonly ITeamTaskStore _store;
    private readonly IUserStore _userStore;
    private readonly IPermissionService _permissionService;
    private readonly IAuditTrailService _auditTrail;

    public TeamTaskService(ITeamTaskStore store, IUserStore userStore, IPermissionService permissionService, IAuditTrailService auditTrail)
    {
        _store = store;
        _userStore = userStore;
        _permissionService = permissionService;
        _auditTrail = auditTrail;
    }

    public IReadOnlyList<TeamTask> ListAllForManagement(int? assignedToUserId = null)
    {
        RequireManagePermission();
        return _store.ListAll(assignedToUserId);
    }

    public TeamTask Create(string title, string? description, int assignedToUserId, string recurrence, DateTime startDateLocal,
        DateTime? endDateLocal, bool isActive)
    {
        RequireManagePermission();
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));
        if (!TeamTaskRecurrence.All.Contains(recurrence))
            throw new ArgumentException("Invalid recurrence.", nameof(recurrence));

        var current = GetCurrentUser();
        if (current == null)
            throw new InvalidOperationException("Not signed in.");

        var assignTo = _userStore.Get(assignedToUserId)
            ?? throw new ArgumentException("Assignee not found.", nameof(assignedToUserId));

        var startStr = startDateLocal.ToString("yyyy-MM-dd");
        string? endStr = endDateLocal?.ToString("yyyy-MM-dd");

        var t = new TeamTask
        {
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            AssignedToUserId = assignTo.Id,
            AssignedToUsername = assignTo.DisplayName ?? assignTo.Username,
            AssignedByUserId = current.Id,
            AssignedByUsername = current.DisplayName ?? current.Username,
            Recurrence = recurrence,
            StartDate = startStr,
            EndDate = endStr,
            IsActive = isActive
        };

        var id = _store.Insert(t);
        if (id <= 0)
            throw new InvalidOperationException("Failed to save team task.");

        t.Id = id;
        _ = _auditTrail.LogAsync(AuditAction.TeamTaskCreated, AuditCategory.System, "TeamTask", t.Uuid,
            details: $"{t.Title} → {assignTo.Username}, {recurrence}", success: true);
        _log.Information("Team task {Id} created for {User}", id, assignTo.Username);
        return t;
    }

    public bool Update(TeamTask task)
    {
        RequireManagePermission();
        if (!TeamTaskRecurrence.All.Contains(task.Recurrence))
            throw new ArgumentException("Invalid recurrence.");

        var assignTo = _userStore.Get(task.AssignedToUserId);
        if (assignTo != null)
        {
            task.AssignedToUsername = assignTo.DisplayName ?? assignTo.Username;
        }

        var ok = _store.Update(task);
        if (ok)
            _ = _auditTrail.LogAsync(AuditAction.TeamTaskUpdated, AuditCategory.System, "TeamTask", task.Uuid,
                details: task.Title, success: true);
        return ok;
    }

    public bool Delete(int id)
    {
        RequireManagePermission();
        var existing = _store.Get(id);
        var ok = _store.Delete(id);
        if (ok && existing != null)
            _ = _auditTrail.LogAsync(AuditAction.TeamTaskDeleted, AuditCategory.System, "TeamTask", existing.Uuid,
                details: existing.Title, success: true);
        return ok;
    }

    public IReadOnlyList<TeamTaskWithState> GetMyTasksWithState()
    {
        var user = GetCurrentUser();
        if (user == null)
            return Array.Empty<TeamTaskWithState>();

        var today = DateTime.Today;
        var todayStr = today.ToString("yyyy-MM-dd");
        var tasks = _store.ListActiveForAssignee(user.Id, todayStr);
        var result = new List<TeamTaskWithState>();
        foreach (var t in tasks)
        {
            if (!TeamTaskPeriodHelper.IsInActiveWindow(today, t.StartDate, t.EndDate))
                continue;
            var periodKey = TeamTaskPeriodHelper.GetPeriodKey(today, t.Recurrence);
            var done = _store.HasCompletion(t.Id, periodKey);
            var hasNote = _store.HasNote(t.Id, user.Id, periodKey);
            result.Add(new TeamTaskWithState
            {
                Task = t,
                PeriodKey = periodKey,
                IsCompletedForCurrentPeriod = done,
                HasNoteForCurrentPeriod = hasNote
            });
        }
        return result;
    }

    public string? GetMyNote(int teamTaskId)
    {
        var user = GetCurrentUser();
        if (user == null)
            return null;

        var task = _store.Get(teamTaskId);
        if (task == null || task.AssignedToUserId != user.Id)
            return null;

        var today = DateTime.Today;
        if (!task.IsActive || !TeamTaskPeriodHelper.IsInActiveWindow(today, task.StartDate, task.EndDate))
            return null;

        var periodKey = TeamTaskPeriodHelper.GetPeriodKey(today, task.Recurrence);
        return _store.GetNote(teamTaskId, user.Id, periodKey);
    }

    public bool SaveMyNote(int teamTaskId, string? noteText)
    {
        var user = GetCurrentUser();
        if (user == null)
            return false;

        var task = _store.Get(teamTaskId);
        if (task == null || task.AssignedToUserId != user.Id)
            return false;

        var today = DateTime.Today;
        if (!task.IsActive || !TeamTaskPeriodHelper.IsInActiveWindow(today, task.StartDate, task.EndDate))
            return false;

        var periodKey = TeamTaskPeriodHelper.GetPeriodKey(today, task.Recurrence);
        return _store.SaveNote(teamTaskId, user.Id, periodKey, noteText);
    }

    public bool? ToggleCompletion(int teamTaskId)
    {
        var user = GetCurrentUser();
        if (user == null)
            return null;

        var task = _store.Get(teamTaskId);
        if (task == null || task.AssignedToUserId != user.Id)
            return null;

        var today = DateTime.Today;
        var todayStr = today.ToString("yyyy-MM-dd");
        if (!task.IsActive || !TeamTaskPeriodHelper.IsInActiveWindow(today, task.StartDate, task.EndDate))
            return null;

        var periodKey = TeamTaskPeriodHelper.GetPeriodKey(today, task.Recurrence);
        if (_store.HasCompletion(teamTaskId, periodKey))
        {
            _store.DeleteCompletion(teamTaskId, periodKey);
            _ = _auditTrail.LogAsync(AuditAction.TeamTaskCompletionToggled, AuditCategory.System, "TeamTask", task.Uuid,
                details: $"Unchecked period {periodKey}", success: true);
            return false;
        }

        _store.InsertCompletion(teamTaskId, periodKey);
        _ = _auditTrail.LogAsync(AuditAction.TeamTaskCompletionToggled, AuditCategory.System, "TeamTask", task.Uuid,
            details: $"Checked period {periodKey}", success: true);
        return true;
    }

    private void RequireManagePermission()
    {
        if (!_permissionService.HasPermission(Permissions.TeamTasksManage))
            throw new UnauthorizedAccessException("Team task management requires Manager or Administrator.");
    }

    private static User? GetCurrentUser()
    {
        if (!ServiceContainer.IsInitialized) return null;
        var session = ServiceContainer.GetService<ISessionService>();
        return session?.CurrentUser;
    }
}
