using System.IO;
using System.Windows;
using System.Windows.Controls;
using Oracle.ManagedDataAccess.Client;
using WorkAudit.Config;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace WorkAudit.Dialogs;

/// <summary>
/// First-run setup wizard for storage settings.
/// Phase 1.2 Configuration Management.
/// </summary>
public partial class SetupWizardDialog : Window
{
    private int _step;
    private TextBox? _baseDirBox;
    private TextBox? _oracleConnectionBox;

    public string BaseDirectory { get; private set; } = "";
    public string OracleConnectionString { get; private set; } = "";

    public SetupWizardDialog()
    {
        InitializeComponent();
        var envBaseDir = Environment.GetEnvironmentVariable("WORKAUDIT_BASE_DIR");
        BaseDirectory = !string.IsNullOrWhiteSpace(envBaseDir)
            ? envBaseDir.Trim()
            : (UserSettings.Get<string>("base_directory") ?? Defaults.GetDefaultBaseDir());

        var envOracleConnection = Environment.GetEnvironmentVariable("WORKAUDIT_ORACLE_CONNECTION")
            ?? Environment.GetEnvironmentVariable("WORKAUDIT_ORACLE_CONN")
            ?? Environment.GetEnvironmentVariable("ORACLE_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("WORKAUDIT_TEST_ORACLE");
        OracleConnectionString = !string.IsNullOrWhiteSpace(envOracleConnection)
            ? envOracleConnection.Trim()
            : (UserSettings.Get<string>("oracle_connection_string") ?? "");
        ShowStep(0);
    }

    private void ShowStep(int step)
    {
        _step = step;
        StepFields.Children.Clear();
        _baseDirBox = null;
        _oracleConnectionBox = null;

        if (step == 0)
        {
            StepTitle.Text = "Welcome to WorkAudit";
            StepDescription.Text = "This setup helps you configure where WorkAudit stores documents and how it connects to Oracle. You can change these values later in Settings.";
            NextBtn.Visibility = Visibility.Visible;
            FinishBtn.Visibility = Visibility.Collapsed;
            BackBtn.Visibility = Visibility.Collapsed;
        }
        else if (step == 1)
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
            NextBtn.Visibility = Visibility.Visible;
            FinishBtn.Visibility = Visibility.Collapsed;
            BackBtn.Visibility = Visibility.Visible;
        }
        else if (step == 2)
        {
            StepTitle.Text = "Oracle connection";
            StepDescription.Text =
                "Enter the Oracle connection string used by AUDITA. " +
                "Example: User Id=workaudit;Password=***;Data Source=//localhost:1521/FREEPDB1";
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _oracleConnectionBox = new TextBox
            {
                Text = OracleConnectionString,
                Padding = new Thickness(8, 6, 8, 6),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_oracleConnectionBox, 0);
            grid.Children.Add(_oracleConnectionBox);
            StepFields.Children.Add(grid);
            NextBtn.Visibility = Visibility.Collapsed;
            FinishBtn.Visibility = Visibility.Visible;
            BackBtn.Visibility = Visibility.Visible;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 0)
            ShowStep(_step - 1);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 1)
        {
            var path = _baseDirBox?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show("Please enter a documents folder path.", "Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            BaseDirectory = path;
        }
        if (_step < 2)
            ShowStep(_step + 1);
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 2)
        {
            var oracleConnection = _oracleConnectionBox?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(oracleConnection))
            {
                MessageBox.Show("Please enter an Oracle connection string.", "Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Fast client-side validation for malformed connection strings.
            try
            {
                _ = new OracleConnectionStringBuilder(oracleConnection);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Oracle connection string format is invalid:\n\n{ex.Message}",
                    "Setup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            OracleConnectionString = oracleConnection;
        }

        UserSettings.Set("base_directory", BaseDirectory);
        UserSettings.Set("oracle_connection_string", OracleConnectionString);
        UserSettings.Set("first_run_completed", true);

        if (!string.IsNullOrEmpty(BaseDirectory))
            Directory.CreateDirectory(BaseDirectory);

        DialogResult = true;
        Close();
    }
}
