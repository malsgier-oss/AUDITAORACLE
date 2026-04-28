using System.Windows;

namespace WorkAudit.Dialogs;

public partial class TextResultDialog : Window
{
    public TextResultDialog(string title, string text, string actionButtonText = "Accept")
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        ResultTextBox.Text = text;
        ActionBtn.Content = actionButtonText;
    }

    public string ResultText => ResultTextBox.Text;

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
