using System.IO;
using System.Windows;
using WorkAudit.Core.Services;
using WorkAudit.Storage;

namespace WorkAudit.Dialogs;

/// <summary>
/// Dialog to create a backup to a user-chosen folder (e.g. USB or network drive).
/// Phase 7.3 Backup & Recovery: Export to External Drive.
/// </summary>
public partial class BackupDialog : Window
{
    private readonly IBackupService _backupService;

    public BackupDialog()
    {
        InitializeComponent();
        _backupService = ServiceContainer.GetService<IBackupService>();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select backup destination (e.g. USB or network drive)",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
        {
            PathBox.Text = dialog.SelectedPath;
        }
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var folder = PathBox.Text?.Trim();
        if (string.IsNullOrEmpty(folder))
        {
            MessageBox.Show("Please select a destination folder.", "Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(folder))
        {
            MessageBox.Show("The selected folder does not exist. Please choose a valid path.", "Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var includeDocuments = IncludeDocumentsCheck?.IsChecked == true;
        var backupPath = Path.Combine(folder, $"WorkAudit_Backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip");

        CreateBackupBtn.IsEnabled = false;
        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = "Creating backup...";
        StatusText.Foreground = System.Windows.Media.Brushes.LightGray;

        try
        {
            var result = await _backupService.CreateBackupAsync(backupPath, includeDocuments);

            if (result.Success)
            {
                if (result.SkippedFiles.Count > 0)
                {
                    StatusText.Foreground = System.Windows.Media.Brushes.Orange;
                    StatusText.Text = $"Backup created (some files skipped): {result.BackupPath}\nSize: {result.SizeBytes / 1024:N0} KB";
                    MessageBox.Show(
                        $"Backup saved to:\n{result.BackupPath}\n\nSize: {result.SizeBytes / 1024:N0} KB\n\n{result.SkippedFiles.Count} file(s) could not be included. Check the application log for details.",
                        "Backup complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    StatusText.Text = $"Backup created: {result.BackupPath}\nSize: {result.SizeBytes / 1024:N0} KB";
                    MessageBox.Show($"Backup saved to:\n{result.BackupPath}\n\nSize: {result.SizeBytes / 1024:N0} KB", "Backup complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                DialogResult = true;
                Close();
            }
            else
            {
                StatusText.Foreground = System.Windows.Media.Brushes.Orange;
                StatusText.Text = result.Error ?? "Backup failed.";
                MessageBox.Show(result.Error ?? "Backup failed.", "Backup", MessageBoxButton.OK, MessageBoxImage.Error);
                CreateBackupBtn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            StatusText.Foreground = System.Windows.Media.Brushes.Orange;
            StatusText.Text = ex.Message;
            MessageBox.Show($"Backup failed: {ex.Message}", "Backup", MessageBoxButton.OK, MessageBoxImage.Error);
            CreateBackupBtn.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
