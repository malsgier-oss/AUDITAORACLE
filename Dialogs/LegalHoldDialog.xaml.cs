using System.Windows;

namespace WorkAudit.Dialogs;

public partial class LegalHoldDialog : Window
{
    public string CaseNumber => CaseNumberBox?.Text ?? "";
    public string Reason => ReasonBox?.Text ?? "";

    public LegalHoldDialog()
    {
        InitializeComponent();
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CaseNumberBox.Text))
        {
            MessageBox.Show("Case number is required.", "Legal Hold", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(ReasonBox.Text))
        {
            MessageBox.Show("Reason is required.", "Legal Hold", MessageBoxButton.OK, MessageBoxImage.Warning);
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
