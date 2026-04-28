using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views;

/// <summary>
/// Combined Input view containing Import and Webcam tabs for document capture.
/// Step 1 of the audit document lifecycle workflow.
/// Shared Branch and Date picker used by both Import and Webcam tabs.
/// </summary>
public partial class InputView : UserControl
{
    private bool _sharedMetadataVisible = true;

    public InputView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        PopulateSharedControls();
        ApplyLocalization();
        if (ImportHost != null)
            ImportHost.Content = new ImportView(this);
        if (InputTabs != null)
            InputTabs.SelectionChanged += InputTabs_SelectionChanged;
    }

    private void InputTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (InputTabs?.SelectedIndex != 1 || WebcamHost == null || WebcamHost.Content != null) return;
        WebcamHost.Content = new WebcamView(this);
    }

    private void PopulateSharedControls()
    {
        var config = ServiceContainer.GetService<AppConfiguration>();
        var permissionService = ServiceContainer.GetService<IPermissionService>();
        var session = ServiceContainer.GetService<ISessionService>();
        var isManagerOrAdmin = permissionService?.HasMinimumRole(Roles.Manager) ?? false;
        var canPickAnyBranch = isManagerOrAdmin || Branches.ScopesToAllBranches(session?.CurrentUser?.Branch);
        var userBranch = Branches.ToConcreteBranchOrDefault(session?.CurrentUser?.Branch ?? config?.CurrentUserBranch);

        BranchCombo?.Items.Clear();
        if (canPickAnyBranch)
        {
            foreach (var b in Branches.All) BranchCombo?.Items.Add(b);
            if (BranchCombo != null) BranchCombo.IsEnabled = true;
        }
        else
        {
            BranchCombo?.Items.Add(userBranch);
            if (BranchCombo != null)
            {
                BranchCombo.IsEnabled = false;
                // Must select the single item or the ComboBox shows blank (SelectedItem was only set in manager branch).
                BranchCombo.SelectedItem = userBranch;
                if (BranchCombo.SelectedIndex < 0)
                    BranchCombo.SelectedIndex = 0;
            }
        }
        if (BranchCombo != null && canPickAnyBranch)
        {
            var pick = Branches.All.Contains(userBranch) ? userBranch : Branches.Default;
            BranchCombo.SelectedItem = pick;
            if (BranchCombo.SelectedIndex < 0) BranchCombo.SelectedIndex = 0;
        }
        if (DocumentDatePicker != null)
            DocumentDatePicker.SelectedDate = DateTime.Today;

        if (SectionCombo != null)
        {
            SectionCombo.Items.Clear();
            foreach (var s in Enums.SectionValues)
                SectionCombo.Items.Add(s);
            SectionCombo.SelectedIndex = 0;
            SectionCombo.SelectionChanged -= SectionCombo_SelectionChanged;
            SectionCombo.SelectionChanged += SectionCombo_SelectionChanged;
        }
        RefreshDocumentTypeCombo();
    }

    private void SectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshDocumentTypeCombo();
    }

    private void RefreshDocumentTypeCombo()
    {
        if (DocumentTypeCombo == null) return;

        var configStore = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
        var previous = DocumentTypeCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(previous))
            previous = null;

        var selectedSection = SectionCombo?.SelectedItem as string;
        var docTypeService = ServiceContainer.GetService<IDocumentTypeService>();
        var availableTypes = docTypeService.GetDocumentTypesForSection(selectedSection);

        DocumentTypeCombo.Items.Clear();
        DocumentTypeCombo.Items.Add(ReportLocalizationService.GetDocumentTypeListLabel(null, configStore));
        foreach (var t in availableTypes)
            DocumentTypeCombo.Items.Add(t);

        if (previous != null)
        {
            foreach (var item in DocumentTypeCombo.Items)
            {
                if (item is string existing && string.Equals(existing, previous, StringComparison.OrdinalIgnoreCase))
                {
                    DocumentTypeCombo.SelectedItem = item;
                    return;
                }
            }
        }

        DocumentTypeCombo.SelectedIndex = 0;
    }

    private void ApplyLocalization()
    {
        var config = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
        if (config == null) return;
        if (ImportTab != null) ImportTab.Header = ReportLocalizationService.GetString("Import", config);
        if (WebcamTab != null) WebcamTab.Header = ReportLocalizationService.GetString("Webcam", config);
        if (InputBranchLabel != null) InputBranchLabel.Text = ReportLocalizationService.GetString("Branch", config);
        if (InputDateLabel != null) InputDateLabel.Text = ReportLocalizationService.GetString("InputDate", config);
        if (InputDocumentTypeLabel != null) InputDocumentTypeLabel.Text = ReportLocalizationService.GetString("InputDocumentType", config);
        if (InputSectionLabel != null) InputSectionLabel.Text = ReportLocalizationService.GetString("InputSection", config);
    }

    /// <summary>Selected type from Input, or null when the first list entry (unclassified) is selected.</summary>
    public string? GetSelectedDocumentTypeOrNull()
    {
        if (DocumentTypeCombo == null || DocumentTypeCombo.SelectedIndex <= 0) return null;
        return DocumentTypeCombo.SelectedItem as string;
    }

    /// <summary>Selected section from Input (defaults to first enum value when unset).</summary>
    public string? GetSelectedSectionOrNull() => SectionCombo?.SelectedItem as string;

    /// <summary>
    /// Imports the specified files into the application.
    /// </summary>
    public Task ImportFilesAsync(IEnumerable<string> filePaths)
    {
        if (ImportHost?.Content is ImportView importView)
        {
            return importView.ImportFilesAsync(filePaths);
        }
        return Task.CompletedTask;
    }

    /// <summary>Show or hide Branch / Date / Document type / Section row (more room for Import / Webcam).</summary>
    public void SetSharedMetadataVisible(bool visible)
    {
        _sharedMetadataVisible = visible;
        if (SharedMetadataPanel != null)
            SharedMetadataPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool IsSharedMetadataVisible => _sharedMetadataVisible;

    /// <summary>
    /// Imports all documents from the specified folder.
    /// </summary>
    public Task ImportFolderAsync(string folderPath)
    {
        if (ImportHost?.Content is ImportView importView)
        {
            return importView.ImportFolderAsync(folderPath);
        }
        return Task.CompletedTask;
    }
}
