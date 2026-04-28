using System.Windows;
using System.Windows.Controls;
using WorkAudit.Core.Services;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Dialogs;

public partial class AddEditUserDialog : Window
{
    private readonly bool _isEdit;
    private readonly IUserStore _userStore;
    private readonly IPasswordService _passwordService;
    private bool _syncingPasswordFields;
    private bool _syncingConfirmFields;

    public User? User { get; private set; }
    public string? NewPassword { get; private set; }

    public AddEditUserDialog(User? existingUser = null)
    {
        InitializeComponent();
        _userStore = ServiceContainer.GetService<IUserStore>();
        _passwordService = ServiceContainer.GetService<IPasswordService>();
        _isEdit = existingUser != null;

        Title = _isEdit ? "Edit User" : "Add User";
        PasswordHint.Visibility = _isEdit ? Visibility.Visible : Visibility.Collapsed;

        RoleCombo.ItemsSource = Roles.AllRoles;
        RoleCombo.SelectedIndex = 0;

        BranchCombo.Items.Clear();
        BranchCombo.Items.Add(""); // Optional - no branch
        BranchCombo.Items.Add(Branches.AllBranchesLabel);
        foreach (var b in Branches.All) BranchCombo.Items.Add(b);
        BranchCombo.SelectedIndex = 0;

        if (existingUser != null)
        {
            User = existingUser;
            UsernameBox.Text = existingUser.Username;
            UsernameBox.IsEnabled = false; // Can't change username
            DisplayNameBox.Text = existingUser.DisplayName;
            RoleCombo.SelectedItem = existingUser.Role;
            if (Branches.ScopesToAllBranches(existingUser.Branch))
                BranchCombo.SelectedItem = Branches.AllBranchesLabel;
            else
                BranchCombo.SelectedItem = string.IsNullOrEmpty(existingUser.Branch) ? "" : existingUser.Branch;
        }
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
        var username = UsernameBox.Text?.Trim();
        var displayName = DisplayNameBox.Text?.Trim();
        var password = GetPrimaryPassword();
        var confirm = GetConfirmPassword();
        var role = RoleCombo.SelectedItem as string ?? Roles.Auditor;
        var branch = BranchCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(branch))
            branch = null;
        else if (Branches.ScopesToAllBranches(branch))
            branch = Branches.AllBranchesLabel;

        if (string.IsNullOrEmpty(username))
        {
            MessageBox.Show("Username is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(displayName))
        {
            MessageBox.Show("Display name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_isEdit && string.IsNullOrEmpty(password))
        {
            MessageBox.Show("Password is required for new users.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrEmpty(password) || !string.IsNullOrEmpty(confirm))
        {
            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                MessageBox.Show("Password and confirmation do not match.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (!_isEdit)
        {
            if (!_passwordService.ValidatePasswordStrength(password, out var errors))
            {
                MessageBox.Show(string.Join("\n", errors), "Password Requirements", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else if (!string.IsNullOrEmpty(password))
        {
            if (!_passwordService.ValidatePasswordStrength(password, out var errors))
            {
                MessageBox.Show(string.Join("\n", errors), "Password Requirements", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (!_isEdit && _userStore.GetByUsername(username) != null)
        {
            MessageBox.Show("Username already exists.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_isEdit && User != null)
        {
            User.DisplayName = displayName;
            User.Role = role;
            User.Branch = branch;
            NewPassword = string.IsNullOrEmpty(password) ? null : password;
        }
        else
        {
            User = new Domain.User
            {
                Username = username,
                DisplayName = displayName,
                Role = role,
                Branch = branch,
                IsActive = true,
                Email = "",
                PasswordHash = "" // Will be set by caller
            };
            NewPassword = password;
        }

        DialogResult = true;
        Close();
    }
}
