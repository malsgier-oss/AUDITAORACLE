using System.Windows;

namespace WorkAudit.Dialogs;

public partial class ExportPasswordDialog : Window
{
    public string? Password { get; private set; }

    public ExportPasswordDialog(bool requireConfirm = true)
    {
        InitializeComponent();
        if (!requireConfirm)
        {
            ConfirmLabel.Visibility = Visibility.Collapsed;
            ConfirmBox.Visibility = Visibility.Collapsed;
            Title = "Decrypt Export";
        }
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        var pwd = PasswordBox.Password;
        if (string.IsNullOrEmpty(pwd))
        {
            MessageBox.Show("Password is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (ConfirmBox.Visibility == Visibility.Visible)
        {
            if (pwd != ConfirmBox.Password)
            {
                MessageBox.Show("Passwords do not match.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        Password = pwd;
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
