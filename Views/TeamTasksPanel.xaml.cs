using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Services;
using WorkAudit.Core.TeamTasks;
using WorkAudit.Dialogs;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views;

public partial class TeamTasksPanel : UserControl
{
    private bool _suppressToggle;
    private ITeamTaskService? _service;
    private IConfigStore? _configStore;

    public TeamTasksPanel()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (ServiceContainer.IsInitialized)
            {
                _service = ServiceContainer.GetService<ITeamTaskService>();
                _configStore = ServiceContainer.GetService<IConfigStore>();
                ApplyLocalization();
            }
            Refresh();
        };
    }

    private void ApplyLocalization()
    {
        if (_configStore == null) return;
        var L = (string key) => ReportLocalizationService.GetString(key, _configStore);
        SectionTitle.Text = L("MyTeamTasksTitle");
        EmptyText.Text = L("MyTeamTasksEmpty");
        SectionSubtitle.Text = L("MyTeamTasksSubtitleWithNote");
    }

    public void Refresh()
    {
        if (_service == null && ServiceContainer.IsInitialized)
            _service = ServiceContainer.GetService<ITeamTaskService>();
        if (_configStore == null && ServiceContainer.IsInitialized)
            _configStore = ServiceContainer.GetService<IConfigStore>();

        if (_service == null)
        {
            PanelBorder.Visibility = Visibility.Collapsed;
            return;
        }

        IReadOnlyList<TeamTaskWithState> list;
        try
        {
            list = _service.GetMyTasksWithState();
        }
        catch
        {
            PanelBorder.Visibility = Visibility.Collapsed;
            return;
        }

        if (list.Count == 0)
        {
            PanelBorder.Visibility = Visibility.Collapsed;
            return;
        }

        PanelBorder.Visibility = Visibility.Visible;
        EmptyText.Visibility = Visibility.Collapsed;
        if (_configStore != null)
            SectionSubtitle.Text = ReportLocalizationService.GetString("MyTeamTasksSubtitleWithNote", _configStore);

        var rows = new List<TeamTaskRowVm>();
        var noteTip = _configStore != null
            ? ReportLocalizationService.GetString("TeamTaskClickForNote", _configStore)
            : "";
        var noteInline = _configStore != null
            ? ReportLocalizationService.GetString("TeamTaskNoteInlineBadge", _configStore)
            : "";
        foreach (var x in list)
        {
            var t = x.Task;
            var periodLabel = FormatPeriodLabel(t.Recurrence, x.PeriodKey);
            var detail = $"{t.Recurrence} · {periodLabel}";
            if (x.HasNoteForCurrentPeriod && !string.IsNullOrEmpty(noteInline))
                detail += $" · {noteInline}";
            rows.Add(new TeamTaskRowVm
            {
                TaskId = t.Id,
                Title = t.Title,
                DetailLine = detail,
                IsCompleted = x.IsCompletedForCurrentPeriod,
                NoteTooltip = noteTip
            });
        }

        _suppressToggle = true;
        TasksItems.ItemsSource = rows;
        _suppressToggle = false;
    }

    private static string FormatPeriodLabel(string recurrence, string periodKey)
    {
        return recurrence switch
        {
            TeamTaskRecurrence.Daily => periodKey,
            TeamTaskRecurrence.Weekly => $"ISO week {periodKey}",
            TeamTaskRecurrence.Monthly => periodKey,
            _ => periodKey
        };
    }

    private void TaskCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle || sender is not System.Windows.Controls.CheckBox cb || cb.DataContext is not TeamTaskRowVm vm)
            return;
        if (_service == null) return;
        try
        {
            _service.ToggleCompletion(vm.TaskId);
        }
        catch
        {
            // ignore
        }
        Refresh();
    }

    private void TaskBody_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TeamTaskRowVm vm)
            return;
        e.Handled = true;
        if (_service == null) return;
        string? initial;
        try
        {
            initial = _service.GetMyNote(vm.TaskId);
        }
        catch
        {
            return;
        }
        var dlg = new TeamTaskNoteDialog(vm.TaskId, vm.Title, initial) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            Refresh();
    }

    private sealed class TeamTaskRowVm
    {
        public int TaskId { get; set; }
        public string Title { get; set; } = "";
        public string DetailLine { get; set; } = "";
        public string NoteTooltip { get; set; } = "";
        public bool IsCompleted { get; set; }
    }
}
