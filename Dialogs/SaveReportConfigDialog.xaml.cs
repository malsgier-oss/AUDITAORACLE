using System.Windows;

namespace WorkAudit.Dialogs;

public partial class SaveReportConfigDialog : Window
{
    public string ConfigName => NameTextBox.Text.Trim();

    public SaveReportConfigDialog(string? initialName = null)
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(initialName))
            NameTextBox.Text = initialName;
        NameTextBox.Focus();
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            MessageBox.Show("Please enter a name.", "Save Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
