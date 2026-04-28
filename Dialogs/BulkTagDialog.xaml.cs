using System.Linq;
using System.Windows;

namespace WorkAudit.Dialogs;

public partial class BulkTagDialog : Window
{
    public string AddTags { get; private set; } = "";
    public string RemoveTags { get; private set; } = "";
    public bool Applied { get; private set; }

    public BulkTagDialog(int documentCount)
    {
        InitializeComponent();
        InfoText.Text = $"{documentCount} document(s) selected. Add and/or remove tags, then click Apply.";
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        AddTags = AddTagsBox.Text?.Trim() ?? "";
        RemoveTags = RemoveTagsBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(AddTags) && string.IsNullOrEmpty(RemoveTags))
        {
            MessageBox.Show("Enter at least one tag to add or remove.", "Bulk Tag", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Applied = true;
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static string[] ParseTags(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return System.Array.Empty<string>();
        return input.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
