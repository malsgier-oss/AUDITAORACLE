using System.Globalization;
using System.Windows;
using WorkAudit.Domain;

namespace WorkAudit.Views.Admin;

public partial class EditBranchDialog : Window
{
    private readonly ConfigBranch? _existing;
    public ConfigBranch? Result { get; private set; }

    public EditBranchDialog(ConfigBranch? existing)
    {
        InitializeComponent();
        _existing = existing;

        if (existing != null)
        {
            Title = "Edit Branch";
            NameBox.Text = existing.Name;
            CodeBox.Text = existing.Code ?? "";
            OrderBox.Text = existing.DisplayOrder.ToString(CultureInfo.InvariantCulture);
            ActiveCheck.IsChecked = existing.IsActive;
        }
        else
        {
            Title = "Add Branch";
            OrderBox.Text = "0";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new ConfigBranch
        {
            Id = _existing?.Id ?? 0,
            Name = NameBox.Text.Trim(),
            Code = string.IsNullOrWhiteSpace(CodeBox.Text) ? null : CodeBox.Text.Trim().ToUpperInvariant(),
            DisplayOrder = int.TryParse(OrderBox.Text, out var order) ? order : 0,
            IsActive = ActiveCheck.IsChecked == true,
            CreatedAt = _existing?.CreatedAt ?? DateTime.UtcNow.ToString("O")
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
