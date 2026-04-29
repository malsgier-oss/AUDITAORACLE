using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Serilog;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Services;
using WorkAudit.Core.TeamTasks;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views.Admin;

public partial class TeamTaskManagementView : UserControl
{
    private static readonly ILogger Log = LoggingService.ForContext<TeamTaskManagementView>();
    private ITeamTaskService? _service;
    private IUserStore? _userStore;
    private IConfigStore? _configStore;
    private readonly List<TeamTaskRow> _rows = new();

    public TeamTaskManagementView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ServiceContainer.IsInitialized) return;
        _service = ServiceContainer.GetService<ITeamTaskService>();
        _userStore = ServiceContainer.GetService<IUserStore>();
        _configStore = ServiceContainer.GetService<IConfigStore>();
        ApplyLocalization();
        LoadAssigneeFilter();
        RefreshList();
    }

    private void ApplyLocalization()
    {
        if (_configStore == null) return;
        var L = (string key) => ReportLocalizationService.GetString(key, _configStore);
        HeaderTitle.Text = L("TeamTaskManagementTitle");
        HeaderSubtitle.Text = L("TeamTaskManagementSubtitle");
        FilterAssigneeLabel.Text = L("TeamTaskFilterAssignee");
        RefreshBtn.Content = L("Refresh");
        ColTitle.Header = L("TeamTaskColTitle");
        ColAssignee.Header = L("TeamTaskColAssignee");
        ColRecurrence.Header = L("TeamTaskColRecurrence");
        ColStart.Header = L("TeamTaskColStart");
        ColEnd.Header = L("TeamTaskColEnd");
        ColActive.Header = L("TeamTaskColActive");
        AddBtn.Content = L("AddNew");
        EditBtn.Content = L("Edit");
        DeleteBtn.Content = L("Delete");
    }

    private void LoadAssigneeFilter()
    {
        AssigneeFilterCombo.Items.Clear();
        AssigneeFilterCombo.Items.Add("(All)");
        if (_userStore != null)
        {
            foreach (var u in _userStore.ListUsers(isActive: true).OrderBy(u => u.DisplayName ?? u.Username))
                AssigneeFilterCombo.Items.Add(u.DisplayName ?? u.Username);
        }
        AssigneeFilterCombo.SelectedIndex = 0;
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => RefreshList();

    private void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshList();

    private void RefreshList()
    {
        if (_service == null || _userStore == null) return;

        try
        {
            int? assigneeId = null;
            var filter = AssigneeFilterCombo.SelectedItem as string;
            if (!string.IsNullOrEmpty(filter) && filter != "(All)")
            {
                var u = _userStore.ListUsers(isActive: true)
                    .FirstOrDefault(x => (x.DisplayName ?? x.Username) == filter);
                if (u != null)
                    assigneeId = u.Id;
            }

            var list = _service.ListAllForManagement(assigneeId);
            _rows.Clear();
            foreach (var t in list)
                _rows.Add(new TeamTaskRow(t));
            TasksGrid.ItemsSource = _rows.ToList();
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(ex.Message, "Team tasks", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load team tasks");
            MessageBox.Show($"Failed to load: {ex.Message}", "Team tasks", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TasksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSel = TasksGrid.SelectedItem is TeamTaskRow;
        EditBtn.IsEnabled = hasSel;
        DeleteBtn.IsEnabled = hasSel;
    }

    private void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_service == null || _userStore == null) return;
        var dlg = new TeamTaskEditDialog(null, _userStore);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true && dlg.ResultTask != null)
        {
            try
            {
                _service.Create(
                    dlg.ResultTask.Title,
                    dlg.ResultTask.Description,
                    dlg.ResultTask.AssignedToUserId,
                    dlg.ResultTask.Recurrence,
                    dlg.StartDateLocal,
                    dlg.EndDateLocal,
                    dlg.ResultTask.IsActive);
                RefreshList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Team tasks", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void EditBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TasksGrid.SelectedItem is not TeamTaskRow row || _service == null || _userStore == null) return;
        var dlg = new TeamTaskEditDialog(row.Task, _userStore);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true && dlg.ResultTask != null)
        {
            try
            {
                var t = dlg.ResultTask;
                t.Id = row.Task.Id;
                t.Uuid = row.Task.Uuid;
                t.AssignedByUserId = row.Task.AssignedByUserId;
                t.AssignedByUsername = row.Task.AssignedByUsername;
                t.CreatedAt = row.Task.CreatedAt;
                t.StartDate = dlg.StartDateLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                t.EndDate = dlg.EndDateLocal?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!_service.Update(t))
                    MessageBox.Show("Update failed.", "Team tasks", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Team tasks", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TasksGrid.SelectedItem is not TeamTaskRow row || _service == null) return;
        if (MessageBox.Show($"Delete task \"{row.Title}\"?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            if (!_service.Delete(row.Task.Id))
                MessageBox.Show("Delete failed.", "Team tasks", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Team tasks", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal sealed class TeamTaskRow
    {
        public TeamTaskRow(TeamTask t)
        {
            Task = t;
        }
        public TeamTask Task { get; }
        public string Title => Task.Title;
        public string AssigneeDisplay => Task.AssignedToUsername;
        public string RecurrenceDisplay => Task.Recurrence;
        public string StartDate => Task.StartDate;
        public string EndDateDisplay => string.IsNullOrEmpty(Task.EndDate) ? "—" : Task.EndDate!;
        public bool IsActive => Task.IsActive;
        public string ActiveDisplay => Task.IsActive ? "Yes" : "No";
    }
}
