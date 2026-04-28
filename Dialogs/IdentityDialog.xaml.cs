using System.Windows;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Services;
using WorkAudit.Storage;

namespace WorkAudit.Dialogs;

public partial class IdentityDialog : Window
{
    public string? UserName => UserBox.Text?.Trim();

    public IdentityDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        var config = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
        if (config == null) return;
        var isArabic = ReportLocalizationService.IsArabic(config);
        FlowDirection = isArabic ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;
        Title = ReportLocalizationService.GetString("Identity", config);
        if (PromptLabel != null) PromptLabel.Text = ReportLocalizationService.GetString("IdentityPrompt", config);
        if (OkButton != null) OkButton.Content = ReportLocalizationService.GetString("OK", config);
        if (CancelButton != null) CancelButton.Content = ReportLocalizationService.GetString("Cancel", config);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UserBox.Text))
        {
            MessageBox.Show("Please enter your name or staff ID.", "Identity");
            return;
        }
        DialogResult = true;
        Close();
    }
}
