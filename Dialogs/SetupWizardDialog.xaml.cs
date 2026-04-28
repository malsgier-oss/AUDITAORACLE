using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    private TextBox? _dbPathBox;

    public string BaseDirectory { get; private set; } = "";
    public string DatabasePath { get; private set; } = "";

    public SetupWizardDialog()
    {
        InitializeComponent();
        var envBaseDir = Environment.GetEnvironmentVariable("WORKAUDIT_BASE_DIR");
        BaseDirectory = !string.IsNullOrWhiteSpace(envBaseDir)
            ? envBaseDir.Trim()
            : (UserSettings.Get<string>("base_directory") ?? Defaults.GetDefaultBaseDir());

        var envDbPath = Environment.GetEnvironmentVariable("WORKAUDIT_DATABASE_PATH");
        DatabasePath = !string.IsNullOrWhiteSpace(envDbPath)
            ? envDbPath.Trim()
            : (UserSettings.Get<string>("database_path") ?? Path.Combine(BaseDirectory, "workaudit.db"));
        ShowStep(0);
    }

    private void ShowStep(int step)
    {
        _step = step;
        StepFields.Children.Clear();
        _baseDirBox = null;
        _dbPathBox = null;

        if (step == 0)
        {
            StepTitle.Text = "Welcome to WorkAudit";
            StepDescription.Text = "This setup helps you configure where WorkAudit stores documents and database data. You can change these locations later in Settings.";
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
            StepTitle.Text = "Database file";
            StepDescription.Text = "Choose where to store the WorkAudit SQLite database file (.db).";
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _dbPathBox = new TextBox
            {
                Text = DatabasePath,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(8, 6, 8, 6),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_dbPathBox, 0);
            var browseBtn = new Button
            {
                Content = "Browse...",
                Padding = new Thickness(12, 6, 12, 6),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            browseBtn.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Select database file location",
                    FileName = Path.GetFileName(_dbPathBox?.Text) ?? "workaudit.db",
                    DefaultExt = ".db",
                    Filter = "SQLite Database (*.db)|*.db|All files (*.*)|*.*",
                    AddExtension = true,
                    OverwritePrompt = false,
                    InitialDirectory = ResolveInitialDirectory(_dbPathBox?.Text, BaseDirectory)
                };
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FileName))
                    _dbPathBox!.Text = dlg.FileName;
            };
            Grid.SetColumn(browseBtn, 1);
            grid.Children.Add(_dbPathBox);
            grid.Children.Add(browseBtn);
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
            if (string.IsNullOrWhiteSpace(DatabasePath) || string.Equals(Path.GetFileName(DatabasePath), "workaudit.db", StringComparison.OrdinalIgnoreCase))
                DatabasePath = Path.Combine(BaseDirectory, "workaudit.db");
        }
        if (_step < 2)
            ShowStep(_step + 1);
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 2)
        {
            var dbPath = _dbPathBox?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(dbPath))
            {
                MessageBox.Show("Please enter a database file path.", "Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DatabasePath = dbPath;
        }

        UserSettings.Set("base_directory", BaseDirectory);
        UserSettings.Set("database_path", DatabasePath);
        UserSettings.Set("first_run_completed", true);

        if (!string.IsNullOrEmpty(BaseDirectory))
            Directory.CreateDirectory(BaseDirectory);
        if (!string.IsNullOrEmpty(DatabasePath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(DatabasePath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        DialogResult = true;
        Close();
    }

    private static string ResolveInitialDirectory(string? selectedFile, string baseDir)
    {
        if (!string.IsNullOrWhiteSpace(selectedFile))
        {
            try
            {
                var full = Path.GetFullPath(selectedFile);
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    return dir;
            }
            catch
            {
                // fallback below
            }
        }
        if (Directory.Exists(baseDir))
            return baseDir;
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
