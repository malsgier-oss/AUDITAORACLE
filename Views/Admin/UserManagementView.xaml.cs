using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WorkAudit.Core.Services;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views.Admin;

public partial class UserManagementView : UserControl
{
    private readonly IUserStore _userStore;
    private readonly IPasswordService _passwordService;
    private readonly IPermissionService _permissionService;
    private readonly IAuditTrailService _auditTrail;
    private List<User> _users = new();

    public UserManagementView()
    {
        InitializeComponent();
        _userStore = ServiceContainer.GetService<IUserStore>();
        _passwordService = ServiceContainer.GetService<IPasswordService>();
        _permissionService = ServiceContainer.GetService<IPermissionService>();
        _auditTrail = ServiceContainer.GetService<IAuditTrailService>();

        Loaded += (s, e) => Refresh();
        UpdateButtonVisibility();
    }

    private void UpdateButtonVisibility()
    {
        var canManage = _permissionService.HasPermission(Permissions.UserCreate);
        var canEdit = _permissionService.HasPermission(Permissions.UserEdit);
        var canDelete = _permissionService.HasPermission(Permissions.UserDelete);

        BtnAdd.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
        BtnEdit.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
        BtnResetPassword.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
        BtnEmergencyCodes.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
        BtnUnlock.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
        BtnDelete.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh()
    {
        _users = _userStore.ListUsers(limit: 500);
        UsersGrid.ItemsSource = null;
        UsersGrid.ItemsSource = _users;
        StatusText.Text = $"{_users.Count} user(s)";
        UpdateButtonVisibility();
    }

    private User? GetSelectedUser()
    {
        return UsersGrid.SelectedItem as User;
    }

    private void UsersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = GetSelectedUser() != null;
        BtnEdit.IsEnabled = hasSelection;
        BtnResetPassword.IsEnabled = hasSelection;
        BtnEmergencyCodes.IsEnabled = hasSelection && GetSelectedUser()?.Role == Roles.Administrator;
        BtnUnlock.IsEnabled = hasSelection;
        BtnDelete.IsEnabled = hasSelection;
    }

    private async void UsersGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not User user) return;

        var header = (e.Column.Header as string) ?? "";
        if (header != "Active" && header != "Locked") return;

        if (!_permissionService.HasPermission(Permissions.UserEdit))
            return;

        if (header == "Locked" && !user.IsLocked)
            user.FailedLoginAttempts = 0;

        var config = ServiceContainer.GetService<AppConfiguration>();
        user.UpdatedBy = config.CurrentUserName ?? "admin";
        user.UpdatedAt = DateTime.UtcNow.ToString("O");

        _userStore.Update(user);

        // Invalidate all sessions for this user when deactivating or locking so they lose access immediately
        if (!user.IsActive || user.IsLocked)
            _userStore.InvalidateUserSessions(user.Id);

        var action = header == "Active"
            ? (user.IsActive ? Domain.AuditAction.UserActivated : Domain.AuditAction.UserDeactivated)
            : (user.IsLocked ? Domain.AuditAction.UserLocked : Domain.AuditAction.UserUnlocked);
        await _auditTrail.LogAsync(action, Domain.AuditCategory.User, "User", user.Uuid);
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.AddEditUserDialog();
        if (dlg.ShowDialog() == true && dlg.User != null)
        {
            var config = ServiceContainer.GetService<AppConfiguration>();
            dlg.User.CreatedBy = config.CurrentUserName ?? "admin";
            dlg.User.PasswordHash = _passwordService.HashPassword(dlg.NewPassword!);
            dlg.User.MustChangePassword = true;
            _userStore.Insert(dlg.User);
            await _auditTrail.LogAsync(Domain.AuditAction.UserCreated, Domain.AuditCategory.User, "User", dlg.User.Uuid);
            Refresh();
        }
    }

    private async void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        var user = GetSelectedUser();
        if (user == null) return;

        var dlg = new Dialogs.AddEditUserDialog(user);
        if (dlg.ShowDialog() == true && dlg.User != null)
        {
            var config = ServiceContainer.GetService<AppConfiguration>();
            dlg.User.UpdatedBy = config.CurrentUserName ?? "admin";
            if (!string.IsNullOrEmpty(dlg.NewPassword))
            {
                _userStore.UpdatePassword(dlg.User.Id, _passwordService.HashPassword(dlg.NewPassword), config.CurrentUserName, requirePasswordChangeOnNextLogin: true);
            }
            _userStore.Update(dlg.User);
            await _auditTrail.LogAsync(Domain.AuditAction.UserUpdated, Domain.AuditCategory.User, "User", dlg.User.Uuid);
            Refresh();
        }
    }

    private async void BtnResetPassword_Click(object sender, RoutedEventArgs e)
    {
        var user = GetSelectedUser();
        if (user == null) return;

        var dlg = new Dialogs.PasswordResetDialog(user);
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.NewPassword))
        {
            var config = ServiceContainer.GetService<AppConfiguration>();
            _userStore.UpdatePassword(user.Id, _passwordService.HashPassword(dlg.NewPassword), config.CurrentUserName, requirePasswordChangeOnNextLogin: true);
            await _auditTrail.LogAsync(Domain.AuditAction.PasswordReset, Domain.AuditCategory.User, "User", user.Uuid);
            Refresh();
        }
    }

    private async void BtnEmergencyCodes_Click(object sender, RoutedEventArgs e)
    {
        var user = GetSelectedUser();
        if (user == null || user.Role != Roles.Administrator)
            return;
        if (!_permissionService.HasPermission(Permissions.UserEdit))
            return;

        if (MessageBox.Show(
                "This removes all existing emergency codes for this administrator and creates 10 new one-time codes. Continue?",
                "Regenerate emergency codes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var plain = _passwordService.GenerateEmergencyAccessCodes(10);
        var hashes = plain.Select(p => _passwordService.HashPassword(p)).ToList();
        _userStore.ReplaceEmergencyCodes(user.Id, hashes);

        var codesDlg = new Dialogs.EmergencyCodesDisplayDialog(plain, user.Username);
        codesDlg.Owner = Window.GetWindow(this);
        codesDlg.ShowDialog();

        await _auditTrail.LogAsync(Domain.AuditAction.EmergencyCodesRegenerated, Domain.AuditCategory.User, "User",
            user.Uuid, details: "Administrator emergency access codes regenerated");
    }

    private async void BtnUnlock_Click(object sender, RoutedEventArgs e)
    {
        var user = GetSelectedUser();
        if (user == null) return;

        user.IsLocked = false;
        user.FailedLoginAttempts = 0;
        var config = ServiceContainer.GetService<AppConfiguration>();
        user.UpdatedBy = config.CurrentUserName ?? "admin";
        _userStore.Update(user);
        await _auditTrail.LogAsync(Domain.AuditAction.UserUnlocked, Domain.AuditCategory.User, "User", user.Uuid);
        Refresh();
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        var user = GetSelectedUser();
        if (user == null) return;

        if (MessageBox.Show($"Delete user '{user.Username}'? This cannot be undone.", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _userStore.Delete(user.Id);
        await _auditTrail.LogAsync(Domain.AuditAction.UserDeleted, Domain.AuditCategory.User, "User", user.Uuid);
        Refresh();
    }
}
