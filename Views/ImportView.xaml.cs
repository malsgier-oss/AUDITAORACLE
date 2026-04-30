using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WorkAudit.Config;
using WorkAudit.Core.Import;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;

namespace WorkAudit.Views;

public partial class ImportView : UserControl, IDisposable
{
    private readonly IImportService _importService;
    private readonly InputView? _parentInput;
    private CancellationTokenSource? _importCts;

    public ImportView(InputView? parentInput = null)
    {
        InitializeComponent();
        _importService = ServiceContainer.GetService<IImportService>();
        _parentInput = parentInput;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        // Branch and Date are populated by InputView; no setup needed here
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Dispose();
    }

    private ImportOptions GetDefaultOptions()
    {
        var config = ServiceContainer.GetService<AppConfiguration>();
        var baseDir = config?.BaseDirectory ?? Defaults.GetDefaultBaseDir();
        var userBranch = config?.CurrentUserBranch ?? Branches.Default;
        var permissionService = ServiceContainer.GetService<IPermissionService>();
        var session = ServiceContainer.GetService<ISessionService>();
        var canPickAnyBranch = permissionService.HasMinimumRole(Roles.Manager)
            || Branches.ScopesToAllBranches(session?.CurrentUser?.Branch);
        var branch = canPickAnyBranch
            ? (_parentInput?.BranchCombo?.SelectedItem as string ?? Branches.Default)
            : userBranch;
        return new ImportOptions
        {
            Branch = branch,
            Section = _parentInput?.GetSelectedSectionOrNull(),
            DocumentType = _parentInput?.GetSelectedDocumentTypeOrNull(),
            DocumentDate = _parentInput?.DocumentDatePicker?.SelectedDate ?? DateTime.Today,
            BaseDirectory = baseDir,
            CopyToBaseDir = true,
            IncludeSubfolders = false,
            SkipDuplicates = true
        };
    }

    private async void BtnImportFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Supported files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.pdf|Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|PDF|*.pdf|All files|*.*",
            Multiselect = true,
            Title = "Select files to import"
        };

        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
        {
            await ImportFilesAsync(dialog.FileNames);
        }
    }

    private async void BtnImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to import",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
        {
            await ImportFolderAsync(dialog.SelectedPath);
        }
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var items = (string[])e.Data.GetData(DataFormats.FileDrop);
        var files = new List<string>();

        foreach (var item in items)
        {
            if (File.Exists(item) && _importService.IsSupportedFile(item))
            {
                files.Add(item);
            }
            else if (Directory.Exists(item))
            {
                var dirFiles = _importService.GetSupportedExtensions()
                    .SelectMany(ext => Directory.GetFiles(item, $"*{ext}"))
                    .ToList();
                files.AddRange(dirFiles);
            }
        }

        if (files.Count > 0)
        {
            _ = ImportFilesAsync(files.Distinct().ToList()).ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                    Serilog.Log.Warning(t.Exception, "Import from drop failed");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
        else
        {
            MessageBox.Show("No supported files found. Supported: PNG, JPG, GIF, BMP, WEBP, PDF",
                "Import", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        e.Handled = true;
    }

    private void BtnCancelImport_Click(object sender, RoutedEventArgs e)
    {
        _importCts?.Cancel();
    }

    public async Task ImportFilesAsync(IEnumerable<string> filePaths)
    {
        var files = filePaths.ToList();
        if (files.Count == 0) return;

        BtnImportFiles.IsEnabled = false;
        BtnImportFolder.IsEnabled = false;
        _importCts = new CancellationTokenSource();
        if (BtnCancelImport != null) BtnCancelImport.Visibility = Visibility.Visible;
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.Value = 0;
        StatusText.Text = $"Importing {files.Count} file(s)...";

        try
        {
            var options = GetDefaultOptions();
            var progress = new Progress<ImportProgress>(p =>
            {
                ProgressBar.Value = p.PercentComplete;
                StatusText.Text = p.IsComplete
                    ? $"Done: {p.ProcessedCount} imported"
                    : $"Importing {p.ProcessedCount}/{p.TotalCount}...";
            });

            var result = await _importService.ImportFilesAsync(files, options, progress, _importCts.Token);

            StatusText.Text = $"Imported: {result.SuccessCount}, Failed: {result.FailedCount}, Skipped: {result.SkippedCount}";
            ShowImportCompletionDialog(result);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Import cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Import failed";
            MessageBox.Show($"Import error: {ex.Message}", "Import Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _importCts?.Dispose();
            _importCts = null;
            if (BtnCancelImport != null) BtnCancelImport.Visibility = Visibility.Collapsed;
            BtnImportFiles.IsEnabled = true;
            BtnImportFolder.IsEnabled = true;
            ProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    public async Task ImportFolderAsync(string folderPath)
    {
        BtnImportFiles.IsEnabled = false;
        BtnImportFolder.IsEnabled = false;
        _importCts = new CancellationTokenSource();
        if (BtnCancelImport != null) BtnCancelImport.Visibility = Visibility.Visible;
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.Value = 0;
        StatusText.Text = "Scanning folder...";

        try
        {
            var options = GetDefaultOptions();
            options.IncludeSubfolders = false;

            var progress = new Progress<ImportProgress>(p =>
            {
                ProgressBar.Value = p.PercentComplete;
                StatusText.Text = p.IsComplete
                    ? $"Done: {p.ProcessedCount} imported"
                    : $"Importing {p.ProcessedCount}/{p.TotalCount}...";
            });

            var result = await _importService.ImportFolderAsync(folderPath, options, progress, _importCts.Token);

            StatusText.Text = $"Imported: {result.SuccessCount}, Failed: {result.FailedCount}, Skipped: {result.SkippedCount}";
            ShowImportCompletionDialog(result);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Import cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Import failed";
            MessageBox.Show($"Import error: {ex.Message}", "Import Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _importCts?.Dispose();
            _importCts = null;
            if (BtnCancelImport != null) BtnCancelImport.Visibility = Visibility.Collapsed;
            BtnImportFiles.IsEnabled = true;
            BtnImportFolder.IsEnabled = true;
            ProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Shows errors and warnings (e.g. skipped duplicates) from an import run.</summary>
    private static void ShowImportCompletionDialog(ImportResult result)
    {
        if (!result.HasErrors && result.Warnings.Count == 0)
            return;

        const int maxLines = 12;
        var parts = new List<string>();
        if (result.Errors.Count > 0)
        {
            parts.Add("Errors:");
            parts.AddRange(result.Errors.Take(maxLines));
            if (result.Errors.Count > maxLines)
                parts.Add($"... and {result.Errors.Count - maxLines} more.");
        }
        if (result.Warnings.Count > 0)
        {
            if (parts.Count > 0)
                parts.Add("");
            parts.Add("Skipped / notes:");
            parts.AddRange(result.Warnings.Take(maxLines));
            if (result.Warnings.Count > maxLines)
                parts.Add($"... and {result.Warnings.Count - maxLines} more.");
        }

        var icon = result.HasErrors ? MessageBoxImage.Warning : MessageBoxImage.Information;
        var title = result.HasErrors ? "Import completed with errors" : "Import completed";
        MessageBox.Show(string.Join("\n", parts), title, MessageBoxButton.OK, icon);
    }

    public void Dispose()
    {
        _importCts?.Cancel();
        _importCts?.Dispose();
        _importCts = null;
        Unloaded -= OnUnloaded;
        GC.SuppressFinalize(this);
    }
}
