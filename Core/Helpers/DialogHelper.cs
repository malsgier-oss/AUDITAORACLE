using System.Windows;
using System.Windows.Controls;
using WorkAudit.Domain;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace WorkAudit.Core.Helpers;

/// <summary>
/// Helper class for creating consistent dialogs throughout the application.
/// </summary>
public static class DialogHelper
{
    /// <summary>
    /// Creates a dialog with a single ComboBox for selection.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="label">Label above the ComboBox</param>
    /// <param name="items">Items to display in the ComboBox</param>
    /// <param name="selectedItem">Initially selected item</param>
    /// <param name="owner">Parent window</param>
    /// <param name="comboBox">Output parameter for the ComboBox control</param>
    /// <returns>The created dialog window</returns>
    public static Window CreateComboBoxDialog(
        string title,
        string label,
        IEnumerable<string> items,
        string? selectedItem,
        Window? owner,
        out WpfComboBox comboBox)
    {
        comboBox = new WpfComboBox
        {
            ItemsSource = items,
            SelectedItem = selectedItem ?? items.FirstOrDefault(),
            MinWidth = 280,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = System.Windows.Media.Brushes.Black
        });
        panel.Children.Add(comboBox);

        var btnPanel = CreateButtonPanel(out var okBtn, out var cancelBtn);
        panel.Children.Add(btnPanel);

        var dlg = new Window
        {
            Title = title,
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Content = panel,
            Background = AppColors.DialogBackgroundBrush,
            ResizeMode = ResizeMode.NoResize
        };

        okBtn.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        cancelBtn.Click += (_, _) => { dlg.DialogResult = false; dlg.Close(); };

        return dlg;
    }

    /// <summary>
    /// Creates a dialog with two ComboBoxes for selection (e.g., Type and Section).
    /// </summary>
    public static Window CreateTwoComboBoxDialog(
        string title,
        string label1,
        IEnumerable<string> items1,
        string? selected1,
        string label2,
        IEnumerable<string> items2,
        string? selected2,
        Window? owner,
        out WpfComboBox comboBox1,
        out WpfComboBox comboBox2)
    {
        comboBox1 = new WpfComboBox
        {
            ItemsSource = items1,
            SelectedItem = selected1 ?? items1.FirstOrDefault(),
            MinWidth = 240,
            Margin = new Thickness(0, 0, 0, 8)
        };

        comboBox2 = new WpfComboBox
        {
            ItemsSource = items2,
            SelectedItem = selected2 ?? items2.FirstOrDefault(),
            MinWidth = 240,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = label1,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = System.Windows.Media.Brushes.Black
        });
        panel.Children.Add(comboBox1);
        panel.Children.Add(new TextBlock
        {
            Text = label2,
            Margin = new Thickness(0, 8, 0, 4),
            Foreground = System.Windows.Media.Brushes.Black
        });
        panel.Children.Add(comboBox2);

        var btnPanel = CreateButtonPanel(out var okBtn, out var cancelBtn);
        panel.Children.Add(btnPanel);

        var dlg = new Window
        {
            Title = title,
            Width = 300,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Content = panel,
            Background = AppColors.DialogBackgroundBrush,
            ResizeMode = ResizeMode.NoResize
        };

        okBtn.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        cancelBtn.Click += (_, _) => { dlg.DialogResult = false; dlg.Close(); };

        return dlg;
    }

    /// <summary>
    /// Shows a ComboBox dialog and returns the selected value.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="label">Label above the ComboBox</param>
    /// <param name="items">Items to display</param>
    /// <param name="selectedItem">Initially selected item</param>
    /// <param name="owner">Parent window</param>
    /// <returns>Selected value if OK was clicked, null otherwise</returns>
    public static string? ShowComboBoxDialog(
        string title,
        string label,
        IEnumerable<string> items,
        string? selectedItem,
        Window? owner)
    {
        var dlg = CreateComboBoxDialog(title, label, items, selectedItem, owner, out var combo);
        return dlg.ShowDialog() == true ? combo.SelectedItem as string : null;
    }

    /// <summary>
    /// Shows a follow-up reminder dialog. User picks: None, 1 day, 3 days, 1 week, 2 weeks, or Custom date.
    /// </summary>
    /// <returns>Due date if OK and not None, null otherwise</returns>
    public static DateTime? ShowFollowUpReminderDialog(Window? owner)
    {
        var options = new[] { "None", "1 day", "3 days", "1 week", "2 weeks", "Custom" };
        var combo = new WpfComboBox
        {
            ItemsSource = options,
            SelectedItem = "3 days",
            MinWidth = 200,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var datePicker = new DatePicker
        {
            SelectedDate = DateTime.UtcNow.Date.AddDays(3),
            Margin = new Thickness(0, 0, 0, 12),
            Visibility = Visibility.Collapsed
        };
        combo.SelectionChanged += (_, _) =>
        {
            datePicker.Visibility = combo.SelectedItem as string == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "Remind me:",
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = System.Windows.Media.Brushes.Black
        });
        panel.Children.Add(combo);
        panel.Children.Add(datePicker);

        var btnPanel = CreateButtonPanel(out var okBtn, out var cancelBtn);
        panel.Children.Add(btnPanel);

        var dlg = new Window
        {
            Title = "Flag for Follow-up",
            Width = 300,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Content = panel,
            Background = AppColors.DialogBackgroundBrush,
            ResizeMode = ResizeMode.NoResize
        };

        okBtn.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        cancelBtn.Click += (_, _) => { dlg.DialogResult = false; dlg.Close(); };

        if (dlg.ShowDialog() != true) return null;

        var choice = combo.SelectedItem as string;
        if (choice == "None") return null;
        if (choice == "Custom")
            return datePicker.SelectedDate?.Date.ToUniversalTime();
        var now = DateTime.UtcNow.Date;
        var due = choice switch
        {
            "1 day" => now.AddDays(1),
            "3 days" => now.AddDays(3),
            "1 week" => now.AddDays(7),
            "2 weeks" => now.AddDays(14),
            _ => (DateTime?)null
        };
        return due;
    }

    /// <summary>
    /// Shows a two-ComboBox dialog and returns both selected values.
    /// </summary>
    /// <returns>Tuple of (value1, value2) if OK was clicked, null otherwise</returns>
    public static (string? value1, string? value2)? ShowTwoComboBoxDialog(
        string title,
        string label1,
        IEnumerable<string> items1,
        string? selected1,
        string label2,
        IEnumerable<string> items2,
        string? selected2,
        Window? owner)
    {
        var dlg = CreateTwoComboBoxDialog(title, label1, items1, selected1, label2, items2, selected2, owner, out var combo1, out var combo2);
        if (dlg.ShowDialog() == true)
        {
            return (combo1.SelectedItem as string, combo2.SelectedItem as string);
        }
        return null;
    }

    /// <summary>
    /// Shows a two-ComboBox dialog where document type options are filtered by selected section.
    /// </summary>
    public static (string? documentType, string? section)? ShowSectionFilteredTypeDialog(
        string title,
        string typeLabel,
        string sectionLabel,
        IEnumerable<string> sections,
        string? selectedSection,
        Func<string?, IEnumerable<string>> typeProvider,
        string? selectedType,
        Window? owner)
    {
        var sectionItems = sections?.ToList() ?? new List<string>();
        if (sectionItems.Count == 0)
            return null;

        var sectionCombo = new WpfComboBox
        {
            ItemsSource = sectionItems,
            SelectedItem = selectedSection ?? sectionItems.FirstOrDefault(),
            MinWidth = 240,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var typeCombo = new WpfComboBox
        {
            MinWidth = 240,
            Margin = new Thickness(0, 0, 0, 12)
        };

        void RebindTypes(string? preferredType = null)
        {
            var selectedSectionValue = sectionCombo.SelectedItem as string ?? sectionCombo.Text;
            var sectionValue = string.IsNullOrWhiteSpace(selectedSectionValue) ? null : selectedSectionValue.Trim();
            var options = typeProvider(sectionValue)?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();

            if (options.Count == 0)
                options.Add(DocumentTypeInfo.UnclassifiedType);

            var desired = preferredType;
            if (string.IsNullOrWhiteSpace(desired))
                desired = typeCombo.SelectedItem as string ?? typeCombo.Text?.Trim();
            if (string.IsNullOrWhiteSpace(desired))
                desired = selectedType;

            typeCombo.ItemsSource = options;
            if (!string.IsNullOrWhiteSpace(desired))
            {
                var exact = options.FirstOrDefault(x => string.Equals(x, desired, StringComparison.OrdinalIgnoreCase));
                typeCombo.SelectedItem = exact ?? options[0];
            }
            else
            {
                typeCombo.SelectedItem = options[0];
            }
        }

        RebindTypes(selectedType);
        sectionCombo.SelectionChanged += (_, _) => RebindTypes();

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = sectionLabel,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = System.Windows.Media.Brushes.Black
        });
        panel.Children.Add(sectionCombo);
        panel.Children.Add(new TextBlock
        {
            Text = typeLabel,
            Margin = new Thickness(0, 8, 0, 4),
            Foreground = System.Windows.Media.Brushes.Black
        });
        panel.Children.Add(typeCombo);

        var btnPanel = CreateButtonPanel(out var okBtn, out var cancelBtn);
        panel.Children.Add(btnPanel);

        var dlg = new Window
        {
            Title = title,
            Width = 300,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Content = panel,
            Background = AppColors.DialogBackgroundBrush,
            ResizeMode = ResizeMode.NoResize
        };

        okBtn.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        cancelBtn.Click += (_, _) => { dlg.DialogResult = false; dlg.Close(); };

        if (dlg.ShowDialog() == true)
            return (typeCombo.SelectedItem as string, sectionCombo.SelectedItem as string);

        return null;
    }

    /// <summary>
    /// Shows a dialog with two text fields (e.g. Title and URL) and returns both values.
    /// </summary>
    /// <returns>Tuple of (value1, value2) if OK was clicked, null otherwise</returns>
    public static (string? value1, string? value2)? ShowTwoTextBoxDialog(
        string title,
        string label1,
        string label2,
        string? default1,
        string? default2,
        Window? owner)
    {
        var textBox1 = new WpfTextBox
        {
            Text = default1 ?? "",
            MinWidth = 280,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var textBox2 = new WpfTextBox
        {
            Text = default2 ?? "",
            MinWidth = 280,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = label1,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = System.Windows.Media.Brushes.Black
        });
        panel.Children.Add(textBox1);
        panel.Children.Add(new TextBlock
        {
            Text = label2,
            Margin = new Thickness(0, 8, 0, 4),
            Foreground = System.Windows.Media.Brushes.Black
        });
        panel.Children.Add(textBox2);

        var btnPanel = CreateButtonPanel(out var okBtn, out var cancelBtn);
        panel.Children.Add(btnPanel);

        var dlg = new Window
        {
            Title = title,
            Width = 320,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Content = panel,
            Background = AppColors.DialogBackgroundBrush,
            ResizeMode = ResizeMode.NoResize
        };

        okBtn.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        cancelBtn.Click += (_, _) => { dlg.DialogResult = false; dlg.Close(); };

        if (dlg.ShowDialog() != true) return null;
        return (textBox1.Text?.Trim(), textBox2.Text?.Trim());
    }

    private static StackPanel CreateButtonPanel(out WpfButton okBtn, out WpfButton cancelBtn)
    {
        okBtn = new WpfButton
        {
            Content = "OK",
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 75
        };

        cancelBtn = new WpfButton
        {
            Content = "Cancel",
            Padding = new Thickness(16, 6, 16, 6),
            MinWidth = 75
        };

        return new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { okBtn, cancelBtn }
        };
    }
}
