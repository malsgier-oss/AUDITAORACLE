using System.Windows;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Services;
using WorkAudit.Core.TeamTasks;
using WorkAudit.Storage;

namespace WorkAudit.Dialogs;

public partial class TeamTaskNoteDialog
{
    private readonly int _taskId;
    private readonly ITeamTaskService _service;
    private readonly IConfigStore _configStore;

    public TeamTaskNoteDialog(int taskId, string taskTitle, string? initialNote)
    {
        InitializeComponent();
        _taskId = taskId;
        _service = ServiceContainer.GetService<ITeamTaskService>();
        _configStore = ServiceContainer.GetService<IConfigStore>();
        TaskTitleBlock.Text = taskTitle;
        NoteBox.Text = initialNote ?? "";
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        Title = ReportLocalizationService.GetString("TeamTaskNoteDialogTitle", _configStore);
        HintBlock.Text = ReportLocalizationService.GetString("TeamTaskNoteHint", _configStore);
        OkBtn.Content = ReportLocalizationService.GetString("OK", _configStore);
        CancelBtn.Content = ReportLocalizationService.GetString("Cancel", _configStore);
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_service.SaveMyNote(_taskId, NoteBox.Text))
            {
                MessageBox.Show(
                    ReportLocalizationService.GetString("TeamTaskNoteSaveFailed", _configStore),
                    ReportLocalizationService.GetString("TeamTaskNoteDialogTitle", _configStore),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message,
                ReportLocalizationService.GetString("TeamTaskNoteDialogTitle", _configStore),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
