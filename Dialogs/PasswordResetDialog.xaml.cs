using System.Windows;
using System.Windows.Controls;
using WorkAudit.Core.Services;
using WorkAudit.Core.Security;
using WorkAudit.Domain;

namespace WorkAudit.Dialogs;

public partial class PasswordResetDialog : Window
{
    private readonly IPasswordService _passwordService;
    private bool _syncingPasswordFields;
    private bool _syncingConfirmFields;

    public string? NewPassword { get; private set; }

    public PasswordResetDialog(User user, bool isMandatoryChangeAfterLogin = false)
    {
        InitializeComponent();
        _passwordService = ServiceContainer.GetService<IPasswordService>();
        UserLabel.Text = isMandatoryChangeAfterLogin
            ? $"You must set a new password before continuing.\nUser: {user.Username}"
            : $"Set a new password for: {user.Username}";
    }

    private string GetPrimaryPassword() =>
        PasswordPlainText.Visibility == Visibility.Visible
            ? PasswordPlainText.Text ?? ""
            : PasswordBox.Password;

    private string GetConfirmPassword() =>
        ConfirmPlainText.Visibility == Visibility.Visible
            ? ConfirmPlainText.Text ?? ""
            : ConfirmPasswordBox.Password;

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPasswordFields || PasswordPlainText.Visibility == Visibility.Visible) return;
        _syncingPasswordFields = true;
        try { PasswordPlainText.Text = PasswordBox.Password; }
        finally { _syncingPasswordFields = false; }
    }

    private void PasswordPlainText_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingPasswordFields || PasswordBox.Visibility == Visibility.Visible) return;
        _syncingPasswordFields = true;
        try { PasswordBox.Password = PasswordPlainText.Text ?? ""; }
        finally { _syncingPasswordFields = false; }
    }

    private void ConfirmPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingConfirmFields || ConfirmPlainText.Visibility == Visibility.Visible) return;
        _syncingConfirmFields = true;
        try { ConfirmPlainText.Text = ConfirmPasswordBox.Password; }
        finally { _syncingConfirmFields = false; }
    }

    private void ConfirmPlainText_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingConfirmFields || ConfirmPasswordBox.Visibility == Visibility.Visible) return;
        _syncingConfirmFields = true;
        try { ConfirmPasswordBox.Password = ConfirmPlainText.Text ?? ""; }
        finally { _syncingConfirmFields = false; }
    }

    private void ShowPasswordCheck_OnChecked(object sender, RoutedEventArgs e) => ApplyShowPasswords(true);

    private void ShowPasswordCheck_OnUnchecked(object sender, RoutedEventArgs e) => ApplyShowPasswords(false);

    private void ApplyShowPasswords(bool show)
    {
        if (show)
        {
            PasswordPlainText.Text = PasswordBox.Password;
            PasswordPlainText.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;

            ConfirmPlainText.Text = ConfirmPasswordBox.Password;
            ConfirmPlainText.Visibility = Visibility.Visible;
            ConfirmPasswordBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            PasswordBox.Password = PasswordPlainText.Text ?? "";
            PasswordBox.Visibility = Visibility.Visible;
            PasswordPlainText.Visibility = Visibility.Collapsed;

            ConfirmPasswordBox.Password = ConfirmPlainText.Text ?? "";
            ConfirmPasswordBox.Visibility = Visibility.Visible;
            ConfirmPlainText.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        var password = GetPrimaryPassword();
        var confirm = GetConfirmPassword();

        if (string.IsNullOrEmpty(password))
        {
            MessageBox.Show("Please enter a new password.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            MessageBox.Show("The two passwords do not match. Please try again.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_passwordService.ValidatePasswordStrength(password, out var errors))
        {
            MessageBox.Show(string.Join("\n", errors), "Password Requirements", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        NewPassword = password;
        DialogResult = true;
        Close();
    }
}
