using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;
using WorkAudit.Core.Assignment;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views.Admin;

public partial class AssignmentManagementView : UserControl
{
    private static readonly ILogger Log = LoggingService.ForContext<AssignmentManagementView>();
    private IDocumentAssignmentService? _assignmentService;
    private IDocumentStore? _documentStore;
    private IUserStore? _userStore;
    private IPermissionService? _permissionService;
    private List<AssignmentRow> _rows = new();
    private AssignmentRow? _draggedRow;

    public AssignmentManagementView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ServiceContainer.IsInitialized) return;
        _assignmentService = ServiceContainer.GetService<IDocumentAssignmentService>();
        _documentStore = ServiceContainer.GetService<IDocumentStore>();
        _userStore = ServiceContainer.GetService<IUserStore>();
        _permissionService = ServiceContainer.GetOptionalService<IPermissionService>();

        LoadUserFilter();
        LoadStatusFilter();
        LoadUserDropTargets();
        RefreshList();
    }

    private void LoadUserDropTargets()
    {
        UserDropTargetList.Items.Clear();
        if (_userStore == null) return;
        foreach (var u in _userStore.ListUsers(isActive: true).OrderBy(u => u.DisplayName ?? u.Username))
            UserDropTargetList.Items.Add(u.DisplayName ?? u.Username);
    }

    private void LoadUserFilter()
    {
        UserFilterCombo.Items.Clear();
        UserFilterCombo.Items.Add("(All users)");
        if (_userStore != null)
        {
            foreach (var u in _userStore.ListUsers(isActive: true).OrderBy(u => u.DisplayName ?? u.Username))
                UserFilterCombo.Items.Add(u.DisplayName ?? u.Username);
        }
        UserFilterCombo.SelectedIndex = 0;
    }

    private void LoadStatusFilter()
    {
        StatusFilterCombo.Items.Clear();
        StatusFilterCombo.Items.Add("(All)");
        StatusFilterCombo.Items.Add(AssignmentStatus.Pending);
        StatusFilterCombo.Items.Add(AssignmentStatus.InProgress);
        StatusFilterCombo.Items.Add(AssignmentStatus.Completed);
        StatusFilterCombo.Items.Add(AssignmentStatus.Cancelled);
        StatusFilterCombo.SelectedIndex = 0;
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => RefreshList();

    private void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshList();

    private void RefreshList()
    {
        if (_assignmentService == null || _documentStore == null) return;

        var userFilter = UserFilterCombo.SelectedItem as string;
        if (userFilter == "(All users)") userFilter = null;

        var statusFilter = StatusFilterCombo.SelectedItem as string;
        if (statusFilter == "(All)") statusFilter = null;

        var assignments = _assignmentService.GetAllAssignments(userFilter, statusFilter);
        _rows.Clear();
        foreach (var a in assignments)
        {
            var getResult = _documentStore!.GetResult(a.DocumentId);
            var docName = getResult.IsSuccess ? Path.GetFileName(getResult.Value!.FilePath) ?? getResult.Value.Uuid : $"Document #{a.DocumentId}";
            if (!getResult.IsSuccess)
                Log.Warning("Could not load document {Id} for assignment list: {Error}", a.DocumentId, getResult.Error);
            var isOverdue = _assignmentService.IsOverdue(a);
            _rows.Add(new AssignmentRow(a, docName, isOverdue));
        }
        AssignmentsGrid.ItemsSource = _rows.ToList();
    }

    private void AssignmentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = GetSelectedRows().ToList();
        var canAct = selected.Count > 0 && _permissionService?.HasMinimumRole(Roles.Manager) == true;
        var canReassignCancel = selected.Any(r => r.Assignment.Status is AssignmentStatus.Pending or AssignmentStatus.InProgress);

        ReassignBtn.IsEnabled = canAct && selected.Count == 1 && canReassignCancel;
        CancelAssignBtn.IsEnabled = canAct && canReassignCancel;
        BulkAssignBtn.IsEnabled = canAct && selected.Count >= 1;
        ViewDocumentBtn.IsEnabled = canAct && selected.Count == 1;
    }

    private IEnumerable<AssignmentRow> GetSelectedRows()
    {
        if (AssignmentsGrid.SelectedItems.Count == 0) yield break;
        foreach (var item in AssignmentsGrid.SelectedItems)
            if (item is AssignmentRow row)
                yield return row;
    }

    private void ReassignBtn_Click(object sender, RoutedEventArgs e)
    {
        var row = AssignmentsGrid.SelectedItem as AssignmentRow;
        if (row == null || _assignmentService == null || _documentStore == null) return;

        var user = ServiceContainer.GetService<ISessionService>()?.CurrentUser;
        if (user == null) return;

        if (row.Assignment.Status != AssignmentStatus.Pending && row.Assignment.Status != AssignmentStatus.InProgress)
        {
            MessageBox.Show("Can only reassign Pending or In Progress assignments.", "Reassign", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var getResult = _documentStore.GetResult(row.Assignment.DocumentId);
        if (!getResult.IsSuccess)
        {
            Log.Warning("Could not load document {Id} for reassign: {Error}", row.Assignment.DocumentId, getResult.Error);
            MessageBox.Show($"Failed to load document: {getResult.Error}", "Reassign", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var doc = getResult.Value!;

        _assignmentService.CancelAssignment(row.Assignment.Id, user);
        var dlg = new Dialogs.AssignDocumentDialog(new[] { doc }, user);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true)
            RefreshList();
    }

    private void CancelAssignBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows().Where(r => r.Assignment.Status is AssignmentStatus.Pending or AssignmentStatus.InProgress).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select Pending or In Progress assignments to cancel.", "Cancel", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var user = ServiceContainer.GetService<ISessionService>()?.CurrentUser;
        if (user == null) return;

        var result = MessageBox.Show($"Cancel {selected.Count} assignment(s)?", "Cancel Assignments", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var cancelled = 0;
        foreach (var row in selected)
        {
            if (_assignmentService!.CancelAssignment(row.Assignment.Id, user))
                cancelled++;
        }
        MessageBox.Show($"{cancelled} assignment(s) cancelled.", "Cancel", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshList();
    }

    private void BulkAssignBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows().ToList();
        if (selected.Count == 0) return;

        var user = ServiceContainer.GetService<ISessionService>()?.CurrentUser;
        if (user == null) return;

        var docs = selected.Select(r => _documentStore!.GetById(r.Assignment.DocumentId)).Where(d => d != null).Cast<Domain.Document>().ToList();
        if (docs.Count == 0)
        {
            MessageBox.Show("No valid documents found for selected assignments.", "Bulk Assign", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Cancel current assignments before reassigning
        foreach (var row in selected.Where(r => r.Assignment.Status is AssignmentStatus.Pending or AssignmentStatus.InProgress))
            _assignmentService!.CancelAssignment(row.Assignment.Id, user);

        var dlg = new Dialogs.AssignDocumentDialog(docs, user);
        dlg.Owner = Window.GetWindow(this);
        dlg.Title = "Bulk Assign Documents";
        if (dlg.ShowDialog() == true)
            RefreshList();
    }

    private void AssignmentsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row == null) return;
        var item = row.Item as AssignmentRow;
        if (item == null || item.Assignment.Status is AssignmentStatus.Completed or AssignmentStatus.Cancelled) return;
        if (_permissionService?.HasMinimumRole(Roles.Manager) != true) return;
        _draggedRow = item;
        DragDrop.DoDragDrop(AssignmentsGrid, new System.Windows.DataObject("AssignmentRow", item), DragDropEffects.Move);
        _draggedRow = null;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void UserDropTarget_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("AssignmentRow"))
            e.Effects = DragDropEffects.Move;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void UserDropTarget_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("AssignmentRow")) return;
        var row = e.Data.GetData("AssignmentRow") as AssignmentRow;
        if (row == null || _assignmentService == null || _userStore == null) return;

        var listBox = (System.Windows.Controls.ListBox)sender;
        var username = GetUsernameFromDropTarget(e.OriginalSource, listBox);
        if (string.IsNullOrEmpty(username)) return;

        var user = _userStore.ListUsers(isActive: true).FirstOrDefault(u => (u.DisplayName ?? u.Username) == username);
        if (user == null) return;

        var currentUser = ServiceContainer.GetService<ISessionService>()?.CurrentUser;
        if (currentUser == null) return;

        if (_assignmentService.ReassignTo(row.Assignment.Id, user, currentUser))
        {
            MessageBox.Show($"Reassigned to {username}.", "Reassign", MessageBoxButton.OK, MessageBoxImage.Information);
            Dispatcher.BeginInvoke(new Action(RefreshList), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
            MessageBox.Show("Reassign failed.", "Reassign", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string? GetUsernameFromDropTarget(object? source, System.Windows.Controls.ListBox listBox)
    {
        var elem = source as DependencyObject;
        while (elem != null)
        {
            if (elem is System.Windows.Controls.ListBoxItem lbi && lbi.DataContext is string s)
                return s;
            elem = VisualTreeHelper.GetParent(elem);
        }
        return null;
    }

    private void AssignmentsGrid_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void AssignmentsGrid_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
    }

    private void ViewDocumentBtn_Click(object sender, RoutedEventArgs e)
    {
        var row = AssignmentsGrid.SelectedItem as AssignmentRow;
        if (row == null || _documentStore == null) return;

        var getResult = _documentStore.GetResult(row.Assignment.DocumentId);
        if (!getResult.IsSuccess)
        {
            Log.Warning("Could not load document {Id}: {Error}", row.Assignment.DocumentId, getResult.Error);
            MessageBox.Show($"Failed to load document: {getResult.Error}", "View Document", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var doc = getResult.Value!;
        if (_permissionService != null && !_permissionService.CanAccessDocument(doc))
        {
            MessageBox.Show("You do not have access to this document.", "View Document", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(doc.FilePath) || !File.Exists(doc.FilePath))
        {
            MessageBox.Show("Document file not found.", "View Document", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(doc.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open file: {ex.Message}", "View Document", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class AssignmentRow
    {
        public DocumentAssignment Assignment { get; }
        public string DocumentName { get; }
        public string AssignedToUsername => Assignment.AssignedToUsername;
        public string Priority => Assignment.Priority;
        public string PriorityIcon => Assignment.Priority switch { AssignmentPriority.Urgent => "!!", AssignmentPriority.High => "↑", AssignmentPriority.Low => "↓", _ => "•" };
        public string PriorityDisplay => $"{PriorityIcon} {Assignment.Priority}";
        public string DueDateDisplay => GetDueDateCountdown(Assignment.DueDate, Assignment.Status, IsOverdue);
        public string StatusDisplay => Assignment.Status + (IsOverdue ? " (Overdue)" : "");
        public string StatusBadgeColor => Assignment.Status switch { AssignmentStatus.Pending => "#0078D4", AssignmentStatus.InProgress => "#FFC107", AssignmentStatus.Completed => "#28A745", _ => "#6C757D" };
        public string DueDateForeground => IsOverdue ? "#DC3545" : "#333333";
        public string AssignedByUsername => Assignment.AssignedByUsername;

        private bool IsOverdue { get; }

        public AssignmentRow(DocumentAssignment a, string docName, bool isOverdue)
        {
            Assignment = a;
            DocumentName = docName;
            IsOverdue = isOverdue;
        }

        private static string GetDueDateCountdown(string? dueDate, string status, bool isOverdue)
        {
            if (string.IsNullOrEmpty(dueDate) || status == AssignmentStatus.Completed || status == AssignmentStatus.Cancelled)
                return "-";
            if (!DateTime.TryParse(dueDate, out var due)) return dueDate;
            var today = DateTime.Today;
            var diff = (due.Date - today).Days;
            if (diff < 0) return $"Overdue by {-diff} day{(-diff == 1 ? "" : "s")}";
            if (diff == 0) return "Due today";
            if (diff == 1) return "Due tomorrow";
            return $"Due in {diff} days";
        }
    }
}
