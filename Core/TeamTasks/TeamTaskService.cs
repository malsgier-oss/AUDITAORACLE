using Serilog;
using System.Globalization;
using Oracle.ManagedDataAccess.Client;
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
        if (endDateLocal.HasValue && endDateLocal.Value.Date < startDateLocal.Date)
            throw new ArgumentException("End date cannot be before start date.", nameof(endDateLocal));

        var current = GetCurrentUser();
        if (current == null)
            throw new InvalidOperationException("Not signed in.");

        var assignTo = _userStore.GetById(assignedToUserId)
            ?? throw new ArgumentException("Assignee not found.", nameof(assignedToUserId));

        var startStr = startDateLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string? endStr = endDateLocal?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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

        _log.Information("Creating team task '{Title}' for user {AssigneeId} by {AssignerId} with recurrence {Recurrence}",
            t.Title, t.AssignedToUserId, t.AssignedByUserId, t.Recurrence);

        int id;
        try
        {
            id = _store.Insert(t);
        }
        catch (OracleException ex)
        {
            _log.Error(ex, "Failed to create team task '{Title}' for user {AssigneeId} (ORA-{Code})",
                t.Title, t.AssignedToUserId, ex.Number);
            throw new InvalidOperationException(GetCreateErrorMessage(ex), ex);
        }

        if (id <= 0)
            throw new InvalidOperationException("Failed to save team task.");

        t.Id = id;
        _ = _auditTrail.LogAsync(AuditAction.TeamTaskCreated, AuditCategory.System, "TeamTask", t.Uuid,
            details: $"{t.Title} → {assignTo.Username}, {recurrence}", success: true);
        _log.Information("Team task {Id} created for {User}", id, assignTo.Username);
        return t;
    }

    private static string GetCreateErrorMessage(OracleException ex)
    {
        return ex.Number switch
        {
            1 => "A team task with the same unique value already exists.",
            904 => "Database schema is missing required team task columns. Please run database updates.",
            942 => "Team task tables are missing in the database. Please run database setup/migrations.",
            1017 => "Database authentication failed. Please verify database credentials.",
            2291 => "The selected assignee or assigner no longer exists.",
            3113 or 3114 or 12170 or 12514 or 12541 => "Database connection is unavailable. Please try again.",
            _ => $"Failed to save team task (ORA-{ex.Number}): {ex.Message}"
        };
    }

    public bool Update(TeamTask task)
    {
        RequireManagePermission();
        if (!TeamTaskRecurrence.All.Contains(task.Recurrence))
            throw new ArgumentException("Invalid recurrence.");

        var assignTo = _userStore.GetById(task.AssignedToUserId);
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
        var existing = _store.GetById(id);
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
        var todayStr = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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

        var task = _store.GetById(teamTaskId);
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

        var task = _store.GetById(teamTaskId);
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

        var task = _store.GetById(teamTaskId);
        if (task == null || task.AssignedToUserId != user.Id)
            return null;

        var today = DateTime.Today;
        var todayStr = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
