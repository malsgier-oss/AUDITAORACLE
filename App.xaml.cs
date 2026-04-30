using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Config;
using WorkAudit.Core;
using WorkAudit.Core.Backup;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Services;
using WorkAudit.Core.Security;
using WorkAudit.Storage;
using WorkAudit.Domain;

namespace WorkAudit;

/// <summary>
/// Application entry point with enhanced initialization.
/// Implements proper startup sequence, error handling, and logging.
/// </summary>
public partial class App : Application
{
    private void App_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        LoggingService.Initialize();
        Log.Information("Application starting...");

        SetupExceptionHandlers();

        ThemeService.ApplySavedTheme();

        try
        {
            if (e.Args.Any(arg => arg.Equals("--reset-setup", StringComparison.OrdinalIgnoreCase)))
            {
                Log.Information("Resetting first-run setup state from startup argument");
                UserSettings.Set("first_run_completed", false);
            }

            var settings = UserSettings.Load();
            settings.TryGetValue("first_run_completed", out var firstRunCompletedValue);
            var shouldShowSetupWizard = !IsSettingTruthy(firstRunCompletedValue);

            Log.Information(
                "First-run setup check: first_run_completed raw value={Value} ({Type}), showWizard={ShowWizard}",
                firstRunCompletedValue ?? "(null)",
                firstRunCompletedValue?.GetType().Name ?? "(null)",
                shouldShowSetupWizard);

            if (shouldShowSetupWizard)
            {
                try
                {
                    Log.Information("Creating setup wizard dialog");
                    var wizard = new Dialogs.SetupWizardDialog();

                    Log.Information("Showing setup wizard dialog");
                    var result = wizard.ShowDialog();
                    Log.Information("Setup wizard result: {Result}", result);

                    if (result != true)
                    {
                        Log.Information("User cancelled setup wizard");
                        Shutdown(0);
                        return;
                    }

                    Log.Information("Setup wizard completed: base dir={BaseDir}", wizard.BaseDirectory);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to show setup wizard");
                    MessageBox.Show(
                        $"Setup wizard initialization failed: {ex.Message}",
                        "AUDITA - Startup Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Shutdown(0);
                    return;
                }
            }
            else
            {
                Log.Information("Skipping first-run setup wizard (first_run_completed already true)");
            }

            var startup = new StartupCoordinator();
            var boot = startup.Initialize(
                promptForConnectionString: PromptForOracleConnectionString,
                resolveOracleConnectionString: ResolveOracleConnectionString,
                ensureArchiveSchema: EnsureArchiveSchema);
            if (!boot.Success)
            {
                Log.Fatal("Startup bootstrap failed ({ErrorCode}): {ErrorMessage}", boot.ErrorCode, boot.ErrorMessage);
                var guidance = boot.ErrorCode switch
                {
                    "BOOT_ORACLE_MISSING" => "Configure WORKAUDIT_ORACLE_CONNECTION at machine scope or complete setup with a valid Oracle ODP.NET connection string.",
                    "BOOT_ORACLE_MALFORMED" => "Use Oracle ODP.NET format with User Id, Password, and Data Source (no placeholders).",
                    "BOOT_ORACLE_UNREACHABLE" => "Check Oracle listener/service reachability, credentials, and network/TNS configuration.",
                    "BOOT_ORACLE_ENV_REQUIRED" => "This deployment requires machine-level Oracle environment variables. Contact IT to configure WORKAUDIT_ORACLE_CONNECTION.",
                    _ => "Review startup logs for details."
                };
                MessageBox.Show(
                    "AUDITA could not initialize startup prerequisites.\n\n" +
                    $"Error: {boot.ErrorCode}\n" +
                    $"{boot.ErrorMessage}\n\n" +
                    $"Guidance: {guidance}",
                    "AUDITA - Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            EnsureDefaultAdminUser();

            if (!ShowLoginAndContinue())
                Shutdown(0);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed");
            var stack = ex.StackTrace ?? "";
            var firstLines = string.Join(Environment.NewLine, stack.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Take(8));
            var logPath = LoggingService.LogDirectory;
            var msg = $"Failed to start AUDITA:\n\n{ex.Message}\n\nStack trace:\n{firstLines}\n\nLogs: {logPath}";
            MessageBox.Show(msg, "AUDITA - Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void SetupExceptionHandlers()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            if (IsBenignShutdownComException(e.Exception))
            {
                Log.Warning(e.Exception, "Suppressed benign WPF/TSF COM exception during shutdown");
                e.Handled = true;
                return;
            }

            if (IsBenignPdfPreviewException(e.Exception))
            {
                Log.Warning(e.Exception, "Benign PdfiumViewer race (disposed PdfPage); error dialog suppressed");
                e.Handled = true;
                return;
            }

            Log.Error(e.Exception, "Unhandled UI exception");
            ShowErrorDialog(e.Exception, "An unexpected error occurred in the user interface.");
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;

            if (IsBenignShutdownComException(ex))
            {
                Log.Warning(ex, "Suppressed benign domain exception during shutdown");
                return;
            }

            Log.Fatal(ex, "Unhandled domain exception (IsTerminating: {IsTerminating})", e.IsTerminating);

            if (e.IsTerminating)
            {
                MessageBox.Show(
                    $"A fatal error occurred:\n\n{ex?.Message}\n\nThe application will now close.",
                    "AUDITA - Fatal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    private static bool IsSettingTruthy(object? value)
    {
        if (value == null)
            return false;

        if (value is bool boolValue)
            return boolValue;

        if (value is Newtonsoft.Json.Linq.JToken token && token.Type == Newtonsoft.Json.Linq.JTokenType.Boolean)
            return token.ToObject<bool?>() == true;

        if (value is string text && bool.TryParse(text, out var parsedBoolean))
            return parsedBoolean;

        if (value is long int64)
            return int64 != 0L;

        if (value is int int32)
            return int32 != 0;

        if (value is Newtonsoft.Json.Linq.JValue tokenValue && tokenValue.Type == Newtonsoft.Json.Linq.JTokenType.Boolean)
            return tokenValue.ToObject<bool>();

        if (value is Newtonsoft.Json.Linq.JValue tokenNumber && tokenNumber.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
            return tokenNumber.ToObject<long>() != 0L;

        return false;
    }

    /// <summary>
    /// PdfiumViewer can throw <see cref="ObjectDisposedException"/> for internal pages during rapid
    /// <c>Source</c> changes; treat as non-fatal and avoid alarming the user with a dialog.
    /// </summary>
    private static bool IsBenignPdfPreviewException(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is ObjectDisposedException ode &&
                ode.ObjectName != null &&
                ode.ObjectName.Contains("PdfPage", StringComparison.OrdinalIgnoreCase))
                return true;
            // PdfiumViewer can throw NRE internally when Source/pages change during rapid navigation.
            if (e is NullReferenceException nre && !string.IsNullOrEmpty(nre.StackTrace))
            {
                var st = nre.StackTrace;
                if (st.Contains("PdfiumViewer", StringComparison.OrdinalIgnoreCase) ||
                    st.Contains("PdfViewer", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// WPF can rarely throw an InvalidCastException for ITfThreadMgr while tearing down TSF/IME state
    /// at process shutdown. This is non-actionable for users and should not trigger a fatal popup.
    /// </summary>
    private static bool IsBenignShutdownComException(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is InvalidCastException ice &&
                ice.Message.Contains("System.__ComObject", StringComparison.OrdinalIgnoreCase) &&
                ice.Message.Contains("ITfThreadMgr", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ShowErrorDialog(Exception? ex, string message)
    {
        try
        {
            var errorDetail = ex?.Message ?? "Unknown";
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(
                    $"{message}\n\nError: {errorDetail}\n\nThe application will attempt to continue.",
                    "AUDITA - Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }
        catch (Exception inner)
        {
            Log.Warning(inner, "ShowErrorDialog failed");
        }
    }

    /// <summary>Verifies archive-related columns exist (Oracle baseline includes them).</summary>
    private static void EnsureArchiveSchema(string oracleConnectionString)
    {
        using var conn = new OracleConnection(oracleConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM user_tab_columns WHERE table_name = 'DOCUMENTS' AND column_name = 'ARCHIVED_AT'";
        var n = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (n > 0)
            return;

        Log.Warning("Oracle documents table missing archived_at column; apply migrations or recreate schema.");
        throw new InvalidOperationException(
            "Oracle schema is missing expected archive columns. Ensure migrations completed successfully.");
    }

    /// <summary>
    /// Prefer configured Oracle string, but for local Oracle Free handoff issues on Windows
    /// retry with TNS alias FREE when the original uses localhost/FREEPDB1.
    /// </summary>
    private static string ResolveOracleConnectionString(string configuredConnectionString)
    {
        if (CanOpenOracleConnection(configuredConnectionString))
            return configuredConnectionString;

        if (!TryBuildLocalFreeAliasFallback(configuredConnectionString, out var fallbackConnectionString))
            return configuredConnectionString;

        if (!CanOpenOracleConnection(fallbackConnectionString))
            return configuredConnectionString;

        Log.Warning(
            "Primary Oracle connection failed; using FREE alias fallback Data Source for this session.");
        return fallbackConnectionString;
    }

    private static bool CanOpenOracleConnection(string connectionString)
    {
        try
        {
            using var conn = new OracleConnection(connectionString);
            conn.Open();
            return true;
        }
        catch (OracleException ex)
        {
            Log.Warning(ex, "Oracle connectivity test failed for configured Data Source");
            return false;
        }
    }

    private static string PromptForOracleConnectionString()
    {
        try
        {
            var dialog = new Dialogs.SetupWizardDialog(promptForConnectionOnly: true);
            return dialog.ShowDialog() == true
                ? dialog.OracleConnectionString
                : string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to collect Oracle connection string from user");
            return string.Empty;
        }
    }

    private static bool TryBuildLocalFreeAliasFallback(string sourceConnectionString, out string fallbackConnectionString)
    {
        fallbackConnectionString = sourceConnectionString;
        try
        {
            var builder = new OracleConnectionStringBuilder(sourceConnectionString);
            var dataSource = (builder.DataSource ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(dataSource))
                return false;

            var normalized = dataSource.Replace(" ", string.Empty).ToUpperInvariant();
            var looksLikeLocalFreePdb =
                normalized.Contains("LOCALHOST") &&
                normalized.Contains("1521") &&
                normalized.Contains("FREEPDB1");
            if (!looksLikeLocalFreePdb)
                return false;

            builder.DataSource = "FREE";
            fallbackConnectionString = builder.ConnectionString;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void EnsureDefaultAdminUser()
    {
        try
        {
            var userStore = ServiceContainer.GetService<IUserStore>();
            var passwordService = ServiceContainer.GetService<IPasswordService>();

            if (userStore.Count() == 0)
            {
                Log.Information("No users found, creating default admin user");

                // Generate a secure random password for the default admin
                var randomPassword = GenerateSecureRandomPassword();

                var defaultUsername = Environment.GetEnvironmentVariable("WORKAUDIT_ADMIN_USERNAME") ?? "admin";
                var defaultEmail = Environment.GetEnvironmentVariable("WORKAUDIT_ADMIN_EMAIL") ?? "admin@workaudit.local";
                var defaultBranch = Environment.GetEnvironmentVariable("WORKAUDIT_ADMIN_BRANCH") ?? Branches.Default;

                var adminUser = new User
                {
                    Username = defaultUsername,
                    DisplayName = "Administrator",
                    Email = defaultEmail,
                    PasswordHash = passwordService.HashPassword(randomPassword),
                    Role = Roles.Administrator,
                    Branch = defaultBranch,
                    IsActive = true,
                    MustChangePassword = true,
                    CreatedBy = "system"
                };

                userStore.Insert(adminUser);
                Log.Information("Default admin user created (username: {Username}, branch: {Branch}). Password must be changed on first login.", defaultUsername, defaultBranch);
                Log.Warning("IMPORTANT: Default admin password is: {Password} - Save this and change it immediately after first login!", randomPassword);

                System.Windows.MessageBox.Show(
                    $"Default administrator account created.\n\n" +
                    $"Username: {defaultUsername}\n" +
                    $"Temporary Password: {randomPassword}\n\n" +
                    $"IMPORTANT: You MUST change this password on first login.\n" +
                    $"Please save this password now!",
                    "AUDITA - Initial Setup",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);

                try
                {
                    var emergencyPlain = passwordService.GenerateEmergencyAccessCodes(10);
                    var emergencyHashes = emergencyPlain.Select(p => passwordService.HashPassword(p)).ToList();
                    userStore.ReplaceEmergencyCodes(adminUser.Id, emergencyHashes);
                    var codesDlg = new Dialogs.EmergencyCodesDisplayDialog(emergencyPlain, adminUser.Username);
                    codesDlg.Owner = Current?.MainWindow;
                    codesDlg.ShowDialog();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not create or display emergency access codes for default admin");
                }

                var auditService = ServiceContainer.GetService<IAuditTrailService>();
                _ = auditService.LogAsync(AuditAction.UserCreated, AuditCategory.User, "User", adminUser.Uuid,
                    details: "Default admin user created during first startup")
                    .ContinueWith(t => { if (t.IsFaulted && t.Exception != null) Log.Warning(t.Exception, "Audit log failed for default admin creation"); }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not ensure default admin user");
        }
    }

    private bool ShowLoginAndContinue()
    {
        while (true)
        {
            var loginDialog = new Dialogs.LoginDialog();
            var result = loginDialog.ShowDialog();

            if (result != true)
            {
                Log.Information("User cancelled login");
                return false;
            }

            var sessionService = ServiceContainer.GetService<ISessionService>();
            var currentUser = sessionService.CurrentUser;
            if (currentUser == null)
            {
                Log.Warning("Login succeeded but CurrentUser is null");
                continue;
            }

            ServiceContainer.SetCurrentUser(currentUser);
            Log.Information("User logged in: {Username} ({Role})", currentUser.Username, currentUser.Role);

            // Check if user must change password
            if (currentUser.MustChangePassword)
            {
                Log.Information("User must change password: {Username}", currentUser.Username);
                var passwordResetDialog = new Dialogs.PasswordResetDialog(currentUser, isMandatoryChangeAfterLogin: true);
                var passwordChangeResult = passwordResetDialog.ShowDialog();

                if (passwordChangeResult != true || string.IsNullOrEmpty(passwordResetDialog.NewPassword))
                {
                    Log.Warning("User cancelled mandatory password change, logging out");
                    _ = sessionService.LogoutAsync();
                    System.Windows.MessageBox.Show(
                        "You must change your password to continue.",
                        "Password Change Required",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    continue;
                }

                // Update password and clear MustChangePassword flag
                var userStore = ServiceContainer.GetService<IUserStore>();
                var passwordService = ServiceContainer.GetService<IPasswordService>();
                var newPasswordHash = passwordService.HashPassword(passwordResetDialog.NewPassword);
                userStore.UpdatePassword(currentUser.Id, newPasswordHash, currentUser.Username, requirePasswordChangeOnNextLogin: false);

                // Clear the MustChangePassword flag (also persisted by UpdatePassword)
                currentUser.MustChangePassword = false;
                currentUser.PasswordChangedAt = DateTime.UtcNow.ToString("O");
                userStore.Update(currentUser);

                var auditService = ServiceContainer.GetService<IAuditTrailService>();
                _ = auditService.LogAsync(Domain.AuditAction.PasswordReset, Domain.AuditCategory.User, "User", currentUser.Uuid,
                    details: "Password changed after mandatory password change requirement");

                Log.Information("Password changed successfully for user: {Username}", currentUser.Username);

                // Reload user to get updated data
                currentUser = userStore.Get(currentUser.Id);
                if (currentUser != null)
                {
                    ServiceContainer.SetCurrentUser(currentUser);
                }
            }

            var mainWindow = new MainWindow();
            mainWindow.Closed += OnMainWindowClosed;
            mainWindow.Show();
            Log.Information("Main window displayed");

            return true;
        }
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window w)
            w.Closed -= OnMainWindowClosed;

        if (WorkAudit.MainWindow.LogoutRequested)
        {
            WorkAudit.MainWindow.LogoutRequested = false;
            Log.Information("User logged out - showing login");
            if (ShowLoginAndContinue())
                return;
        }

        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down (exit code: {ExitCode})", e.ApplicationExitCode);

        try
        {
            var scheduledBackup = ServiceContainer.GetOptionalService<IScheduledBackupService>();
            scheduledBackup?.Stop();
            var scheduledReport = ServiceContainer.GetOptionalService<IScheduledReportService>();
            scheduledReport?.Stop();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error stopping scheduled services during shutdown");
        }

        try
        {
            var auditService = ServiceContainer.GetOptionalService<IAuditTrailService>();
            _ = auditService?.LogSystemActionAsync(AuditAction.ApplicationShutdown, "Normal shutdown");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error logging application shutdown");
        }

        ServiceContainer.Dispose();
        LoggingService.Shutdown();

        base.OnExit(e);
    }

    private static string GenerateSecureRandomPassword()
    {
        const string uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lowercase = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string special = "!@#$%^&*";
        const string allChars = uppercase + lowercase + digits + special;

        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var password = new char[16];
        var buffer = new byte[16];
        rng.GetBytes(buffer);

        // Ensure at least one of each character type
        password[0] = uppercase[buffer[0] % uppercase.Length];
        password[1] = lowercase[buffer[1] % lowercase.Length];
        password[2] = digits[buffer[2] % digits.Length];
        password[3] = special[buffer[3] % special.Length];

        // Fill remaining with random characters
        for (int i = 4; i < password.Length; i++)
        {
            password[i] = allChars[buffer[i] % allChars.Length];
        }

        // Shuffle the password
        for (int i = password.Length - 1; i > 0; i--)
        {
            rng.GetBytes(buffer);
            int j = buffer[0] % (i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string(password);
    }
}
