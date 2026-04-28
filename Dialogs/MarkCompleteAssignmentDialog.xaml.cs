using System.Windows;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Dialogs;

public partial class MarkCompleteAssignmentDialog : Window
{
    private readonly Document _document;

    public string CompletionContent => ContentBox?.Text?.Trim() ?? "";
    public string SelectedType => TypeCombo?.SelectedItem?.ToString() ?? NoteType.Evidence;
    public string SelectedSeverity => SeverityCombo?.SelectedItem?.ToString() ?? NoteSeverity.Info;

    public MarkCompleteAssignmentDialog(Document document)
    {
        InitializeComponent();
        _document = document;

        var docName = System.IO.Path.GetFileName(document.FilePath) ?? document.Uuid;
        DocumentNameText.Text = $"Document: {docName}";

        TypeCombo.ItemsSource = NoteType.Values;
        TypeCombo.SelectedItem = NoteType.Evidence;

        SeverityCombo.ItemsSource = NoteSeverity.Values;
        SeverityCombo.SelectedItem = NoteSeverity.Info;
    }

    private void MarkCompleteAssignmentDialog_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        if (!ServiceContainer.IsInitialized) return;
        var config = ServiceContainer.GetService<IConfigStore>();
        if (config == null) return;
        Title = ReportLocalizationService.GetString("MarkComplete", config);
        CompletionNoteLabel.Text = ReportLocalizationService.GetString("CompletionNote", config);
        TypeLabel.Text = ReportLocalizationService.GetString("CompletionType", config);
        SeverityLabel.Text = ReportLocalizationService.GetString("CompletionSeverity", config);
        OkBtn.Content = ReportLocalizationService.GetString("MarkComplete", config);
        CancelBtn.Content = ReportLocalizationService.GetString("Cancel", config);
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CompletionContent))
        {
            var config = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
            var msg = config != null
                ? ReportLocalizationService.GetString("CompletionNoteRequired", config)
                : "Please enter a completion note.";
            MessageBox.Show(msg, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            ContentBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
