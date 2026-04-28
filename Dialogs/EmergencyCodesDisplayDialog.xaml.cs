using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace WorkAudit.Dialogs;

public partial class EmergencyCodesDisplayDialog : Window
{
    public EmergencyCodesDisplayDialog(IReadOnlyList<string> plainCodes, string? username = null)
    {
        InitializeComponent();

        InstructionText.Text =
            "Save these one-time emergency codes in a secure place (password manager or print). " +
            "Each code can be used once in place of your password at sign-in. " +
            "After using a code, you must set a new password. " +
            (string.IsNullOrEmpty(username)
                ? "You can generate a new set from User Management while signed in as an administrator."
                : $"You can generate a new set for user \"{username}\" from User Management while signed in as an administrator.");

        CodesTextBox.Text = string.Join(System.Environment.NewLine, plainCodes.Select((c, i) => $"{i + 1,2}. {c}"));
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(CodesTextBox.Text);
            MessageBox.Show(this, "Copied to clipboard.", "Emergency codes", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this, $"Could not copy: {ex.Message}", "Emergency codes", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
