using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Serilog;
using WorkAudit.Core.Notes;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;
using WorkAudit.ViewModels;
using WorkAudit.Views.Admin;

namespace WorkAudit.Views;

public partial class DashboardView : UserControl
{
    private readonly ILogger _log = LoggingService.ForContext<DashboardView>();
    private readonly DashboardViewModel _viewModel;
    private readonly IDocumentStore _store;
    private readonly INotesStore _notesStore;
    private readonly IDocumentAssignmentStore _assignmentStore;
    private readonly IUserStore _userStore;
    private readonly INoteDocumentStatusSync _noteStatusSync;
    private readonly IPermissionService _permissionService;
    private readonly IAuditTrailService _auditTrailService;
    private readonly IIntelligenceService _intelligenceService;
    private readonly DispatcherTimer _refreshTimer;
    private DashboardData _dashboardData = new();
    /// <summary>Avoids reloading the dashboard from <see cref="DashboardViewModel.SelectedTimeRange"/> binding until the first load has finished.</summary>
    private bool _timeRangeReloadEnabled;
    private bool _suppressScopeFilterChanged;
    private const string AllFilterLabel = "All";
    private const int DashboardAssignmentListLimit = 10000;

    public DashboardView()
    {
        InitializeComponent();
        _viewModel = ServiceContainer.GetService<DashboardViewModel>();
        DataContext = _viewModel;
        _viewModel.DataLoadCompleted += OnViewModelDataLoadCompleted;
        _store = ServiceContainer.GetService<IDocumentStore>();
        _notesStore = ServiceContainer.GetService<INotesStore>();
        _assignmentStore = ServiceContainer.GetService<IDocumentAssignmentStore>();
        _userStore = ServiceContainer.GetService<IUserStore>();
        _noteStatusSync = ServiceContainer.GetService<INoteDocumentStatusSync>();
        _permissionService = ServiceContainer.GetService<IPermissionService>();
        _auditTrailService = ServiceContainer.GetService<IAuditTrailService>();
        _intelligenceService = ServiceContainer.GetService<IIntelligenceService>();

        // Initialize combo boxes
        TimeRangeCombo.Items.Clear();
        foreach (var label in DashboardViewModel.TimeRangeOptions)
            TimeRangeCombo.Items.Add(label);
        TimeRangeCombo.SelectedItem = _viewModel.SelectedTimeRange;

        BranchFilterCombo.Items.Add(AllFilterLabel);
        BranchFilterCombo.SelectedIndex = 0;
        SectionFilterCombo.Items.Add(AllFilterLabel);
        SectionFilterCombo.SelectedIndex = 0;

        IssueSeverityFilter.Items.Add("All Severities");
        IssueSeverityFilter.Items.Add("Critical");
        IssueSeverityFilter.Items.Add("High");
        IssueSeverityFilter.Items.Add("Medium");
        IssueSeverityFilter.Items.Add("Low");
        IssueSeverityFilter.SelectedIndex = 0;

        FollowUpFilter.Items.Add("All");
        FollowUpFilter.Items.Add("Due Today");
        FollowUpFilter.Items.Add("Overdue");
        FollowUpFilter.Items.Add("This Week");
        FollowUpFilter.SelectedIndex = 0;

        DrillDownModeCombo.Items.Add("By Branch");
        DrillDownModeCombo.Items.Add("By Section");
        DrillDownModeCombo.SelectedIndex = 0;

        // Set up auto-refresh timer (every 2 minutes)
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(2)
        };
        _refreshTimer.Tick += (s, e) => LoadDashboardData();

        Loaded += DashboardView_Loaded;
        Unloaded += DashboardView_Unloaded;
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DashboardViewModel.SelectedTimeRange) || !_timeRangeReloadEnabled || !IsLoaded)
            return;
        LoadDashboardData();
    }

    private void DashboardView_Loaded(object sender, RoutedEventArgs e)
    {
        // Check permissions
        var config = ServiceContainer.GetService<AppConfiguration>();
        if (config?.CurrentUserRole == null || 
            !Roles.HasMinimumRole(config.CurrentUserRole, Roles.Manager))
        {
            MessageBox.Show("You do not have permission to access the Dashboard.\n\nThis feature requires Manager or Administrator role.",
                "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        _timeRangeReloadEnabled = false;
        LoadDashboardData();
        _refreshTimer.Start();
    }

    private void DashboardView_Unloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
        _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
    }

    private async void LoadDashboardData()
    {
        try
        {
            _dashboardData = new DashboardData();

            var selectedTimeRange = _viewModel.SelectedTimeRange ?? "This Month";
            await _viewModel.LoadDataAsync(selectedTimeRange);
            // UI refresh runs from ViewModel.DataLoadCompleted -> OnViewModelDataLoadCompleted (single path, no duplicate Apply).
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading dashboard data: {ex.Message}",
                "Dashboard Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Updates KPIs, activity feed, etc. from ViewModel.FilteredDocuments. Called after LoadDataAsync or from DataLoadCompleted.
    /// </summary>
    private void OnViewModelDataLoadCompleted()
    {
        Dispatcher.BeginInvoke(() =>
        {
            ApplyDashboardDataToUi();
            _timeRangeReloadEnabled = true;
        });
    }

    private void ApplyDashboardDataToUi()
    {
        var filteredDocs = _viewModel.FilteredDocuments;
        PopulateScopeCombos(filteredDocs);
        var scopedDocs = ApplyBranchSectionFilter(filteredDocs);
        var scopedIds = scopedDocs.Select(d => d.Id).ToHashSet();
        var issueNotesByDocId = BuildIssueNotesByDocumentId(scopedIds);
        CalculateKPIs(scopedDocs, issueNotesByDocId);
        LoadCriticalIssues(scopedDocs, issueNotesByDocId);
        LoadActivityFeed(scopedDocs);
        LoadTeamWorkload();
        LoadDocumentPipeline(scopedDocs);
        LoadFollowUps(scopedDocs);
        LoadAlerts(scopedDocs, issueNotesByDocId);
        LoadAiInsights(scopedDocs);
        LoadDrillDownAnalysis(scopedDocs);
        LoadAuditTrail();
        TeamTasksSection.Refresh();
    }

    private void PopulateScopeCombos(IReadOnlyList<Document> documentsFromTimeRange)
    {
        _suppressScopeFilterChanged = true;
        try
        {
            var prevBranch = BranchFilterCombo.SelectedItem?.ToString() ?? AllFilterLabel;
            var prevSection = SectionFilterCombo.SelectedItem?.ToString() ?? AllFilterLabel;

            BranchFilterCombo.Items.Clear();
            BranchFilterCombo.Items.Add(AllFilterLabel);
            foreach (var b in Branches.All)
                BranchFilterCombo.Items.Add(b);
            foreach (var b in documentsFromTimeRange
                         .Select(d => d.Branch)
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Select(x => x!.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (!BranchFilterCombo.Items.Cast<object>().Any(x => string.Equals(x.ToString(), b, StringComparison.OrdinalIgnoreCase)))
                    BranchFilterCombo.Items.Add(b);
            }

            SectionFilterCombo.Items.Clear();
            SectionFilterCombo.Items.Add(AllFilterLabel);
            foreach (var s in Enums.SectionValues)
                SectionFilterCombo.Items.Add(s);
            foreach (var s in documentsFromTimeRange
                         .Select(d => d.Section)
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Select(x => x!.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (!SectionFilterCombo.Items.Cast<object>().Any(x => string.Equals(x.ToString(), s, StringComparison.OrdinalIgnoreCase)))
                    SectionFilterCombo.Items.Add(s);
            }

            SelectComboIfPresent(BranchFilterCombo, prevBranch);
            SelectComboIfPresent(SectionFilterCombo, prevSection);
        }
        finally
        {
            _suppressScopeFilterChanged = false;
        }
    }

    private static void SelectComboIfPresent(System.Windows.Controls.ComboBox combo, string? desired)
    {
        if (string.IsNullOrEmpty(desired))
        {
            combo.SelectedIndex = 0;
            return;
        }
        foreach (var item in combo.Items)
        {
            if (string.Equals(item.ToString(), desired, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private List<Document> ApplyBranchSectionFilter(IReadOnlyList<Document> docs)
    {
        var branchItem = BranchFilterCombo.SelectedItem?.ToString();
        var sectionItem = SectionFilterCombo.SelectedItem?.ToString();
        var allBranch = string.IsNullOrEmpty(branchItem) || branchItem == AllFilterLabel;
        var allSection = string.IsNullOrEmpty(sectionItem) || sectionItem == AllFilterLabel;
        IEnumerable<Document> q = docs;
        if (!allBranch)
        {
            q = q.Where(d => !string.IsNullOrWhiteSpace(d.Branch) &&
                             string.Equals(d.Branch!.Trim(), branchItem!.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        if (!allSection)
        {
            q = q.Where(d => !string.IsNullOrWhiteSpace(d.Section) &&
                             string.Equals(d.Section!.Trim(), sectionItem!.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        return q.ToList();
    }

    /// <summary>Current time-range documents with branch/section KPI filters (no combo repopulate).</summary>
    private List<Document> GetScopedDocuments() =>
        ApplyBranchSectionFilter(_viewModel.FilteredDocuments);

    private void BranchSectionFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressScopeFilterChanged || !IsLoaded)
            return;
        ApplyDashboardDataToUi();
    }

    /// <summary>All Issue-type notes per scoped document (any status). Uses a batched query so notes are not dropped by a global Search limit.</summary>
    private Dictionary<int, List<Note>> BuildIssueNotesByDocumentId(IReadOnlyCollection<int> scopedDocumentIds)
    {
        var ids = scopedDocumentIds as HashSet<int> ?? scopedDocumentIds.ToHashSet();
        if (ids.Count == 0) return new Dictionary<int, List<Note>>();
        return _notesStore.GetIssueNotesByDocumentIds(ids.ToList());
    }

    private static bool HasUnresolvedIssueNotes(IReadOnlyList<Note> issueNotesForDoc) =>
        issueNotesForDoc.Any(n =>
            n.Status == NoteStatus.Open || n.Status == NoteStatus.InProgress);

    /// <summary>
    /// True when the dashboard should treat the document as having an active issue:
    /// any unresolved (Open/InProgress) Issue-type note, or Issue status with no Issue-type notes (doc-level flag only).
    /// Resolved Issue-type notes with status still "Issue" do not count — clear document status separately if needed.
    /// </summary>
    private static bool DocumentHasActiveIssue(Document d, IReadOnlyList<Note> issueTypeNotesForDoc)
    {
        if (HasUnresolvedIssueNotes(issueTypeNotesForDoc))
            return true;
        return d.Status == Enums.Status.Issue && issueTypeNotesForDoc.Count == 0;
    }

    private void CalculateKPIs(List<Document> docs, Dictionary<int, List<Note>> issueNotesByDocId)
    {
        var scopedIds = docs.Select(d => d.Id).ToHashSet();
        var assignmentsInScope = _assignmentStore.ListAll(limit: DashboardAssignmentListLimit)
            .Where(a => scopedIds.Contains(a.DocumentId))
            .ToList();

        // Distinct documents with unresolved issue notes or Issue status with no issue-type trail (no double-counting).
        var activeIssueDocIds = new HashSet<int>();
        foreach (var d in docs)
        {
            issueNotesByDocId.TryGetValue(d.Id, out var list);
            list ??= new List<Note>();
            if (DocumentHasActiveIssue(d, list))
                activeIssueDocIds.Add(d.Id);
        }

        KpiActiveIssues.Text = activeIssueDocIds.Count.ToString(CultureInfo.InvariantCulture);
        KpiActiveIssuesChange.Text = "";

        // Pending Tasks
        var pendingTasks = assignmentsInScope.Count(a => a.Status == AssignmentStatus.Pending);
        KpiPendingTasks.Text = pendingTasks.ToString(CultureInfo.InvariantCulture);
        KpiPendingTasksChange.Text = "—";

        // Overdue Items
        var now = DateTime.UtcNow;
        var overdueCount = assignmentsInScope
            .Count(a => a.Status != AssignmentStatus.Completed &&
                        a.Status != AssignmentStatus.Cancelled &&
                        !string.IsNullOrEmpty(a.DueDate) &&
                        DateTime.TryParse(a.DueDate, out var due) && due < now);
        KpiOverdueItems.Text = overdueCount.ToString(CultureInfo.InvariantCulture);
        KpiOverdueItemsChange.Text = overdueCount > 0 ? "⚠️ Requires attention" : "Good";

        // Completion Rate
        var totalAssignments = assignmentsInScope.Count;
        var completedAssignments = assignmentsInScope.Count(a => a.Status == AssignmentStatus.Completed);
        var completionRate = totalAssignments > 0 ? (completedAssignments * 100.0 / totalAssignments) : 0;
        KpiCompletionRate.Text = $"{completionRate:F1}%";
        KpiCompletionRateChange.Text = "—";

        // Average Response Time
        var completedWithTimes = assignmentsInScope
            .Where(a => a.Status == AssignmentStatus.Completed &&
                        !string.IsNullOrEmpty(a.AssignedAt) && !string.IsNullOrEmpty(a.CompletedAt))
            .ToList();
        
        if (completedWithTimes.Any())
        {
            var avgHours = completedWithTimes
                .Select(a => {
                    if (DateTime.TryParse(a.AssignedAt, out var assigned) && 
                        DateTime.TryParse(a.CompletedAt, out var completed))
                    {
                        return (completed - assigned).TotalHours;
                    }
                    return 0.0;
                })
                .Where(h => h > 0)
                .DefaultIfEmpty(0)
                .Average();
            
            KpiAvgResponseTime.Text = avgHours < 24 
                ? $"{avgHours:F1}h" 
                : $"{(avgHours / 24):F1}d";
        }
        else
        {
            KpiAvgResponseTime.Text = "—";
        }
        KpiAvgResponseTimeChange.Text = "—";

        // Daily Throughput (documents cleared today)
        var today = DateTime.Now.Date;
        var clearedToday = docs.Count(d => d.Status == Enums.Status.Cleared && 
                                           d.ReviewedAt != null &&
                                           DateTime.TryParse(d.ReviewedAt, out var reviewDate) &&
                                           reviewDate.Date == today);
        KpiDailyThroughput.Text = clearedToday.ToString(CultureInfo.InvariantCulture);
        KpiDailyThroughputChange.Text = $"{docs.Count} total in range";

        // Total Documents
        KpiTotalDocuments.Text = docs.Count.ToString(CultureInfo.InvariantCulture);
        KpiTotalDocumentsChange.Text = $"{_viewModel.GetTotalDocumentCount()} all time";

        // Follow-ups Due
        var followUpsDue = docs.Count(d => d.IsFollowUpDue);
        KpiFollowUpsDue.Text = followUpsDue.ToString(CultureInfo.InvariantCulture);
        KpiFollowUpsDueChange.Text = followUpsDue > 0 ? "Needs attention" : "All clear";
    }

    private void LoadCriticalIssues(List<Document> docs, Dictionary<int, List<Note>> issueNotesByDocId)
    {
        var issues = new List<IssueItem>();
        var severityFilter = IssueSeverityFilter.SelectedItem?.ToString();

        void AddIssueItemFromNote(Document doc, Note note)
        {
            if (severityFilter != "All Severities" && note.Severity != severityFilter)
                return;

            var assignment = _assignmentStore.ListByDocument(doc.Id)
                .FirstOrDefault(a => a.Status != AssignmentStatus.Completed);

            issues.Add(new IssueItem
            {
                Title = $"{doc.DocumentType ?? "Document"} - {Path.GetFileName(doc.FilePath)}",
                Details = note.Content.Length > 100 ? note.Content.Substring(0, 100) + "..." : note.Content,
                Severity = note.Severity,
                SeverityColor = note.SeverityColor,
                AssigneeInfo = assignment != null
                    ? $"Assigned to: {assignment.AssignedToUsername}"
                    : "Unassigned",
                DocumentId = doc.Id,
                NoteId = note.Id
            });
        }

        // Documents in Issue status: list unresolved Issue-type notes, or synthetic row only when there are no Issue-type notes at all.
        foreach (var doc in docs.Where(d => d.Status == Enums.Status.Issue))
        {
            issueNotesByDocId.TryGetValue(doc.Id, out var allIssueNotes);
            allIssueNotes ??= new List<Note>();
            var activeNotes = allIssueNotes
                .Where(n => n.Type == NoteType.Issue &&
                            (n.Status == NoteStatus.Open || n.Status == NoteStatus.InProgress))
                .ToList();

            if (activeNotes.Any())
            {
                foreach (var note in activeNotes)
                    AddIssueItemFromNote(doc, note);
            }
            else if (!allIssueNotes.Any(n => n.Type == NoteType.Issue))
            {
                issues.Add(new IssueItem
                {
                    Title = $"{doc.DocumentType ?? "Document"} - {Path.GetFileName(doc.FilePath)}",
                    Details = "Issue flagged but no detailed notes",
                    Severity = NoteSeverity.Medium,
                    SeverityColor = "#FFC107",
                    AssigneeInfo = "No assignment",
                    DocumentId = doc.Id
                });
            }
        }

        // Other statuses can still have open Critical/High issue notes — surface them here too (matches KPI).
        foreach (var doc in docs.Where(d => d.Status != Enums.Status.Issue))
        {
            if (!issueNotesByDocId.TryGetValue(doc.Id, out var allIssueNotes))
                continue;
            foreach (var note in allIssueNotes.Where(n =>
                         n.Type == NoteType.Issue &&
                         (n.Status == NoteStatus.Open || n.Status == NoteStatus.InProgress) &&
                         (n.Severity == NoteSeverity.Critical || n.Severity == NoteSeverity.High)))
                AddIssueItemFromNote(doc, note);
        }

        // Sort by severity
        var severityOrder = new Dictionary<string, int>
        {
            [NoteSeverity.Critical] = 0,
            [NoteSeverity.High] = 1,
            [NoteSeverity.Medium] = 2,
            [NoteSeverity.Low] = 3,
            [NoteSeverity.Info] = 4
        };

        issues = issues.OrderBy(i => severityOrder.GetValueOrDefault(i.Severity, 5)).ToList();
        CriticalIssuesList.ItemsSource = issues;
    }

    private void LoadActivityFeed(IReadOnlyList<Document> scopedDocuments)
    {
        var activities = new List<ActivityItem>();
        var scopedIds = scopedDocuments.Select(d => d.Id).ToHashSet();
        var recentCutoff = DateTime.Now.AddHours(-24);

        // Get recent document status changes (from notes)
        var recentNotes = _notesStore.List(limit: 1000)
            .Where(n => scopedIds.Contains(n.DocumentId))
            .Where(n => DateTime.TryParse(n.CreatedAt, out var created) && created > recentCutoff)
            .OrderByDescending(n => n.CreatedAt)
            .Take(20)
            .ToList();

        foreach (var note in recentNotes)
        {
            var getResult = _store.GetResult(note.DocumentId);
            if (!getResult.IsSuccess)
            {
                _log.Warning("Could not load document {Id} for activity: {Error}", note.DocumentId, getResult.Error);
                continue;
            }
            var doc = getResult.Value!;

            var icon = note.Type switch
            {
                NoteType.Issue => "🔴",
                NoteType.Evidence => "✅",
                NoteType.Recommendation => "💡",
                _ => "📝"
            };

            activities.Add(new ActivityItem
            {
                Icon = icon,
                Message = $"{note.CreatedBy} added {note.Type.ToLower()} to {Path.GetFileName(doc.FilePath)}",
                TimeAgo = GetTimeAgo(note.CreatedAt),
                Timestamp = note.CreatedAt
            });
        }

        // Get recent assignments
        var recentAssignments = _assignmentStore.ListAll(limit: DashboardAssignmentListLimit)
            .Where(a => scopedIds.Contains(a.DocumentId))
            .Where(a => DateTime.TryParse(a.AssignedAt, out var assigned) && assigned > recentCutoff)
            .OrderByDescending(a => a.AssignedAt)
            .Take(10);

        foreach (var assignment in recentAssignments)
        {
            var getResult = _store.GetResult(assignment.DocumentId);
            if (!getResult.IsSuccess)
            {
                _log.Warning("Could not load document {Id} for activity: {Error}", assignment.DocumentId, getResult.Error);
                continue;
            }
            var doc = getResult.Value!;

            activities.Add(new ActivityItem
            {
                Icon = "👤",
                Message = $"{assignment.AssignedByUsername} assigned document to {assignment.AssignedToUsername}",
                TimeAgo = GetTimeAgo(assignment.AssignedAt),
                Timestamp = assignment.AssignedAt
            });
        }

        // Sort all activities by timestamp
        activities = activities
            .OrderByDescending(a => DateTime.TryParse(a.Timestamp, out var dt) ? dt : DateTime.MinValue)
            .Take(30)
            .ToList();

        ActivityFeed.ItemsSource = activities;
    }

    private void LoadTeamWorkload()
    {
        var users = _userStore.ListUsers(isActive: true);
        var workloadData = new List<WorkloadItem>();
        var now = DateTime.UtcNow;

        foreach (var user in users)
        {
            var assignments = _assignmentStore.ListByUser(user.Id);
            
            var pending = assignments.Count(a => a.Status == AssignmentStatus.Pending);
            var inProgress = assignments.Count(a => a.Status == AssignmentStatus.InProgress);
            var overdue = assignments.Count(a => 
                a.Status != AssignmentStatus.Completed && 
                a.Status != AssignmentStatus.Cancelled &&
                !string.IsNullOrEmpty(a.DueDate) &&
                DateTime.TryParse(a.DueDate, out var due) && due < now);

            workloadData.Add(new WorkloadItem
            {
                Username = user.DisplayName,
                PendingCount = pending,
                InProgressCount = inProgress,
                OverdueCount = overdue
            });
        }

        TeamWorkloadGrid.ItemsSource = workloadData.OrderByDescending(w => w.OverdueCount);
    }

    private void LoadDocumentPipeline(List<Document> docs)
    {
        PipelineDraft.Text = docs.Count(d => d.Status == Enums.Status.Draft).ToString(CultureInfo.InvariantCulture);
        PipelineReviewed.Text = docs.Count(d => d.Status == Enums.Status.Reviewed).ToString(CultureInfo.InvariantCulture);
        PipelineReady.Text = docs.Count(d => d.Status == Enums.Status.ReadyForAudit).ToString(CultureInfo.InvariantCulture);
        PipelineIssue.Text = docs.Count(d => d.Status == Enums.Status.Issue).ToString(CultureInfo.InvariantCulture);
        PipelineCleared.Text = docs.Count(d => d.Status == Enums.Status.Cleared).ToString(CultureInfo.InvariantCulture);
    }

    private void LoadFollowUps(List<Document> docs)
    {
        var followUps = new List<FollowUpItem>();
        var filter = FollowUpFilter.SelectedItem?.ToString() ?? "All";
        var now = DateTime.UtcNow;
        var today = now.Date;
        var endOfWeek = today.AddDays(7 - (int)today.DayOfWeek);

        var docsWithFollowUp = docs.Where(d => d.GetTags().Contains("follow-up", StringComparer.OrdinalIgnoreCase)).ToList();

        foreach (var doc in docsWithFollowUp)
        {
            var dueDate = doc.GetFollowUpDue();
            if (dueDate == null) continue;

            // Apply filter
            var include = filter switch
            {
                "Due Today" => dueDate.Value.Date == today,
                "Overdue" => dueDate.Value < now,
                "This Week" => dueDate.Value.Date <= endOfWeek,
                _ => true
            };

            if (!include) continue;

            var assignment = _assignmentStore.ListByDocument(doc.Id).FirstOrDefault();
            var daysUntilDue = (dueDate.Value.Date - today).Days;
            
            var dueInfo = daysUntilDue < 0 
                ? $"Overdue by {Math.Abs(daysUntilDue)} day(s)" 
                : daysUntilDue == 0 
                    ? "Due today" 
                    : $"Due in {daysUntilDue} day(s)";

            var dueColor = daysUntilDue < 0 ? "#DC3545" : daysUntilDue == 0 ? "#FFC107" : "#28A745";

            followUps.Add(new FollowUpItem
            {
                DocumentId = doc.Id,
                DocumentName = Path.GetFileName(doc.FilePath),
                DueDate = dueDate.Value,
                DueInfo = dueInfo,
                DueColor = dueColor,
                AssignedTo = assignment != null ? $"Assigned to: {assignment.AssignedToUsername}" : "Unassigned"
            });
        }

        FollowUpsList.ItemsSource = followUps.OrderBy(f => f.DueDate);
    }

    private void LoadAlerts(List<Document> docs, Dictionary<int, List<Note>> issueNotesByDocId)
    {
        var alerts = new List<AlertItem>();
        var scopedIds = docs.Select(d => d.Id).ToHashSet();
        var assignmentsInScope = _assignmentStore.ListAll(limit: DashboardAssignmentListLimit)
            .Where(a => scopedIds.Contains(a.DocumentId))
            .ToList();

        // Check for overdue assignments
        var overdueAssignments = assignmentsInScope
            .Count(a => a.Status != AssignmentStatus.Completed &&
                        a.Status != AssignmentStatus.Cancelled &&
                        !string.IsNullOrEmpty(a.DueDate) &&
                        DateTime.TryParse(a.DueDate, out var due) && due < DateTime.UtcNow);

        if (overdueAssignments > 0)
        {
            alerts.Add(new AlertItem
            {
                Icon = "⚠️",
                Message = $"{overdueAssignments} assignment(s) are overdue and need immediate attention",
                Type = "warning",
                Kind = DashboardAlertKind.OverdueAssignments
            });
        }

        // Open or InProgress Critical issue notes (Resolved no longer triggers the alert).
        var criticalIssues = issueNotesByDocId.Values
            .SelectMany(n => n)
            .Count(n => scopedIds.Contains(n.DocumentId) &&
                        n.Type == NoteType.Issue &&
                        (n.Status == NoteStatus.Open || n.Status == NoteStatus.InProgress) &&
                        n.Severity == NoteSeverity.Critical);

        if (criticalIssues > 0)
        {
            alerts.Add(new AlertItem
            {
                Icon = "🔴",
                Message = $"{criticalIssues} critical issue(s) require immediate resolution",
                Type = "critical",
                Kind = DashboardAlertKind.CriticalIssues
            });
        }

        // Active issue documents (same rules as KPI) that have no active assignment
        var unassignedIssues = docs.Count(d =>
        {
            issueNotesByDocId.TryGetValue(d.Id, out var list);
            list ??= new List<Note>();
            if (!DocumentHasActiveIssue(d, list))
                return false;
            return !_assignmentStore.ListByDocument(d.Id).Any(a => a.Status != AssignmentStatus.Completed);
        });

        if (unassignedIssues > 0)
        {
            alerts.Add(new AlertItem
            {
                Icon = "📋",
                Message = $"{unassignedIssues} document(s) with issues are not assigned to anyone",
                Type = "warning",
                Kind = DashboardAlertKind.UnassignedIssues
            });
        }

        AlertsList.ItemsSource = alerts;
        AlertsPanel.Visibility = alerts.Any() ? Visibility.Visible : Visibility.Collapsed;
    }

    // ============================================
    // PHASE 3: Advanced Features
    // ============================================

    private void LoadAiInsights(List<Document> docs)
    {
        try
        {
        var scopedIds = docs.Select(d => d.Id).ToHashSet();
        // Get open issues as findings
        var findings = _notesStore.List()
            .Where(n => n.Type == NoteType.Issue && n.Status == NoteStatus.Open && scopedIds.Contains(n.DocumentId))
            .Cast<object>()
            .ToList();

            var config = ServiceContainer.GetService<IConfigStore>();
            var language = config != null && ReportLocalizationService.IsArabic(config) ? "ar" : "en";

            // Generate AI summary
            var summary = _intelligenceService.GenerateExecutiveSummary(docs, findings, language);
            AiSummaryText.Text = summary;

            // Generate recommendations
            var recommendations = _intelligenceService.GenerateRecommendations(findings, language);
            AiRecommendationsList.ItemsSource = recommendations;
        }
        catch (Exception ex)
        {
            AiSummaryText.Text = $"Error loading AI insights: {ex.Message}";
            AiRecommendationsList.ItemsSource = new List<string>();
        }
    }

    private void LoadDrillDownAnalysis(List<Document> docs)
    {
        try
        {
            var mode = DrillDownModeCombo.SelectedItem?.ToString() ?? "By Branch";
            var data = new List<DashboardDrillDownItem>();

            if (mode == "By Branch")
            {
                var branches = docs
                    .Where(d => !string.IsNullOrEmpty(d.Branch))
                    .GroupBy(d => d.Branch)
                    .Select(g => new DashboardDrillDownItem
                    {
                        Name = g.Key ?? "Unknown",
                        TotalCount = g.Count(),
                        IssueCount = g.Count(d => d.Status == Enums.Status.Issue),
                        ClearedCount = g.Count(d => d.Status == Enums.Status.Cleared),
                        Mode = "Branch"
                    })
                    .OrderByDescending(d => d.TotalCount)
                    .ToList();

                data = branches;
            }
            else // By Section
            {
                var sections = docs
                    .Where(d => !string.IsNullOrEmpty(d.Section))
                    .GroupBy(d => d.Section)
                    .Select(g => new DashboardDrillDownItem
                    {
                        Name = g.Key ?? "Unknown",
                        TotalCount = g.Count(),
                        IssueCount = g.Count(d => d.Status == Enums.Status.Issue),
                        ClearedCount = g.Count(d => d.Status == Enums.Status.Cleared),
                        Mode = "Section"
                    })
                    .OrderByDescending(d => d.TotalCount)
                    .ToList();

                data = sections;
            }

            DrillDownGrid.ItemsSource = data;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading drill-down analysis: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LoadAuditTrail()
    {
        try
        {
            var logs = await _auditTrailService.GetLogsAsync(
                from: DateTime.UtcNow.AddDays(-7), // Last 7 days
                limit: 50
            );

            // Add formatted timestamp for display
            var formattedLogs = logs.Select(log => new AuditLogDisplayItem
            {
                FormattedTimestamp = FormatAuditTimestamp(log.Timestamp),
                Username = log.Username,
                Action = log.Action,
                Category = log.Category,
                Details = log.Details ?? ""
            }).ToList();

            AuditTrailGrid.ItemsSource = formattedLogs;
        }
        catch
        {
            // Audit trail service might not be available in all configurations
            AuditTrailGrid.ItemsSource = new List<AuditLogDisplayItem>();
        }
    }

    private string FormatAuditTimestamp(string timestamp)
    {
        return Core.Helpers.AuditTimeHelper.FormatForDisplay(timestamp);
    }

    // ============================================
    // End Phase 3 Methods
    // ============================================

    private string GetTimeAgo(string timestamp)
    {
        if (!DateTime.TryParse(timestamp, out var dt)) return "Unknown";
        
        var diff = DateTime.UtcNow - dt.ToUniversalTime();
        
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        
        return dt.ToLocalTime().ToString("MMM dd", CultureInfo.CurrentCulture);
    }

    // Event Handlers
    private void IssueSeverityFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var scoped = GetScopedDocuments();
        LoadCriticalIssues(scoped, BuildIssueNotesByDocumentId(scoped.Select(d => d.Id).ToHashSet()));
    }

    private void FollowUpFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            LoadFollowUps(GetScopedDocuments());
    }

    private void CriticalIssuesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = CriticalIssuesList.SelectedItem != null;
        ResolveIssueBtn.IsEnabled = hasSelection;
        ReassignIssueBtn.IsEnabled = hasSelection;
        ViewIssueDetailsBtn.IsEnabled = hasSelection;
    }

    private void CriticalIssuesList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CriticalIssuesList.SelectedItem is IssueItem issue)
        {
            NavigateToDocument(issue.DocumentId);
        }
    }

    private void FollowUpsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = FollowUpsList.SelectedItem != null;
        SendReminderBtn.IsEnabled = hasSelection;
        ViewFollowUpBtn.IsEnabled = hasSelection;
    }

    private void FollowUpsList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FollowUpsList.SelectedItem is FollowUpItem followUp)
            NavigateToDocument(followUp.DocumentId);
    }

    private void ResolveIssue_Click(object sender, RoutedEventArgs e)
    {
        if (CriticalIssuesList.SelectedItem is IssueItem issue && issue.NoteId > 0)
        {
            var note = _notesStore.GetById(issue.NoteId);
            if (note != null)
            {
                var previousStatus = note.Status;
                var config = ServiceContainer.GetService<AppConfiguration>();
                note.Status = NoteStatus.Resolved;
                note.ResolvedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                note.ResolvedBy = config?.CurrentUserName ?? "Unknown";
                if (!_notesStore.Update(note))
                {
                    MessageBox.Show("Unable to update issue status.", "Update Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _ = _noteStatusSync.OnNoteStatusChangedAsync(note, previousStatus);
                
                MessageBox.Show("Issue marked as resolved.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadDashboardData();
            }
        }
    }

    private void ReassignIssue_Click(object sender, RoutedEventArgs e)
    {
        if (CriticalIssuesList.SelectedItem is IssueItem issue)
        {
            var getResult = _store.GetResult(issue.DocumentId);
            if (!getResult.IsSuccess)
            {
                _log.Warning("Could not load document {Id}: {Error}", issue.DocumentId, getResult.Error);
                MessageBox.Show($"Failed to load document: {getResult.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var doc = getResult.Value!;
            var config = ServiceContainer.GetService<AppConfiguration>();
            var currentUser = _userStore.GetByUsername(config?.CurrentUserName ?? "");
            if (currentUser != null)
            {
                var dialog = new Dialogs.AssignDocumentDialog(new List<Document> { doc }, currentUser);
                dialog.ShowDialog();
                LoadDashboardData();
            }
        }
    }

    private void ViewIssueDetails_Click(object sender, RoutedEventArgs e)
    {
        if (CriticalIssuesList.SelectedItem is IssueItem issue)
        {
            NavigateToDocument(issue.DocumentId);
        }
    }

    private void SendReminder_Click(object sender, RoutedEventArgs e)
    {
        if (FollowUpsList.SelectedItem is FollowUpItem followUp)
        {
            var assignment = _assignmentStore.ListByDocument(followUp.DocumentId).FirstOrDefault();
            if (assignment != null)
            {
                MessageBox.Show($"Assignment reminder functionality requires email configuration in Control Panel.",
                    "Reminder", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("No assignment found for this document.", 
                    "Cannot Send Reminder", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void ViewFollowUp_Click(object sender, RoutedEventArgs e)
    {
        if (FollowUpsList.SelectedItem is FollowUpItem followUp)
        {
            NavigateToDocument(followUp.DocumentId);
        }
    }

    private void ClearActivityFeed_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Are you sure you want to clear the activity feed display?\n\nThis will only clear the display, not the actual data.",
            "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            ActivityFeed.ItemsSource = new List<ActivityItem>();
        }
    }

    private void DismissAlerts_Click(object sender, RoutedEventArgs e)
    {
        AlertsPanel.Visibility = Visibility.Collapsed;
    }

    private void ViewAlert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not AlertItem alert)
            return;

        var scopedDocs = GetScopedDocuments();
        var scopedIds = scopedDocs.Select(d => d.Id).ToHashSet();
        var utc = DateTime.UtcNow;

        switch (alert.Kind)
        {
            case DashboardAlertKind.OverdueAssignments:
            {
                var overdue = _assignmentStore.ListAll(limit: DashboardAssignmentListLimit)
                    .Where(a => scopedIds.Contains(a.DocumentId))
                    .FirstOrDefault(a =>
                        a.Status != AssignmentStatus.Completed &&
                        a.Status != AssignmentStatus.Cancelled &&
                        !string.IsNullOrEmpty(a.DueDate) &&
                        DateTime.TryParse(a.DueDate, out var due) && due < utc);
                if (overdue != null)
                    NavigateToDocument(overdue.DocumentId);
                else
                    MessageBox.Show("No overdue assignments found for the current dashboard filters.",
                        "Overdue assignments", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            }
            case DashboardAlertKind.CriticalIssues:
            {
                var map = BuildIssueNotesByDocumentId(scopedIds);
                var note = map.Values
                    .SelectMany(n => n)
                    .FirstOrDefault(n =>
                        scopedIds.Contains(n.DocumentId) &&
                        n.Type == NoteType.Issue &&
                        (n.Status == NoteStatus.Open || n.Status == NoteStatus.InProgress) &&
                        n.Severity == NoteSeverity.Critical);
                if (note != null)
                    NavigateToDocument(note.DocumentId);
                else
                    MessageBox.Show("No critical issues found for the current dashboard filters.",
                        "Critical issues", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            }
            case DashboardAlertKind.UnassignedIssues:
            {
                var map = BuildIssueNotesByDocumentId(scopedIds);
                var doc = scopedDocs.FirstOrDefault(d =>
                {
                    map.TryGetValue(d.Id, out var list);
                    list ??= new List<Note>();
                    if (!DocumentHasActiveIssue(d, list))
                        return false;
                    return !_assignmentStore.ListByDocument(d.Id).Any(a => a.Status != AssignmentStatus.Completed);
                });
                if (doc != null)
                    NavigateToDocument(doc.Id);
                else
                    MessageBox.Show("No unassigned issue documents found for the current dashboard filters.",
                        "Unassigned issues", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            }
            default:
                MessageBox.Show(alert.Message, "Alert", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
        }
    }

    private void ManageAssignments_Click(object sender, RoutedEventArgs e)
    {
        var window = new AssignmentManagementWindow();
        window.ShowDialog();
        LoadDashboardData();
    }

    private void BulkAssign_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to workspace for bulk assignment
        NavigateToWorkspace();
    }

    private void SendTeamReminders_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Send reminders to all team members with pending or overdue assignments?",
            "Send Team Reminders", MessageBoxButton.YesNo, MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            MessageBox.Show("Team reminder functionality requires email configuration and SMTP setup in Control Panel.", 
                "Reminder", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void GenerateReport_Click(object sender, RoutedEventArgs e)
    {
        NavigateToReports();
    }

    private void ManageUsers_Click(object sender, RoutedEventArgs e)
    {
        var window = new UserManagementWindow();
        window.ShowDialog();
        LoadDashboardData();
    }

    private void ControlPanel_Click(object sender, RoutedEventArgs e)
    {
        var window = new ControlPanelWindow();
        window.ShowDialog();
    }

    private void AdvancedSearch_Click(object sender, RoutedEventArgs e)
    {
        NavigateToArchive();
    }

    // Navigation helpers
    private void NavigateToDocument(int documentId)
    {
        var getResult = _store.GetResult(documentId);
        if (!getResult.IsSuccess)
        {
            _log.Warning("Could not load document {Id}: {Error}", documentId, getResult.Error);
            MessageBox.Show($"Failed to load document: {getResult.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            NavigateToWorkspace();
            return;
        }
        var doc = getResult.Value!;
        DrillDownRequested.Raise(new DrillDownRequest
        {
            Branch = doc.Branch,
            Section = doc.Section,
            DocumentId = documentId
        });
    }

    private void NavigateToWorkspace()
    {
        // Navigate to Workspace view
        var mainWindow = Window.GetWindow(this) as MainWindow;
        mainWindow?.NavigateToWorkspace();
    }

    private void NavigateToReports()
    {
        // Navigate to Reports view
        var mainWindow = Window.GetWindow(this) as MainWindow;
        mainWindow?.NavigateToReports();
    }

    private void NavigateToArchive()
    {
        // Navigate to Archive view
        var mainWindow = Window.GetWindow(this) as MainWindow;
        mainWindow?.NavigateToArchive();
    }

    // ============================================
    // PHASE 3: Event Handlers
    // ============================================

    private void DrillDownMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            LoadDrillDownAnalysis(GetScopedDocuments());
    }

    private void DrillDownGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DrillDownBtn.IsEnabled = DrillDownGrid.SelectedItem != null;
    }

    private void DrillDownGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DrillDownGrid.SelectedItem is DashboardDrillDownItem item)
        {
            // Navigate to workspace with branch/section filter
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.NavigateToWorkspace();
            MessageBox.Show($"Navigate to Workspace and filter by {item.Mode}: {item.Name}",
                "Drill Down", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void DrillDownBtn_Click(object sender, RoutedEventArgs e)
    {
        DrillDownGrid_DoubleClick(sender, null!);
    }

    private void ViewFullAuditLog_Click(object sender, RoutedEventArgs e)
    {
        var window = new AuditLogWindow();
        window.ShowDialog();
    }

    private void ExportDashboardExcel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = $"Dashboard_Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                DefaultExt = ".xlsx"
            };

            if (saveDialog.ShowDialog() == true)
            {
                MessageBox.Show($"Use Reports > Executive Summary to export dashboard data to Excel.\n\n" +
                    $"For custom exports, use Search > Export Results.",
                    "Excel Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error exporting dashboard: {ex.Message}",
                "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportDashboardPDF_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"Dashboard_Export_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
                DefaultExt = ".pdf"
            };

            if (saveDialog.ShowDialog() == true)
            {
                MessageBox.Show($"Use Reports > Executive Summary to export dashboard data to PDF.\n\n" +
                    $"For custom reports, use Reports > Generate Report.",
                    "PDF Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error exporting dashboard: {ex.Message}",
                "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ViewPerformanceAnalytics_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to Reports view for detailed analytics
        NavigateToReports();
        MessageBox.Show("Performance Analytics view will show detailed charts and trend analysis.\n\nFor now, navigating to Reports view.",
            "Performance Analytics", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ViewTrendAnalysis_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to Reports view for trend analysis
        NavigateToReports();
        MessageBox.Show("Trend Analysis will show historical data patterns over time.\n\nFor now, navigating to Reports view.",
            "Trend Analysis", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowAdvancedFilters_Click(object sender, RoutedEventArgs e)
    {
        // Show advanced filter dialog
        MessageBox.Show("Advanced Filters:\n\n" +
            "• Filter by multiple branches/sections\n" +
            "• Filter by document types\n" +
            "• Filter by assignee\n" +
            "• Filter by date ranges\n" +
            "• Filter by status combinations\n" +
            "• Custom query builder\n\n" +
            "(Advanced filter dialog coming soon)",
            "Advanced Filters", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ============================================
    // End Phase 3 Event Handlers
    // ============================================
}

// Data Models
internal class DashboardData
{
    public int ActiveIssues { get; set; }
    public int PendingTasks { get; set; }
    public int OverdueItems { get; set; }
    public double CompletionRate { get; set; }
}

internal class IssueItem
{
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public string Severity { get; set; } = "";
    public string SeverityColor { get; set; } = "";
    public string AssigneeInfo { get; set; } = "";
    public int DocumentId { get; set; }
    public int NoteId { get; set; }
}

internal class ActivityItem
{
    public string Icon { get; set; } = "";
    public string Message { get; set; } = "";
    public string TimeAgo { get; set; } = "";
    public string Timestamp { get; set; } = "";
}

internal class WorkloadItem
{
    public string Username { get; set; } = "";
    public int PendingCount { get; set; }
    public int InProgressCount { get; set; }
    public int OverdueCount { get; set; }
}

internal class FollowUpItem
{
    public int DocumentId { get; set; }
    public string DocumentName { get; set; } = "";
    public DateTime DueDate { get; set; }
    public string DueInfo { get; set; } = "";
    public string DueColor { get; set; } = "";
    public string AssignedTo { get; set; } = "";
}

internal enum DashboardAlertKind
{
    OverdueAssignments,
    CriticalIssues,
    UnassignedIssues
}

internal class AlertItem
{
    public string Icon { get; set; } = "";
    public string Message { get; set; } = "";
    public string Type { get; set; } = "";
    public DashboardAlertKind Kind { get; set; }
}

// Phase 3 Data Models
internal class DashboardDrillDownItem
{
    public string Name { get; set; } = "";
    public int TotalCount { get; set; }
    public int IssueCount { get; set; }
    public int ClearedCount { get; set; }
    public string Mode { get; set; } = ""; // "Branch" or "Section"
}

internal class AuditLogDisplayItem
{
    public string FormattedTimestamp { get; set; } = "";
    public string Username { get; set; } = "";
    public string Action { get; set; } = "";
    public string Category { get; set; } = "";
    public string Details { get; set; } = "";
}
