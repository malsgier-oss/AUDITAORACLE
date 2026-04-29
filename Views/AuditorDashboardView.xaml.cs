using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Serilog;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Services;
using WorkAudit.Dialogs;
using WorkAudit.Domain;
using WorkAudit.Storage;
using Button = System.Windows.Controls.Button;

namespace WorkAudit.Views;

public partial class AuditorDashboardView : UserControl
{
    private const int DashboardAssignmentListLimit = 5000;
    private readonly ILogger _log = LoggingService.ForContext<AuditorDashboardView>();
    private readonly IDocumentStore _store;
    private readonly INotesStore _notesStore;
    private readonly IDocumentAssignmentStore _assignmentStore;
    private readonly IUserStore _userStore;
    private readonly INotificationStore _notificationStore;
    private readonly AppConfiguration _config;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _journalAutoSaveTimer;
    private int _currentUserId;
    private string _currentUsername = "";
    private bool _userBranchScopeAll;
    private string _userEffectiveBranch = Branches.Default;
    private bool _isUserContextReady;
    private bool _journalModified;

    public AuditorDashboardView()
    {
        InitializeComponent();

        _store = ServiceContainer.GetService<IDocumentStore>();
        _notesStore = ServiceContainer.GetService<INotesStore>();
        _assignmentStore = ServiceContainer.GetService<IDocumentAssignmentStore>();
        _userStore = ServiceContainer.GetService<IUserStore>();
        _notificationStore = ServiceContainer.GetService<INotificationStore>();
        _config = ServiceContainer.GetService<AppConfiguration>();

        // Initialize filter combos
        AssignmentFilterCombo.Items.Add("All");
        AssignmentFilterCombo.Items.Add("Pending");
        AssignmentFilterCombo.Items.Add("In Progress");
        AssignmentFilterCombo.Items.Add("Overdue");
        AssignmentFilterCombo.SelectedIndex = 0;

        IssuesFilterCombo.Items.Add("All");
        IssuesFilterCombo.Items.Add("Assigned to Me");
        IssuesFilterCombo.Items.Add("My Branch");
        IssuesFilterCombo.Items.Add("High Priority");
        IssuesFilterCombo.SelectedIndex = 0;

        // Set up auto-refresh timer (every 2 minutes)
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(2)
        };
        _refreshTimer.Tick += (s, e) => LoadDashboardData();

        // Set up journal auto-save timer (every 30 seconds)
        _journalAutoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _journalAutoSaveTimer.Tick += (s, e) => SaveJournalEntry();

        JournalTextBox.TextChanged += (s, e) => { _journalModified = true; JournalSaveStatus.Visibility = Visibility.Collapsed; };

        Loaded += AuditorDashboardView_Loaded;
        Unloaded += AuditorDashboardView_Unloaded;
    }

    private void AuditorDashboardView_Loaded(object sender, RoutedEventArgs e)
    {
        // Check permissions - only Auditor (2) and Reviewer (3) can access
        var roleLevel = Roles.GetRoleLevel(_config.CurrentUserRole ?? Roles.Viewer);
        if (roleLevel < 2 || roleLevel >= 4)
        {
            MessageBox.Show("This dashboard is for Auditors and Reviewers only.\n\nManagers and Administrators have their own dashboard.",
                "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Get current user info
        var currentUser = _userStore.GetByUsername(_config.CurrentUserName ?? "");
        if (currentUser != null)
        {
            _currentUserId = currentUser.Id;
            _currentUsername = currentUser.Username;
            _userBranchScopeAll = Branches.ScopesToAllBranches(currentUser.Branch);
            _userEffectiveBranch = Branches.ToConcreteBranchOrDefault(currentUser.Branch);
            _isUserContextReady = true;
        }
        else
        {
            _isUserContextReady = false;
            MessageBox.Show(
                "Unable to load your user profile. KPI data cannot be shown right now. Please sign in again.",
                "User Profile Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        LoadDashboardData();
        _refreshTimer.Start();
        _journalAutoSaveTimer.Start();
    }

    private void AuditorDashboardView_Unloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
        _journalAutoSaveTimer.Stop();
        if (_journalModified)
        {
            SaveJournalEntry();
        }
    }

    private void LoadDashboardData()
    {
        if (!_isUserContextReady)
            return;

        try
        {
            LoadPersonalKPIs();
            LoadMyAssignments();
            LoadBranchOverview();
            LoadCriticalIssues();
            LoadBranchTrends();
            LoadJournalEntry();
            LoadActivityFeed();
            LoadAlerts();
            TeamTasksSection.Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading dashboard data: {ex.Message}",
                "Dashboard Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadPersonalKPIs()
    {
        var myAssignments = _assignmentStore.ListAll(limit: DashboardAssignmentListLimit)
            .Where(a => a.AssignedToUserId == _currentUserId)
            .ToList();

        var pendingCount = myAssignments.Count(a => a.Status == AssignmentStatus.Pending);
        var inProgressCount = myAssignments.Count(a => a.Status == AssignmentStatus.InProgress);
        var completedCount = myAssignments.Count(a => a.Status == AssignmentStatus.Completed);
        var totalAssignments = myAssignments.Count;

        KpiMyPending.Text = pendingCount.ToString(CultureInfo.InvariantCulture);
        KpiMyInProgress.Text = inProgressCount.ToString(CultureInfo.InvariantCulture);

        // Completion rate (this month)
        var thisMonth = DateTime.Now.Month;
        var thisYear = DateTime.Now.Year;
        var thisMonthAssignments = myAssignments.Where(a =>
        {
            if (DateTime.TryParse(a.AssignedAt, out var assignedDate))
            {
                return assignedDate.Month == thisMonth && assignedDate.Year == thisYear;
            }
            return false;
        }).ToList();

        var thisMonthCompleted = thisMonthAssignments.Count(a => a.Status == AssignmentStatus.Completed);
        var completionRate = thisMonthAssignments.Count > 0
            ? (thisMonthCompleted * 100.0 / thisMonthAssignments.Count)
            : 0;
        KpiMyCompletionRate.Text = $"{completionRate:F1}%";

        // Average time
        var completedWithTimes = myAssignments
            .Where(a => a.Status == AssignmentStatus.Completed &&
                       !string.IsNullOrEmpty(a.AssignedAt) &&
                       !string.IsNullOrEmpty(a.CompletedAt))
            .ToList();

        if (completedWithTimes.Any())
        {
            var avgHours = completedWithTimes
                .Select(a =>
                {
                    if (DateTime.TryParse(a.AssignedAt, out var assigned) &&
                        DateTime.TryParse(a.CompletedAt, out var completed))
                    {
                        return (completed - assigned).TotalHours;
                    }
                    return 0;
                })
                .Where(h => h > 0)
                .Average();

            if (avgHours < 24)
                KpiMyAvgTime.Text = $"{avgHours:F1}h";
            else
                KpiMyAvgTime.Text = $"{(avgHours / 24):F1}d";
        }
        else
        {
            KpiMyAvgTime.Text = "—";
        }
    }

    private void LoadMyAssignments()
    {
        var myAssignments = _assignmentStore.ListAll(limit: DashboardAssignmentListLimit)
            .Where(a => a.AssignedToUserId == _currentUserId &&
                       a.Status != AssignmentStatus.Completed &&
                       a.Status != AssignmentStatus.Cancelled)
            .ToList();

        // Apply filter
        var filter = AssignmentFilterCombo.SelectedItem?.ToString() ?? "All";
        if (filter == "Pending")
            myAssignments = myAssignments.Where(a => a.Status == AssignmentStatus.Pending).ToList();
        else if (filter == "In Progress")
            myAssignments = myAssignments.Where(a => a.Status == AssignmentStatus.InProgress).ToList();
        else if (filter == "Overdue")
        {
            var now = DateTime.UtcNow;
            myAssignments = myAssignments.Where(a =>
                !string.IsNullOrEmpty(a.DueDate) &&
                DateTime.TryParse(a.DueDate, out var due) &&
                due < now).ToList();
        }

        var rows = new List<AssignmentRow>();
        foreach (var a in myAssignments)
        {
            var getResult = _store.GetResult(a.DocumentId);
            if (!getResult.IsSuccess)
                _log.Warning("Could not load document {Id} for assignment row: {Error}", a.DocumentId, getResult.Error);
            var documentRef = getResult.IsSuccess
                ? BuildDocumentReference(getResult.Value, a.DocumentId)
                : $"Doc #{a.DocumentId}";

            var priorityIcon = a.Priority switch
            {
                AssignmentPriority.Urgent => "🔴",
                AssignmentPriority.High => "🟠",
                AssignmentPriority.Low => "⚪",
                _ => "🔵"
            };

            var dueDateDisplay = "";
            if (!string.IsNullOrEmpty(a.DueDate) && DateTime.TryParse(a.DueDate, out var dueDate))
            {
                var timeSpan = dueDate - DateTime.UtcNow;
                if (timeSpan.TotalHours < 0)
                {
                    dueDateDisplay = "Overdue";
                }
                else if (timeSpan.TotalHours < 24)
                {
                    dueDateDisplay = "Due today";
                }
                else
                {
                dueDateDisplay = dueDate.ToString("MMM dd", CultureInfo.CurrentCulture);
                }
            }
            else
            {
                dueDateDisplay = "No due date";
            }

            rows.Add(new AssignmentRow
            {
                Assignment = a,
                DocumentRef = documentRef,
                PriorityIcon = priorityIcon,
                DueDateDisplay = dueDateDisplay,
                StatusBadge = a.Status
            });
        }

        // Sort by due date
        rows = rows.OrderBy(r =>
        {
            if (!string.IsNullOrEmpty(r.Assignment.DueDate) &&
                DateTime.TryParse(r.Assignment.DueDate, out var due))
            {
                return due;
            }
            return DateTime.MaxValue;
        }).ToList();

        AssignmentsGrid.ItemsSource = rows;
    }

    private bool UserBranchMatchesDocument(Document d)
    {
        if (_userBranchScopeAll)
            return true;
        var docBranch = string.IsNullOrWhiteSpace(d.Branch) ? Branches.Default : d.Branch.Trim();
        return string.Equals(docBranch, _userEffectiveBranch, StringComparison.OrdinalIgnoreCase);
    }

    private void LoadBranchOverview()
    {
        var branchDocs = _store.ListDocuments()
            .Where(UserBranchMatchesDocument)
            .ToList();

        // Stats for this month
        var thisMonth = DateTime.Now.Month;
        var thisYear = DateTime.Now.Year;
        var thisMonthDocs = branchDocs.Where(d =>
        {
            if (d.CaptureDateTime.HasValue)
            {
                return d.CaptureDateTime.Value.Month == thisMonth &&
                       d.CaptureDateTime.Value.Year == thisYear;
            }
            return false;
        }).ToList();

        BranchTotalDocs.Text = thisMonthDocs.Count.ToString(CultureInfo.InvariantCulture);
        BranchPendingDocs.Text = thisMonthDocs.Count(d => d.Status == Enums.Status.Reviewed).ToString(CultureInfo.InvariantCulture);
        BranchIssues.Text = thisMonthDocs.Count(d => d.Status == Enums.Status.Issue).ToString(CultureInfo.InvariantCulture);
        BranchCleared.Text = thisMonthDocs.Count(d => d.Status == Enums.Status.Cleared).ToString(CultureInfo.InvariantCulture);

        // Recent documents
        var recentDocs = branchDocs
            .OrderByDescending(d => d.UpdatedAt ?? d.CaptureTime)
            .Take(10)
            .Select(d =>
            {
                var statusIcon = d.Status switch
                {
                    Enums.Status.Draft => "📝",
                    Enums.Status.Reviewed => "👁️",
                    Enums.Status.ReadyForAudit => "✅",
                    Enums.Status.Issue => "⚠️",
                    Enums.Status.Cleared => "🎉",
                    _ => "📄"
                };

                var updatedDisplay = "";
                if (DateTime.TryParse(d.UpdatedAt ?? d.CaptureTime, out var updated))
                {
                    var timeSpan = DateTime.UtcNow - updated;
                    if (timeSpan.TotalHours < 24)
                        updatedDisplay = "Today";
                    else if (timeSpan.TotalDays < 7)
                        updatedDisplay = $"{(int)timeSpan.TotalDays}d ago";
                    else
                updatedDisplay = updated.ToString("MMM dd", CultureInfo.CurrentCulture);
                }

                return new BranchDocRow
                {
                    DocumentId = d.Id,
                    StatusIcon = statusIcon,
                    DocumentRef = BuildDocumentReference(d, d.Id),
                    UpdatedDisplay = updatedDisplay
                };
            })
            .ToList();

        BranchDocsGrid.ItemsSource = recentDocs;
    }

    private void LoadCriticalIssues()
    {
        // Get documents with Issue status or critical/high notes
        var issueNotes = _notesStore.Search(type: NoteType.Issue, status: NoteStatus.Open, limit: 500)
            .Where(n => n.Severity == NoteSeverity.Critical || n.Severity == NoteSeverity.High)
            .ToList();

        // Apply filter
        var filter = IssuesFilterCombo.SelectedItem?.ToString() ?? "All";
        if (filter == "Assigned to Me")
        {
            // Get document IDs of assignments assigned to me
            var myAssignmentDocIds = _assignmentStore.ListAll(limit: DashboardAssignmentListLimit)
                .Where(a => a.AssignedToUserId == _currentUserId)
                .Select(a => a.DocumentId)
                .ToHashSet();
            issueNotes = issueNotes.Where(n => myAssignmentDocIds.Contains(n.DocumentId)).ToList();
        }
        else if (filter == "My Branch")
        {
            issueNotes = issueNotes.Where(n =>
            {
                var gr = _store.GetResult(n.DocumentId);
                if (!gr.IsSuccess)
                {
                    _log.Warning("Could not load document {Id} for branch filter: {Error}", n.DocumentId, gr.Error);
                    return false;
                }
                return UserBranchMatchesDocument(gr.Value!);
            }).ToList();
        }
        else if (filter == "High Priority")
        {
            issueNotes = issueNotes.Where(n => n.Severity == NoteSeverity.Critical).ToList();
        }

        var issueRows = new List<IssueRow>();
        foreach (var note in issueNotes)
        {
            var getResult = _store.GetResult(note.DocumentId);
            var documentRef = getResult.IsSuccess
                ? BuildDocumentReference(getResult.Value, note.DocumentId)
                : $"Doc #{note.DocumentId}";
            if (!getResult.IsSuccess)
                _log.Warning("Could not load document {Id} for issue row: {Error}", note.DocumentId, getResult.Error);

            // Find assignment
            var assignment = _assignmentStore.ListByDocument(note.DocumentId).FirstOrDefault();
            var assignedTo = assignment?.AssignedToUsername ?? "Unassigned";

            var issuePreview = note.Content.Length > 50
                ? note.Content.Substring(0, 50) + "..."
                : note.Content;

            var severityDisplay = note.Severity == NoteSeverity.Critical ? "🔴 Critical" : "🟠 High";

            issueRows.Add(new IssueRow
            {
                Note = note,
                DocumentId = note.DocumentId,
                DocumentRef = documentRef,
                IssuePreview = issuePreview,
                AssignedTo = assignedTo,
                SeverityDisplay = severityDisplay
            });
        }
        
        issueRows = issueRows
            .OrderByDescending(r => r.Note.Severity == NoteSeverity.Critical)
            .Take(20)
            .ToList();

        IssuesGrid.ItemsSource = issueRows;
    }

    private void LoadBranchTrends()
    {
        var last7Days = Enumerable.Range(0, 7)
            .Select(i => DateTime.Now.Date.AddDays(-i))
            .Reverse()
            .ToList();

        var branchDocs = _store.ListDocuments()
            .Where(UserBranchMatchesDocument)
            .ToList();

        // Trend 1: Documents Cleared
        var clearedTrend = last7Days.Select(date =>
        {
            var count = branchDocs.Count(d =>
                d.Status == Enums.Status.Cleared &&
                !string.IsNullOrEmpty(d.UpdatedAt) &&
                DateTime.TryParse(d.UpdatedAt, out var updated) &&
                updated.Date == date);

            return new TrendDataPoint
            {
                Date = date,
                Value = count,
                BarHeight = Math.Max(5, count * 10), // Min 5px, scale by 10
                Tooltip = $"{date:MMM dd}: {count} cleared"
            };
        }).ToList();
        TrendDocsProcessed.ItemsSource = clearedTrend;
        TrendDocsProcessedTotal.Text = $"{clearedTrend.Sum(t => t.Value)} total";

        // Trend 2: Issues Found
        var issuesTrend = last7Days.Select(date =>
        {
            var count = branchDocs.Count(d =>
                d.Status == Enums.Status.Issue &&
                d.CaptureDateTime.HasValue &&
                d.CaptureDateTime.Value.Date == date);

            return new TrendDataPoint
            {
                Date = date,
                Value = count,
                BarHeight = Math.Max(5, count * 10),
                Tooltip = $"{date:MMM dd}: {count} issues"
            };
        }).ToList();
        TrendIssuesFound.ItemsSource = issuesTrend;
        TrendIssuesFoundTotal.Text = $"{issuesTrend.Sum(t => t.Value)} total";

        // Trend 3: Pending Review (snapshot)
        var pendingTrend = last7Days.Select(date =>
        {
            // This is a simplification - ideally would track historical snapshots
            var count = branchDocs.Count(d => d.Status == Enums.Status.Reviewed);

            return new TrendDataPoint
            {
                Date = date,
                Value = count,
                BarHeight = Math.Max(5, count * 5),
                Tooltip = $"{date:MMM dd}: {count} pending"
            };
        }).ToList();
        TrendPendingReview.ItemsSource = pendingTrend;
        var avgPending = pendingTrend.Any() ? (int)pendingTrend.Average(t => t.Value) : 0;
        TrendPendingReviewTotal.Text = $"{avgPending} avg";

        // Trend 4: Completion Rate (assignments)
        var completionTrend = last7Days.Select(date =>
        {
            var dayAssignments = _assignmentStore.ListAll(limit: DashboardAssignmentListLimit)
                .Where(a => a.AssignedToUserId == _currentUserId &&
                           !string.IsNullOrEmpty(a.CompletedAt) &&
                           DateTime.TryParse(a.CompletedAt, out var completed) &&
                           completed.Date == date)
                .Count();

            var rate = dayAssignments; // Simplified - could calculate actual rate

            return new TrendDataPoint
            {
                Date = date,
                Value = rate,
                BarHeight = Math.Max(5, rate * 10),
                Tooltip = $"{date:MMM dd}: {rate} completed"
            };
        }).ToList();
        TrendCompletionRate.ItemsSource = completionTrend;
        var avgRate = completionTrend.Any() ? (int)completionTrend.Average(t => t.Value) : 0;
        TrendCompletionRateTotal.Text = $"{avgRate} avg";
    }

    private void LoadJournalEntry()
    {
        var today = DateTime.Today;
        var todayString = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var todayJournal = _notesStore.Search(type: NoteType.Journal, limit: 100)
            .FirstOrDefault(n => n.CreatedByUserId == _currentUserId &&
                                n.Category == todayString);

        if (todayJournal != null)
        {
            JournalTextBox.Text = todayJournal.Content;
            _journalModified = false;
        }
        else
        {
            JournalTextBox.Text = "";
            _journalModified = false;
        }

        JournalHeader.Text = $"📝 Daily Journal - {today:dddd, MMMM dd, yyyy}";
    }

    private void SaveJournalEntry()
    {
        if (!_journalModified || string.IsNullOrWhiteSpace(JournalTextBox.Text))
            return;

        try
        {
            var today = DateTime.Today;
            var todayString = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var journalText = JournalTextBox.Text.Trim();

            var existingJournal = _notesStore.Search(type: NoteType.Journal, limit: 100)
                .FirstOrDefault(n => n.CreatedByUserId == _currentUserId &&
                                    n.Category == todayString);

            if (existingJournal == null)
            {
                // Create new journal entry
                _notesStore.Add(new Note
                {
                    Type = NoteType.Journal,
                    Content = journalText,
                    Category = todayString,
                    DocumentId = 0, // Sentinel for day-level note
                    DocumentUuid = "", 
                    CreatedByUserId = _currentUserId,
                    CreatedBy = _currentUsername,
                    Severity = NoteSeverity.Info,
                    Status = NoteStatus.Open
                });
            }
            else
            {
                // Update existing journal entry
                existingJournal.Content = journalText;
                existingJournal.UpdatedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                existingJournal.UpdatedBy = _currentUsername;
                _notesStore.Update(existingJournal);
            }

            _journalModified = false;
            JournalSaveStatus.Text = $"Auto-saved at {DateTime.Now:HH:mm}";
            JournalSaveStatus.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            // Silent fail for auto-save
            System.Diagnostics.Debug.WriteLine($"Journal auto-save failed: {ex.Message}");
        }
    }

    private void LoadActivityFeed()
    {
        var last24Hours = DateTime.UtcNow.AddHours(-24);
        var activities = new List<ActivityFeedItem>();

        // My notes
        var myNotes = _notesStore.Search(limit: 100)
            .Where(n => n.CreatedByUserId == _currentUserId &&
                       !string.IsNullOrEmpty(n.CreatedAt) &&
                       DateTime.TryParse(n.CreatedAt, out var created) &&
                       created > last24Hours &&
                       n.Type != NoteType.Journal) // Exclude journal entries
            .OrderByDescending(n => n.CreatedAt)
            .Take(15)
            .ToList();

        foreach (var note in myNotes)
        {
            var gr = _store.GetResult(note.DocumentId);
            if (!gr.IsSuccess)
                _log.Warning("Could not load document {Id} for activity feed: {Error}", note.DocumentId, gr.Error);
            var docRef = gr.IsSuccess
                ? BuildDocumentReference(gr.Value, note.DocumentId)
                : $"Doc #{note.DocumentId}";
            var description = $"Added {note.Type} note to {docRef}";
            var relativeTime = GetRelativeTime(note.CreatedAt);

            activities.Add(new ActivityFeedItem
            {
                Icon = note.TypeIcon,
                Description = description,
                RelativeTime = relativeTime,
                DocumentId = note.DocumentId
            });
        }

        // My assignment activity
        var myAssignmentActivity = _assignmentStore.ListAll(limit: DashboardAssignmentListLimit)
            .Where(a => a.AssignedToUserId == _currentUserId)
            .ToList();

        foreach (var assignment in myAssignmentActivity)
        {
            if (!string.IsNullOrEmpty(assignment.StartedAt) &&
                DateTime.TryParse(assignment.StartedAt, out var started) &&
                started > last24Hours)
            {
                var gr = _store.GetResult(assignment.DocumentId);
                if (!gr.IsSuccess)
                    _log.Warning("Could not load document {Id} for activity feed: {Error}", assignment.DocumentId, gr.Error);
                var docRef = gr.IsSuccess
                    ? BuildDocumentReference(gr.Value, assignment.DocumentId)
                    : $"Doc #{assignment.DocumentId}";
                activities.Add(new ActivityFeedItem
                {
                    Icon = "▶️",
                    Description = $"Started review of {docRef}",
                    RelativeTime = GetRelativeTime(assignment.StartedAt),
                    AssignmentId = assignment.Id,
                    DocumentId = assignment.DocumentId
                });
            }

            if (!string.IsNullOrEmpty(assignment.CompletedAt) &&
                DateTime.TryParse(assignment.CompletedAt, out var completed) &&
                completed > last24Hours)
            {
                var gr = _store.GetResult(assignment.DocumentId);
                if (!gr.IsSuccess)
                    _log.Warning("Could not load document {Id} for activity feed: {Error}", assignment.DocumentId, gr.Error);
                var docRef = gr.IsSuccess
                    ? BuildDocumentReference(gr.Value, assignment.DocumentId)
                    : $"Doc #{assignment.DocumentId}";
                activities.Add(new ActivityFeedItem
                {
                    Icon = "✅",
                    Description = $"Completed assignment for {docRef}",
                    RelativeTime = GetRelativeTime(assignment.CompletedAt),
                    AssignmentId = assignment.Id,
                    DocumentId = assignment.DocumentId
                });
            }
        }

        // Sort by time and limit
        var sortedActivities = activities
            .OrderByDescending(a => a.RelativeTime)
            .Take(20)
            .ToList();

        ActivityFeedList.ItemsSource = sortedActivities;
    }

    private void LoadAlerts()
    {
        var alerts = new List<AlertItem>();

        // Overdue assignments
        var now = DateTime.UtcNow;
        var overdueCount = _assignmentStore.ListAll(limit: DashboardAssignmentListLimit)
            .Where(a => a.AssignedToUserId == _currentUserId &&
                       a.Status != AssignmentStatus.Completed &&
                       a.Status != AssignmentStatus.Cancelled &&
                       !string.IsNullOrEmpty(a.DueDate) &&
                       DateTime.TryParse(a.DueDate, out var due) &&
                       due < now)
            .Count();

        if (overdueCount > 0)
        {
            alerts.Add(new AlertItem
            {
                Icon = "⚠️",
                Message = $"You have {overdueCount} overdue assignment(s)",
                Type = "overdue"
            });
        }

        // Critical issues in my assignments
        var myAssignmentDocIds = _assignmentStore.ListAll(limit: DashboardAssignmentListLimit)
            .Where(a => a.AssignedToUserId == _currentUserId)
            .Select(a => a.DocumentId)
            .ToHashSet();

        var criticalIssuesCount = _notesStore.Search(type: NoteType.Issue, status: NoteStatus.Open, limit: 500)
            .Where(n => n.Severity == NoteSeverity.Critical && myAssignmentDocIds.Contains(n.DocumentId))
            .Count();

        if (criticalIssuesCount > 0)
        {
            alerts.Add(new AlertItem
            {
                Icon = "🔴",
                Message = $"{criticalIssuesCount} critical issue(s) in your assignments require attention",
                Type = "critical_issue"
            });
        }

        // Notifications from managers
        var unreadNotifications = _notificationStore.GetUnreadCount(_currentUserId);
        if (unreadNotifications > 0)
        {
            alerts.Add(new AlertItem
            {
                Icon = "📧",
                Message = $"You have {unreadNotifications} unread notification(s) from managers",
                Type = "notification"
            });
        }

        if (alerts.Any())
        {
            AlertsList.ItemsSource = alerts;
            AlertsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            AlertsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private string GetRelativeTime(string dateTimeString)
    {
        if (string.IsNullOrEmpty(dateTimeString))
            return "";

        if (DateTime.TryParse(dateTimeString, out var dateTime))
        {
            var timeSpan = DateTime.UtcNow - dateTime;
            if (timeSpan.TotalMinutes < 1) return "just now";
            if (timeSpan.TotalMinutes < 60) return $"{(int)timeSpan.TotalMinutes}m ago";
            if (timeSpan.TotalHours < 24) return $"{(int)timeSpan.TotalHours}h ago";
            return $"{(int)timeSpan.TotalDays}d ago";
        }

        return "";
    }

    // Event Handlers
    private void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        LoadDashboardData();
    }

    private void AssignmentFilterCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            LoadMyAssignments();
    }

    private void IssuesFilterCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            LoadCriticalIssues();
    }

    private void AssignmentsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AssignmentsGrid.SelectedItem is AssignmentRow row)
        {
            NavigateToDocument(row.Assignment.DocumentId);
        }
    }

    private void BranchDocsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BranchDocsGrid.SelectedItem is BranchDocRow row)
        {
            NavigateToDocument(row.DocumentId);
        }
    }

    private void IssuesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IssuesGrid.SelectedItem is IssueRow row)
        {
            NavigateToDocument(row.DocumentId);
        }
    }

    private void BtnStartReview_Click(object sender, RoutedEventArgs e)
    {
        if (AssignmentsGrid.SelectedItem is AssignmentRow row)
        {
            _assignmentStore.UpdateStatus(row.Assignment.Id, AssignmentStatus.InProgress,
                startedAt: DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            NavigateToDocument(row.Assignment.DocumentId);
            LoadDashboardData();
        }
        else
        {
            MessageBox.Show("Please select an assignment first.", "No Selection",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnMarkComplete_Click(object sender, RoutedEventArgs e)
    {
        if (AssignmentsGrid.SelectedItem is AssignmentRow row)
        {
            var getResult = _store.GetResult(row.Assignment.DocumentId);
            if (!getResult.IsSuccess)
            {
                _log.Warning("Could not load document {Id}: {Error}", row.Assignment.DocumentId, getResult.Error);
                MessageBox.Show($"Failed to load document: {getResult.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var doc = getResult.Value!;

            var dialog = new MarkCompleteAssignmentDialog(doc);
            if (dialog.ShowDialog() == true)
            {
                _assignmentStore.UpdateStatus(row.Assignment.Id, AssignmentStatus.Completed,
                    completedAt: DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    completionNotes: dialog.CompletionContent);

                // Create completion note
                _notesStore.Add(new Note
                {
                    DocumentId = doc.Id,
                    DocumentUuid = doc.Uuid,
                    Type = dialog.SelectedType,
                    Severity = dialog.SelectedSeverity,
                    Content = dialog.CompletionContent,
                    CreatedBy = _currentUsername,
                    CreatedByUserId = _currentUserId
                });

                MessageBox.Show("Assignment marked as complete.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                LoadDashboardData();
            }
        }
        else
        {
            MessageBox.Show("Please select an assignment first.", "No Selection",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnViewDocument_Click(object sender, RoutedEventArgs e)
    {
        if (AssignmentsGrid.SelectedItem is AssignmentRow row)
        {
            NavigateToDocument(row.Assignment.DocumentId);
        }
        else
        {
            MessageBox.Show("Please select an assignment first.", "No Selection",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnViewIssue_Click(object sender, RoutedEventArgs e)
    {
        if (IssuesGrid.SelectedItem is IssueRow row)
        {
            NavigateToDocument(row.DocumentId);
        }
        else
        {
            MessageBox.Show("Please select an issue first.", "No Selection",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnAddNote_Click(object sender, RoutedEventArgs e)
    {
        if (IssuesGrid.SelectedItem is IssueRow row)
        {
            var getResult = _store.GetResult(row.DocumentId);
            if (!getResult.IsSuccess)
            {
                _log.Warning("Could not load document {Id}: {Error}", row.DocumentId, getResult.Error);
                MessageBox.Show($"Failed to load document: {getResult.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var doc = getResult.Value!;

            var docName = System.IO.Path.GetFileName(doc.FilePath) ?? doc.Uuid;
            var dialog = new NotesDialog(doc.Id, doc.Uuid, docName);
            dialog.ShowDialog();
            LoadDashboardData();
        }
        else
        {
            MessageBox.Show("Please select an issue first.", "No Selection",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnMarkResolved_Click(object sender, RoutedEventArgs e)
    {
        if (IssuesGrid.SelectedItem is IssueRow row)
        {
            var dialog = new ResolutionDialog();
            if (dialog.ShowDialog() == true)
            {
                row.Note.Status = NoteStatus.Resolved;
                row.Note.ResolvedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                row.Note.ResolvedBy = _currentUsername;
                row.Note.ResolutionComment = dialog.ResolutionComment;
                _notesStore.Update(row.Note);

                MessageBox.Show("Issue marked as resolved.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                LoadDashboardData();
            }
        }
        else
        {
            MessageBox.Show("Please select an issue first.", "No Selection",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void JournalTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_journalModified)
        {
            SaveJournalEntry();
        }
    }

    private void BtnViewJournalHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DailyJournalHistoryDialog(_currentUserId);
        dialog.ShowDialog();
    }

    private void ActivityFeedItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is ActivityFeedItem item)
        {
            if (item.DocumentId.HasValue)
            {
                NavigateToDocument(item.DocumentId.Value);
            }
        }
    }

    private void DismissAlerts_Click(object sender, RoutedEventArgs e)
    {
        AlertsPanel.Visibility = Visibility.Collapsed;
    }

    private void ViewAlert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is AlertItem alert)
        {
            if (alert.Type == "overdue" || alert.Type == "critical_issue")
            {
                // Scroll to assignments/issues section
                LoadDashboardData();
            }
            else if (alert.Type == "notification")
            {
                BtnQuickNotifications_Click(sender, e);
            }
        }
    }

    // Quick Actions
    private void BtnQuickSearch_Click(object sender, RoutedEventArgs e)
    {
        NavigateToView(4); // Archive view (has search)
    }

    private void BtnQuickReport_Click(object sender, RoutedEventArgs e)
    {
        NavigateToView(7); // Auditor Reports view (My Reports)
    }

    private void BtnQuickWorkspace_Click(object sender, RoutedEventArgs e)
    {
        NavigateToView(3); // Workspace view
    }

    private void BtnQuickAddNote_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Please select a document in Workspace to add notes.",
            "Add Note", MessageBoxButton.OK, MessageBoxImage.Information);
        NavigateToView(3);
    }

    private void BtnQuickCompleted_Click(object sender, RoutedEventArgs e)
    {
        // Show completed assignments
        AssignmentFilterCombo.SelectedIndex = 0; // Reset to "All"
        MessageBox.Show("View your completed work in Assignment Management.",
            "Completed Work", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnQuickNotifications_Click(object sender, RoutedEventArgs e)
    {
        var notifications = _notificationStore.GetByUser(_currentUserId, unreadOnly: false, limit: 50);
        var message = notifications.Any()
            ? $"You have {notifications.Count} notification(s).\n\n" +
              string.Join("\n", notifications.Take(5).Select(n => $"• {n.Title}"))
            : "No notifications.";

        MessageBox.Show(message, "Notifications", MessageBoxButton.OK, MessageBoxImage.Information);

        // Mark all as read
        _notificationStore.MarkAllRead(_currentUserId);
        LoadDashboardData();
    }

    private void NavigateToDocument(int documentId)
    {
        var getResult = _store.GetResult(documentId);
        if (!getResult.IsSuccess)
        {
            _log.Warning("Could not load document {Id}: {Error}", documentId, getResult.Error);
            MessageBox.Show($"Failed to load document: {getResult.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            NavigateToView(3); // Still switch to Workspace
            return;
        }
        var doc = getResult.Value!;
        DrillDownRequested.Raise(new DrillDownRequest
        {
            Branch = doc.Branch,
            Section = doc.Section,
            DocumentId = documentId
        });
        NavigateToView(3); // Workspace view
    }

    private void NavigateToView(int viewIndex)
    {
        // Find MainWindow and navigate
        var mainWindow = Window.GetWindow(this) as MainWindow;
        mainWindow?.NavigateToView(viewIndex);
    }

    private static string BuildDocumentReference(Document? doc, int fallbackId)
    {
        if (doc == null)
            return $"Doc #{fallbackId}";

        var engagement = (doc.Engagement ?? "").Trim();
        var section = (doc.Section ?? "").Trim();

        if (!string.IsNullOrEmpty(engagement) && !string.IsNullOrEmpty(section))
            return $"{engagement}/{section}";
        if (!string.IsNullOrEmpty(engagement))
            return engagement;
        if (!string.IsNullOrEmpty(section))
            return section;

        return $"Doc #{fallbackId}";
    }

    // Data Models
    private class AssignmentRow
    {
        public DocumentAssignment Assignment { get; set; } = null!;
        public string DocumentRef { get; set; } = "";
        public string PriorityIcon { get; set; } = "";
        public string DueDateDisplay { get; set; } = "";
        public string StatusBadge { get; set; } = "";
    }

    private class BranchDocRow
    {
        public int DocumentId { get; set; }
        public string StatusIcon { get; set; } = "";
        public string DocumentRef { get; set; } = "";
        public string UpdatedDisplay { get; set; } = "";
    }

    private class IssueRow
    {
        public Note Note { get; set; } = null!;
        public int DocumentId { get; set; }
        public string DocumentRef { get; set; } = "";
        public string IssuePreview { get; set; } = "";
        public string AssignedTo { get; set; } = "";
        public string SeverityDisplay { get; set; } = "";
    }

    private class TrendDataPoint
    {
        public DateTime Date { get; set; }
        public int Value { get; set; }
        public double BarHeight { get; set; }
        public string Tooltip { get; set; } = "";
    }

    private class ActivityFeedItem
    {
        public string Icon { get; set; } = "";
        public string Description { get; set; } = "";
        public string RelativeTime { get; set; } = "";
        public int? DocumentId { get; set; }
        public int? AssignmentId { get; set; }
    }

    private class AlertItem
    {
        public string Icon { get; set; } = "";
        public string Message { get; set; } = "";
        public string Type { get; set; } = "";
    }
}
