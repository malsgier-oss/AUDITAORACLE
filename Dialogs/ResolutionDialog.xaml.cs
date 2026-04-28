using System.Windows;

namespace WorkAudit.Dialogs;

public partial class ResolutionDialog : Window
{
    public string ResolutionComment { get; private set; } = "";

    public ResolutionDialog()
    {
        InitializeComponent();
        CommentTextBox.Focus();
    }

    private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
    {
        var comment = CommentTextBox.Text?.Trim();

        if (string.IsNullOrEmpty(comment))
        {
            MessageBox.Show(
                "Please provide a resolution comment.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        ResolutionComment = comment;
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
