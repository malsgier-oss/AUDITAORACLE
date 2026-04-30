using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Config;
using Orientation = System.Windows.Controls.Orientation;
using Button = System.Windows.Controls.Button;
using PasswordBox = System.Windows.Controls.PasswordBox;
using TextBox = System.Windows.Controls.TextBox;

namespace WorkAudit.Dialogs;

/// <summary>
/// First-run setup wizard for storage and Oracle settings.
/// </summary>
public partial class SetupWizardDialog : Window
{
    private const int DefaultOraclePort = 1521;
    private int _step;
    private readonly bool _promptForConnectionOnly;
    private readonly bool _managedOracleEnvRequired;
    private readonly string _managedOracleConnectionValue;

    private TextBox? _baseDirBox;
    private TextBox? _oracleHostBox;
    private TextBox? _oraclePortBox;
    private TextBox? _oracleServiceBox;
    private TextBox? _oracleUserBox;
    private PasswordBox? _oraclePasswordBox;
    private Button? _testConnectionButton;
    private TextBlock? _validationMessage;
    private Button? _nextButton;
    private Button? _finishButton;
    private Button? _backButton;

    public string BaseDirectory { get; private set; } = "";
    public string OracleConnectionString { get; private set; } = "";

    public SetupWizardDialog(bool promptForConnectionOnly = false)
    {
        try
        {
            InitializeComponent();
            Log.Information("SetupWizardDialog: InitializeComponent completed");

            _promptForConnectionOnly = promptForConnectionOnly;
            _managedOracleEnvRequired = IsManagedOracleEnvRequired();
            _managedOracleConnectionValue = (Environment.GetEnvironmentVariable("WORKAUDIT_ORACLE_CONNECTION")
                                               ?? Environment.GetEnvironmentVariable("WORKAUDIT_ORACLE_CONN")
                                               ?? Environment.GetEnvironmentVariable("ORACLE_CONNECTION_STRING")
                                               ?? Environment.GetEnvironmentVariable("WORKAUDIT_TEST_ORACLE") ?? "")
                .Trim();

            var envBaseDir = Environment.GetEnvironmentVariable("WORKAUDIT_BASE_DIR");
            BaseDirectory = !string.IsNullOrWhiteSpace(envBaseDir)
                ? envBaseDir.Trim()
                : (UserSettings.Get<string>("base_directory") ?? Defaults.GetDefaultBaseDir());

            var envOracleConnection = Environment.GetEnvironmentVariable("WORKAUDIT_ORACLE_CONNECTION")
                                          ?? Environment.GetEnvironmentVariable("WORKAUDIT_ORACLE_CONN")
                                          ?? Environment.GetEnvironmentVariable("ORACLE_CONNECTION_STRING")
                                          ?? Environment.GetEnvironmentVariable("WORKAUDIT_TEST_ORACLE");
            OracleConnectionString = _managedOracleEnvRequired && !string.IsNullOrWhiteSpace(_managedOracleConnectionValue)
                ? _managedOracleConnectionValue
                : !string.IsNullOrWhiteSpace(envOracleConnection)
                    ? envOracleConnection.Trim()
                    : (UserSettings.GetSecure("oracle_connection_string") ?? UserSettings.Get<string>("oracle_connection_string") ?? "");

            _nextButton = NextBtn;
            _finishButton = FinishBtn;
            _backButton = BackBtn;

            Log.Information("SetupWizardDialog: showing initial step (promptForConnectionOnly={Prompt})", _promptForConnectionOnly);
            if (_promptForConnectionOnly)
                ShowConnectionStep();
            else
                ShowWelcomeStep();

            Log.Information("SetupWizardDialog: initialization complete (step={Step})", _step);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SetupWizardDialog: initialization failed");
            throw;
        }
    }

    private static bool IsManagedOracleEnvRequired()
    {
        var flag = Environment.GetEnvironmentVariable("WORKAUDIT_REQUIRE_ORACLE_ENV");
        return flag != null &&
               (flag.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                flag.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                flag.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private void ShowWelcomeStep()
    {
        ShowStep(0);
    }

    private void ShowConnectionStep()
    {
        ShowStep(2);
    }

    private void ShowStep(int step)
    {
        try
        {
            _step = step;
            StepFields.Children.Clear();
            _baseDirBox = null;
            _oracleHostBox = null;
            _oraclePortBox = null;
            _oracleServiceBox = null;
            _oracleUserBox = null;
            _oraclePasswordBox = null;
            _testConnectionButton = null;
            _validationMessage = null;

            if (step == 0)
            {
                StepTitle.Text = "Welcome to WorkAudit";
                StepDescription.Text =
                    "This setup helps you configure where WorkAudit stores documents and how it connects to Oracle. You can change these values later in Settings.";
                _nextButton!.Visibility = Visibility.Visible;
                _finishButton!.Visibility = Visibility.Collapsed;
                _backButton!.Visibility = Visibility.Collapsed;
                StepFields.Children.Add(new TextBlock
                {
                    Text = "Click Next to choose your documents folder and configure Oracle."
                });
                return;
            }

            if (step == 1)
            {
                StepTitle.Text = "Documents folder";
                StepDescription.Text = "Choose where imported documents are stored. This folder is created automatically if it does not exist.";
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _baseDirBox = new TextBox
                {
                    Text = BaseDirectory,
                    Margin = new Thickness(0, 0, 8, 0),
                    Padding = new Thickness(8, 6, 8, 6),
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(_baseDirBox, 0);
                var browseBtn = new Button
                {
                    Content = "Browse...",
                    Padding = new Thickness(12, 6, 12, 6),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                browseBtn.Click += (s, e) =>
                {
                    var dlg = new System.Windows.Forms.FolderBrowserDialog
                    {
                        Description = "Select documents folder",
                        SelectedPath = _baseDirBox?.Text ?? ""
                    };
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
                    {
                        _baseDirBox!.Text = dlg.SelectedPath;
                    }
                };
                Grid.SetColumn(browseBtn, 1);
                grid.Children.Add(_baseDirBox);
                grid.Children.Add(browseBtn);
                StepFields.Children.Add(grid);
                StepFields.Children.Add(new TextBlock
                {
                    Text = "Tip: this folder is local to this machine unless you change it later.",
                    Margin = new Thickness(0, 8, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });

                _nextButton!.Visibility = Visibility.Visible;
                _finishButton!.Visibility = Visibility.Collapsed;
                _backButton!.Visibility = Visibility.Visible;
                return;
            }

            if (step == 2)
            {
                StepTitle.Text = "Oracle connection";
                StepDescription.Text =
                    "Enter host, port, service name, username and password for AUDITA. " +
                    "Example: Host=127.0.0.1, Port=1521, Service Name=FREEPDB1";

                var parsedConnection = ParseConnectionString(OracleConnectionString);

                StepFields.Children.Add(BuildLabeledField("Server / Host", _oracleHostBox = CreateTextBox(parsedConnection.Host, isReadOnly: _managedOracleEnvRequired)));
                StepFields.Children.Add(BuildLabeledField("Port", _oraclePortBox = CreateTextBox(parsedConnection.Port, isReadOnly: _managedOracleEnvRequired)));
                StepFields.Children.Add(BuildLabeledField("Service Name", _oracleServiceBox = CreateTextBox(parsedConnection.Service, isReadOnly: _managedOracleEnvRequired)));
                StepFields.Children.Add(BuildLabeledField("User Name", _oracleUserBox = CreateTextBox(parsedConnection.User, isReadOnly: _managedOracleEnvRequired)));
                StepFields.Children.Add(BuildLabeledField("Password", _oraclePasswordBox = CreatePasswordBox(parsedConnection.Password, isReadOnly: _managedOracleEnvRequired)));

                _testConnectionButton = new Button
                {
                    Content = "Test Connection",
                    Width = 140,
                    Height = 32,
                    Margin = new Thickness(0, 8, 0, 10),
                    IsEnabled = !_managedOracleEnvRequired,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                _testConnectionButton.Click += TestConnection_Click;
                StepFields.Children.Add(_testConnectionButton);

                _validationMessage = new TextBlock
                {
                    Text = string.Empty,
                    TextWrapping = TextWrapping.Wrap
                };
                StepFields.Children.Add(_validationMessage);

                if (_managedOracleEnvRequired)
                {
                    StepFields.Children.Add(new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(_managedOracleConnectionValue)
                            ? "Managed deployment requires WORKAUDIT_ORACLE_CONNECTION at machine scope. Set it before finishing setup."
                            : "Managed deployment detected. Oracle connection is read-only and sourced from machine environment variables.",
                        Margin = new Thickness(0, 8, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                _nextButton!.Visibility = Visibility.Collapsed;
                _finishButton!.Visibility = Visibility.Visible;
                _backButton!.Visibility = Visibility.Visible;

                if (_promptForConnectionOnly)
                {
                    _backButton.Visibility = Visibility.Collapsed;
                }
            }

            Log.Information("ShowStep succeeded for step {Step}", step);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ShowStep failed for step {Step}", step);
            MessageBox.Show($"Setup wizard step load failed: {ex.Message}", "Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_promptForConnectionOnly) return;
        if (_step == 2) ShowStep(1);
        else if (_step == 1) ShowStep(0);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 1)
        {
            var path = _baseDirBox?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("Please enter a documents folder path.", "Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BaseDirectory = path;
            _step = 2;
            ShowStep(2);
            return;
        }
        else if (_step == 0)
        {
            _step = 1;
            ShowStep(1);
            return;
        }
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildOracleConnectionString(out var oracleConnection, out var connectionError))
        {
            ShowValidationError(connectionError);
            return;
        }

        OracleConnectionString = oracleConnection;

        if (!_promptForConnectionOnly)
        {
            UserSettings.Set("base_directory", BaseDirectory);
            if (!_managedOracleEnvRequired)
                UserSettings.SetSecure("oracle_connection_string", OracleConnectionString);
            UserSettings.Set("first_run_completed", true);
        }

        if (!string.IsNullOrEmpty(BaseDirectory))
            Directory.CreateDirectory(BaseDirectory);

        DialogResult = true;
        Close();
    }

    private void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildOracleConnectionString(out var oracleConnection, out var error))
        {
            ShowValidationError(error);
            return;
        }

        try
        {
            using var conn = new OracleConnection(oracleConnection);
            conn.Open();
            ShowValidationMessage("Connection test passed.");
        }
        catch (Exception ex)
        {
            ShowValidationError($"Connection test failed: {ex.Message}");
        }
    }

    private (string Host, string Port, string Service, string User, string Password) ParseConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return ("", DefaultOraclePort.ToString(CultureInfo.InvariantCulture), "", "", "");

        try
        {
            var builder = new OracleConnectionStringBuilder(connectionString);
            var dataSource = builder.DataSource ?? "";
            var parsedDataSource = ParseDataSource(dataSource);
            return (
                parsedDataSource.host,
                parsedDataSource.port,
                parsedDataSource.service,
                builder.UserID ?? string.Empty,
                string.Empty);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse stored oracle connection string");
            return ("", DefaultOraclePort.ToString(CultureInfo.InvariantCulture), "", "", "");
        }
    }

    private static (string host, string port, string service) ParseDataSource(string dataSource)
    {
        var normalized = (dataSource ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return ("", DefaultOraclePort.ToString(CultureInfo.InvariantCulture), "");

        if (normalized.StartsWith("DESCRIPTION=", StringComparison.OrdinalIgnoreCase))
            return ("", DefaultOraclePort.ToString(CultureInfo.InvariantCulture), "");

        if (normalized.StartsWith("//", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..];
        if (normalized.StartsWith("/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[1..];

        var serviceIndex = normalized.IndexOf('/');
        var hostAndPort = serviceIndex >= 0
            ? normalized[..serviceIndex]
            : normalized;
        var service = serviceIndex >= 0 && serviceIndex + 1 < normalized.Length
            ? normalized[(serviceIndex + 1)..]
            : "";

        var port = DefaultOraclePort.ToString(CultureInfo.InvariantCulture);
        var host = hostAndPort;
        var colonIndex = hostAndPort.LastIndexOf(':');
        if (colonIndex > 0)
        {
            host = hostAndPort[..colonIndex];
            if (int.TryParse(hostAndPort[(colonIndex + 1)..], out var parsedPort))
                port = parsedPort.ToString(CultureInfo.InvariantCulture);
        }

        return (host, port, service);
    }

    private bool TryBuildOracleConnectionString(out string connectionString, out string validationError)
    {
        connectionString = string.Empty;
        validationError = string.Empty;

        if (_managedOracleEnvRequired && string.IsNullOrWhiteSpace(_managedOracleConnectionValue))
        {
            validationError = "Managed deployment requires WORKAUDIT_ORACLE_CONNECTION at machine scope.";
            return false;
        }

        if (_managedOracleEnvRequired && !string.IsNullOrWhiteSpace(_managedOracleConnectionValue))
        {
            connectionString = _managedOracleConnectionValue;
            return true;
        }

        var host = (_oracleHostBox?.Text ?? "").Trim();
        var portText = (_oraclePortBox?.Text ?? "").Trim();
        var service = (_oracleServiceBox?.Text ?? "").Trim();
        var user = (_oracleUserBox?.Text ?? "").Trim();
        var password = _oraclePasswordBox?.Password ?? "";

        if (string.IsNullOrWhiteSpace(host))
        {
            validationError = "Server/Host is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(portText) ||
            !int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
            port <= 0 || port > 65535)
        {
            validationError = "Enter a valid port number (1-65535).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(service))
        {
            validationError = "Service Name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(user))
        {
            validationError = "User Name is required.";
            return false;
        }

        var candidate = ComposeConnectionString(host, port, service, user, password);
        try
        {
            _ = new OracleConnectionStringBuilder(candidate);
            connectionString = candidate;
            return true;
        }
        catch (Exception ex)
        {
            validationError = ex.Message;
            return false;
        }
    }

    private static string ComposeConnectionString(string host, int port, string service, string user, string password)
    {
        return $"User Id={user};Password={password};Data Source=//{host}:{port}/{service}";
    }

    private UIElement BuildLabeledField(string label, TextBox textBox)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(textBox);
        return panel;
    }

    private UIElement BuildLabeledField(string label, PasswordBox passwordBox)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(passwordBox);
        return panel;
    }

    private TextBox CreateTextBox(string text, bool isReadOnly = false)
    {
        return new TextBox
        {
            Text = text,
            Margin = new Thickness(0, 0, 0, 0),
            Padding = new Thickness(8, 6, 8, 6),
            VerticalContentAlignment = VerticalAlignment.Center,
            IsReadOnly = isReadOnly,
            IsEnabled = !isReadOnly
        };
    }

    private PasswordBox CreatePasswordBox(string text, bool isReadOnly = false)
    {
        var passwordBox = new PasswordBox
        {
            Password = text,
            Margin = new Thickness(0, 0, 0, 0),
            Padding = new Thickness(8, 6, 8, 6),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (isReadOnly)
        {
            passwordBox.IsEnabled = false;
            passwordBox.ToolTip = "Managed deployment: controlled by environment variables.";
        }
        return passwordBox;
    }

    private void ShowValidationError(string message)
    {
        ShowValidationMessage(message, isError: true);
    }

    private void ShowValidationMessage(string message, bool isError = false)
    {
        if (_validationMessage == null) return;
        _validationMessage.Text = message;
        _validationMessage.Foreground = isError
            ? System.Windows.Media.Brushes.Crimson
            : System.Windows.Media.Brushes.SeaGreen;
        _validationMessage.Margin = new Thickness(0, 2, 0, 0);
    }
}
