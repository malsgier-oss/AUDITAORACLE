using System.IO;
using System.Windows;
using Microsoft.Win32;
using WorkAudit.Core.Services;
using WorkAudit.Storage;

namespace WorkAudit.Dialogs;

public partial class RestoreBackupDialog : Window
{
    private readonly IBackupService _backupService;

    public RestoreBackupDialog()
    {
        InitializeComponent();
        _backupService = ServiceContainer.GetService<IBackupService>();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "WorkAudit backups (*.zip)|*.zip|All files (*.*)|*.*",
            Title = "Select backup ZIP"
        };
        if (dlg.ShowDialog() == true)
            PathBox.Text = dlg.FileName;
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text?.Trim();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            MessageBox.Show("Please select a valid backup ZIP file.", "Restore", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var pwd = DecryptPasswordBox.Password;
        var usePwd = !string.IsNullOrEmpty(pwd) ? pwd : null;

        var confirm = MessageBox.Show(
            "Restoring may overwrite documents and optionally the Oracle schema. Continue?",
            "Confirm restore",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        RestoreBtn.IsEnabled = false;
        StatusText.Text = "Restoring... This may take several minutes.";
        try
        {
            var options = new RestoreBackupOptions
            {
                RestoreOracleSchema = RestoreOracleCheck.IsChecked == true,
                CreateSafetyBackup = SafetyBackupCheck.IsChecked == true,
                SafetyBackupIncludeOracle = RestoreOracleCheck.IsChecked == true
            };

            var result = await _backupService.RestoreBackupAsync(path, usePwd, options).ConfigureAwait(true);
            if (result.Success)
            {
                MessageBox.Show(
                    $"Restore completed.\n\nRestored from snapshot: {result.RestoredFrom}\n\nRestart the application if needed.",
                    "Restore",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                StatusText.Text = result.Error ?? "Restore failed.";
                MessageBox.Show(result.Error ?? "Restore failed.", "Restore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Restore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RestoreBtn.IsEnabled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
