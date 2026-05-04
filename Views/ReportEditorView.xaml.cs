using System.IO;
using System.Windows;
using System.Windows.Controls;
using Serilog;
using WorkAudit.Core.Helpers;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Services;
using WorkAudit.Domain;

namespace WorkAudit.Views;

public partial class ReportEditorView : UserControl
{
    private static readonly ILogger Log = LoggingService.ForContext<ReportEditorView>();
    private IReportDraftService? _draftService;
    private ReportDraft? _currentDraft;
    private bool _webViewInitialized;

    public ReportEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ServiceContainer.IsInitialized) return;

        _draftService = ServiceContainer.GetService<IReportDraftService>();

        try
        {
            await InitializeWebView();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize WebView2");
            ShowMessage(
                "Failed to initialize the embedded browser. Ensure Evergreen WebView2 is installed, or ship the fixed runtime under WebView2Runtime next to the app.",
                isError: true);
        }
    }

    private async Task InitializeWebView()
    {
        try
        {
            var env = await WebView2EnvironmentHelper.CreateForAppAsync("ReportEditor");
            await EditorWebView.EnsureCoreWebView2Async(env);
            
            _webViewInitialized = true;
            LoadingText.Visibility = Visibility.Collapsed;
            
            Log.Information("WebView2 initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebView2 initialization failed");
            throw;
        }
    }

    public async void LoadDraft(int draftId)
    {
        if (_draftService == null)
        {
            ShowMessage("Draft service not available", isError: true);
            return;
        }

        try
        {
            _currentDraft = _draftService.GetDraft(draftId);
            if (_currentDraft == null)
            {
                ShowMessage("Draft not found", isError: true);
                return;
            }

            TitleTextBox.Text = _currentDraft.Title ?? "";
            TagsTextBox.Text = _currentDraft.Tags ?? "";
            NotesTextBox.Text = _currentDraft.Notes ?? "";
            FinalizedCheckBox.IsChecked = _currentDraft.IsFinalized;

            SubtitleText.Text = $"{_currentDraft.ReportType} • Created {_currentDraft.CreatedAt}";
            StatusText.Text = $"Last modified: {_currentDraft.LastModifiedAt ?? "Never"}";

            if (_webViewInitialized && File.Exists(_currentDraft.DraftFilePath))
            {
                var htmlContent = await File.ReadAllTextAsync(_currentDraft.DraftFilePath);
                EditorWebView.NavigateToString(htmlContent);
                ShowMessage("Draft loaded successfully", isError: false);
            }
            else if (!_webViewInitialized)
            {
                ShowMessage("Waiting for editor to initialize...", isError: false);
            }
            else
            {
                ShowMessage("Draft file not found", isError: true);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load draft {DraftId}", draftId);
            ShowMessage($"Error loading draft: {ex.Message}", isError: true);
        }
    }

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDraft == null || _draftService == null)
        {
            ShowMessage("No draft to save", isError: true);
            return;
        }

        try
        {
            _currentDraft.Title = TitleTextBox.Text;
            _currentDraft.Tags = TagsTextBox.Text;
            _currentDraft.Notes = NotesTextBox.Text;
            _currentDraft.IsFinalized = FinalizedCheckBox.IsChecked ?? false;

            if (_webViewInitialized)
            {
                var htmlContent = await EditorWebView.ExecuteScriptAsync("document.documentElement.outerHTML");
                htmlContent = System.Text.Json.JsonSerializer.Deserialize<string>(htmlContent) ?? "";
                _draftService.UpdateDraftContent(_currentDraft.Id, htmlContent);
            }

            _draftService.UpdateDraft(_currentDraft);

            StatusText.Text = $"Last modified: {DateTime.UtcNow:O}";
            ShowMessage("Draft saved successfully", isError: false);
            
            Log.Information("Draft {DraftId} saved successfully", _currentDraft.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save draft");
            ShowMessage($"Error saving draft: {ex.Message}", isError: true);
        }
    }

    private async void ExportPdfBtn_Click(object sender, RoutedEventArgs e)
    {
        await ExportDraft(ReportFormat.Pdf);
    }

    private async void ExportExcelBtn_Click(object sender, RoutedEventArgs e)
    {
        await ExportDraft(ReportFormat.Excel);
    }

    private async Task ExportDraft(ReportFormat format)
    {
        if (_currentDraft == null || _draftService == null)
        {
            ShowMessage("No draft to export", isError: true);
            return;
        }

        try
        {
            SaveBtn_Click(this, new RoutedEventArgs());
            await Task.Delay(500);

            var path = _draftService.ExportDraft(_currentDraft.Id, format);

            ShowMessage($"Exported to: {Path.GetFileName(path)}", isError: false);

            var result = MessageBox.Show(
                $"Report exported successfully.\n\n{path}\n\nOpen file?",
                "Export Complete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                if (!ReportOutputLauncher.TryOpen(path, out var openError) && !string.IsNullOrEmpty(openError))
                    MessageBox.Show(openError, "Open report", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            Log.Information("Draft {DraftId} exported to {Format} at {Path}", _currentDraft.Id, format, path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export draft");
            ShowMessage($"Error exporting: {ex.Message}", isError: true);
        }
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDraft == null || _draftService == null)
        {
            ShowMessage("No draft to delete", isError: true);
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to delete this draft?\n\n{_currentDraft.Title ?? "Untitled"}",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            _draftService.DeleteDraft(_currentDraft.Id);
            ShowMessage("Draft deleted", isError: false);
            Log.Information("Draft {DraftId} deleted", _currentDraft.Id);

            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.NavigateToReportsForCurrentRole();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete draft");
            ShowMessage($"Error deleting draft: {ex.Message}", isError: true);
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        mainWindow?.NavigateToReportsForCurrentRole();
    }

    private void ShowMessage(string message, bool isError = false)
    {
        if (MessageText != null)
        {
            MessageText.Text = message;
            MessageText.Foreground = isError
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69));
        }
    }
}
