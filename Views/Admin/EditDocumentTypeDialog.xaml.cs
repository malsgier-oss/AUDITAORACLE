using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views.Admin;

public partial class EditDocumentTypeDialog : Window
{
    private readonly ConfigDocumentType? _existing;
    public ConfigDocumentType? Result { get; private set; }

    public EditDocumentTypeDialog(ConfigDocumentType? existing)
    {
        InitializeComponent();
        _existing = existing;

        SectionCombo.Items.Clear();
        SectionCombo.Items.Add("");
        foreach (var s in Enums.SectionValues)
            SectionCombo.Items.Add(s);

        if (existing != null)
        {
            Title = "Edit Document Type";
            NameBox.Text = existing.Name;
            if (!string.IsNullOrEmpty(existing.Section))
            {
                var sectionMatch = SectionCombo.Items.Cast<object>().FirstOrDefault(x => x?.ToString() == existing.Section);
                if (sectionMatch != null) SectionCombo.SelectedItem = sectionMatch;
                else SectionCombo.Text = existing.Section;
            }
            else
                SectionCombo.SelectedIndex = 0;
            KeywordsBox.Text = existing.Keywords ?? "";
            OrderBox.Text = existing.DisplayOrder.ToString();
            ActiveCheck.IsChecked = existing.IsActive;
        }
        else
        {
            Title = "Add Document Type";
            SectionCombo.SelectedIndex = 0;
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

        var section = string.IsNullOrWhiteSpace(SectionCombo.Text) ? null : SectionCombo.Text.Trim();

        // Document types are applied to all branches (Branch = null)
        Result = new ConfigDocumentType
        {
            Id = _existing?.Id ?? 0,
            Name = NameBox.Text.Trim(),
            Category = "",
            Keywords = string.IsNullOrWhiteSpace(KeywordsBox.Text) ? null : KeywordsBox.Text.Trim(),
            DisplayOrder = int.TryParse(OrderBox.Text, out var order) ? order : 0,
            IsActive = ActiveCheck.IsChecked == true,
            CreatedAt = _existing?.CreatedAt ?? DateTime.UtcNow.ToString("O"),
            Branch = null,
            Section = section
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
