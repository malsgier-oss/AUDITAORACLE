using System.Windows;

namespace WorkAudit.Dialogs;

public partial class AboutDialog : Window
{
    public AboutDialog() => InitializeComponent();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
