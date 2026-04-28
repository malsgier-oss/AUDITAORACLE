using System.Windows;
using System.Windows.Automation;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Services;
using WorkAudit.Core.Security;
using WorkAudit.Storage;

namespace WorkAudit.Dialogs;

/// <summary>
/// Login dialog for WorkAudit authentication.
/// Replaces IdentityDialog with full username/password login.
/// </summary>
public partial class LoginDialog : Window
{
    private readonly ISessionService _sessionService;
    private bool _passwordVisible;

    public LoginDialog()
    {
        InitializeComponent();
        _sessionService = ServiceContainer.GetService<ISessionService>();
        Loaded += (_, _) => ApplyLocalization();
        if (UsernameBox != null)
            UsernameBox.Focus();
    }

    private void ApplyLocalization()
    {
        var config = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
        if (config == null) return;
        // Keep shell layout fixed in LTR; language selection only changes localized strings.
        FlowDirection = ReportLocalizationService.ShellFlowDirection;
        Title = ReportLocalizationService.GetString("AuditaSignInWindowTitle", config);
        if (UsernameLabel != null) UsernameLabel.Text = ReportLocalizationService.GetString("Username", config);
        if (PasswordLabel != null) PasswordLabel.Text = ReportLocalizationService.GetString("Password", config);
        if (LoginButton != null) LoginButton.Content = ReportLocalizationService.GetString("SignIn", config);
        if (CancelButton != null) CancelButton.Content = ReportLocalizationService.GetString("Cancel", config);
        if (ProductTitleText != null) ProductTitleText.Text = ReportLocalizationService.GetString("AuditaProductTitle", config);
        if (TaglineText != null) TaglineText.Text = ReportLocalizationService.GetString("DocumentScannerForBankAuditors", config);
        UpdatePasswordVisibilityUi();
    }

    private void UpdatePasswordVisibilityUi()
    {
        var config = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
        if (TogglePasswordVisibilityBtn != null && config != null)
        {
            var tip = ReportLocalizationService.GetString(_passwordVisible ? "HidePassword" : "ShowPassword", config);
            TogglePasswordVisibilityBtn.ToolTip = tip;
            AutomationProperties.SetName(TogglePasswordVisibilityBtn, tip);
        }
        else if (TogglePasswordVisibilityBtn != null)
        {
            var tip = _passwordVisible ? "Hide password" : "Show password";
            TogglePasswordVisibilityBtn.ToolTip = tip;
            AutomationProperties.SetName(TogglePasswordVisibilityBtn, tip);
        }

        if (PasswordVisibilityIcon != null)
        {
            var hideGlyph = Application.Current?.TryFindResource("IconHidePassword") as string ?? "\uE8F1";
            var showGlyph = Application.Current?.TryFindResource("IconViewPassword") as string ?? "\uE890";
            PasswordVisibilityIcon.Text = _passwordVisible ? hideGlyph : showGlyph;
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ClearError();
    }

    private void PasswordPlain_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ClearError();
    }

    private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        if (_passwordVisible)
        {
            PasswordPlain.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordPlain.Visibility = Visibility.Visible;
            PasswordPlain.Focus();
            PasswordPlain.CaretIndex = PasswordPlain.Text.Length;
        }
        else
        {
            PasswordBox.Password = PasswordPlain.Text ?? "";
            PasswordPlain.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordBox.Focus();
        }
        UpdatePasswordVisibilityUi();
    }

    private string GetPasswordForLogin()
    {
        if (PasswordPlain.Visibility == Visibility.Visible)
            return PasswordPlain.Text ?? "";
        return PasswordBox.Password;
    }

    private void ClearError()
    {
        if (ErrorText != null)
        {
            ErrorText.Text = "";
            ErrorText.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowError(string message)
    {
        if (ErrorText != null)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text?.Trim();
        var password = GetPasswordForLogin();

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("Please enter both username and password.");
            return;
        }

        ClearError();
        LoginButton.IsEnabled = false;

        try
        {
            var (success, error, _) = await _sessionService.LoginAsync(username, password);

            if (success)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                ShowError(error ?? "Login failed.");
                LoginButton.IsEnabled = true;
            }
        }
        catch (System.Exception ex)
        {
            ShowError($"An error occurred: {ex.Message}");
            LoginButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
