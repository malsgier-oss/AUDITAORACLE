using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using WorkAudit.Domain;

namespace WorkAudit.Views.Admin;

public partial class EditDocumentTypeDialog : Window
{
    private const string AllBranchesLabel = "All branches";

    private readonly ConfigDocumentType? _existing;
    public ConfigDocumentType? Result { get; private set; }

    public EditDocumentTypeDialog(ConfigDocumentType? existing, IReadOnlyList<ConfigBranch> branches)
    {
        InitializeComponent();
        _existing = existing;

        SectionCombo.Items.Clear();
        SectionCombo.Items.Add("");
        foreach (var s in Enums.SectionValues)
            SectionCombo.Items.Add(s);

        PopulateBranchCombo(branches);

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
            OrderBox.Text = existing.DisplayOrder.ToString(CultureInfo.InvariantCulture);
            ActiveCheck.IsChecked = existing.IsActive;
        }
        else
        {
            Title = "Add Document Type";
            SectionCombo.SelectedIndex = 0;
            OrderBox.Text = "0";
            BranchCombo.SelectedIndex = 0;
        }
    }

    private void PopulateBranchCombo(IReadOnlyList<ConfigBranch> branches)
    {
        BranchCombo.Items.Clear();
        BranchCombo.Items.Add(AllBranchesLabel);
        var namesAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AllBranchesLabel };
        foreach (var b in branches)
        {
            if (string.IsNullOrWhiteSpace(b.Name)) continue;
            var n = b.Name.Trim();
            if (namesAdded.Add(n))
                BranchCombo.Items.Add(n);
        }

        var existingBranch = _existing?.Branch?.Trim();
        if (!string.IsNullOrEmpty(existingBranch))
        {
            var present = BranchCombo.Items.Cast<object>()
                .Any(x => string.Equals(x?.ToString()?.Trim(), existingBranch, StringComparison.OrdinalIgnoreCase));
            if (!present)
                BranchCombo.Items.Add(existingBranch);
        }

        if (string.IsNullOrEmpty(existingBranch))
        {
            BranchCombo.SelectedIndex = 0;
            return;
        }

        var match = BranchCombo.Items.Cast<object>().FirstOrDefault(x =>
            string.Equals(x?.ToString()?.Trim(), existingBranch, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            BranchCombo.SelectedItem = match;
        else
            BranchCombo.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var section = string.IsNullOrWhiteSpace(SectionCombo.Text) ? null : SectionCombo.Text.Trim();

        var branchText = (BranchCombo.SelectedItem ?? BranchCombo.Text)?.ToString()?.Trim();
        var branch = string.IsNullOrEmpty(branchText) ||
                     string.Equals(branchText, AllBranchesLabel, StringComparison.OrdinalIgnoreCase)
            ? null
            : branchText;

        Result = new ConfigDocumentType
        {
            Id = _existing?.Id ?? 0,
            Name = NameBox.Text.Trim(),
            Category = "",
            Keywords = string.IsNullOrWhiteSpace(KeywordsBox.Text) ? null : KeywordsBox.Text.Trim(),
            DisplayOrder = int.TryParse(OrderBox.Text, out var order) ? order : 0,
            IsActive = ActiveCheck.IsChecked == true,
            CreatedAt = _existing?.CreatedAt ?? DateTime.UtcNow.ToString("O"),
            Branch = branch,
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
