using System.Windows;
using System.Windows.Controls;
using WorkAudit.Core.Backup;
using WorkAudit.Core.Services;
using WorkAudit.Dialogs;
using WorkAudit.Storage;

namespace WorkAudit.Views.Tools;

public partial class BackupToolsPanel : UserControl
{
    public BackupToolsPanel()
    {
        InitializeComponent();
    }

    private void ExternalBackup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new BackupDialog { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    private async void VerifyRecent_Click(object sender, RoutedEventArgs e)
    {
        VerifyRecentBtn.IsEnabled = false;
        StatusText.Text = "Verifying...";
        try
        {
            var svc = ServiceContainer.GetService<IBackupVerificationService>();
            var (valid, total, _) = await svc.VerifyRecentBackupsAsync(5).ConfigureAwait(true);
            StatusText.Text = total == 0
                ? "No backups found in the default backup folder."
                : $"Verified {valid} of {total} recent backup(s). Check logs for details on failures.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Verification error: {ex.Message}";
        }
        finally
        {
            VerifyRecentBtn.IsEnabled = true;
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RestoreBackupDialog { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }
}
