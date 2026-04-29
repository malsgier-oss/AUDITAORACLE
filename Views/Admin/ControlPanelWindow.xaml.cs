using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Serilog;
using WorkAudit.Config;
using WorkAudit.Core;
using WorkAudit.Core.Backup;
using WorkAudit.Core.Security;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Services;
using WorkAudit.Core.TextExtraction;
using WorkAudit.Dialogs;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views.Admin;

/// <summary>
/// Control Panel window for managing application settings.
/// </summary>
public partial class ControlPanelWindow : Window
{
    private static readonly (int Id, string Label)[] OcrPsmChoices =
    {
        (0, "0 — OSD only"),
        (1, "1 — Auto + OSD"),
        (2, "2 — Auto only (no OSD)"),
        (3, "3 — Fully automatic (default)"),
        (4, "4 — Single column"),
        (5, "5 — Single vertical block"),
        (6, "6 — Single uniform block"),
        (7, "7 — Single text line"),
        (8, "8 — Single word"),
        (9, "9 — Circle word"),
        (10, "10 — Single character"),
        (11, "11 — Sparse text"),
        (12, "12 — Sparse text + OSD"),
        (13, "13 — Raw line")
    };

    private readonly IConfigStore _configStore;
    private readonly IDocumentStore _documentStore;
    private readonly IEnvironmentService _environmentService;
    private readonly IBackupService _backupService;
    private readonly IUserStore _userStore;
    private readonly IMigrationService _migrationService;
    private readonly IPermissionService _permissionService;
    private readonly bool _isFullControlPanel;
    private readonly ILogger _log = LoggingService.ForContext<ControlPanelWindow>();

    private bool _isDirty;
    private string _initialLanguage = "en";
    private readonly IKeyboardShortcutService _keyboardShortcutService;
    private readonly Dictionary<string, string> _keyboardShortcutDraft = new();
    private readonly Dictionary<string, System.Windows.Controls.TextBox> _shortcutValueBoxes = new();
    private string? _capturingShortcutId;

    public ControlPanelWindow()
    {
        InitializeComponent();
        _keyboardShortcutService = ServiceContainer.GetService<IKeyboardShortcutService>();
        _configStore = ServiceContainer.GetService<IConfigStore>();
        _documentStore = ServiceContainer.GetService<IDocumentStore>();
        _environmentService = ServiceContainer.GetService<IEnvironmentService>();
        _backupService = ServiceContainer.GetService<IBackupService>();
        _userStore = ServiceContainer.GetService<IUserStore>();
        _migrationService = ServiceContainer.GetService<IMigrationService>();
        _permissionService = ServiceContainer.GetService<IPermissionService>();
        _isFullControlPanel = _permissionService.HasMinimumRole(Roles.Manager);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalization();
        ApplyControlPanelAccess();
        LoadThemeSettings();
        LoadReportLanguageSetting();
        if (_isFullControlPanel)
        {
            LoadDocumentTypes();
            LoadBranches();
            LoadSecuritySettings();
            LoadWorkflowSettings();
            LoadBackupSettings();
            LoadSystemInfo();
        }
        LoadKeyboardShortcuts();
        LoadOcrSettings();
    }

    private void ApplyControlPanelAccess()
    {
        if (_isFullControlPanel)
        {
            foreach (var tab in new TabItem?[]
                     {
                         GeneralTab, ShortcutsTab, DocTypesTab, BranchesTab, SecurityTab, WorkflowTab, OcrTab,
                         BackupTab, SystemTab
                     })
            {
                if (tab != null) tab.Visibility = Visibility.Visible;
            }
            if (SaveBtn != null)
                SaveBtn.Content = ReportLocalizationService.GetString("SaveAllSettings", _configStore);
            return;
        }

        foreach (var tab in new[] { DocTypesTab, BranchesTab, SecurityTab, WorkflowTab, BackupTab, SystemTab })
        {
            if (tab != null) tab.Visibility = Visibility.Collapsed;
        }
        foreach (var tab in new[] { GeneralTab, ShortcutsTab, OcrTab })
        {
            if (tab != null) tab.Visibility = Visibility.Visible;
        }

        if (HeaderTitle != null)
            HeaderTitle.Text = ReportLocalizationService.GetString("PreferencesWindowTitle", _configStore);
        if (HeaderSubtitle != null)
            HeaderSubtitle.Text = ReportLocalizationService.GetString("PreferencesWindowSubtitle", _configStore);
        Title = ReportLocalizationService.GetString("PreferencesWindowTitle", _configStore) + " - " +
                ReportLocalizationService.GetString("AppBrandName", _configStore);
        if (SaveBtn != null)
            SaveBtn.Content = ReportLocalizationService.GetString("SavePreferences", _configStore);
    }

    private void ApplyLocalization()
    {
        if (_configStore == null) return;
        // Keep shell layout fixed in LTR; language selection only changes localized strings.
        FlowDirection = ReportLocalizationService.ShellFlowDirection;
        if (_isFullControlPanel)
        {
            Title = ReportLocalizationService.GetString("ControlPanelTitle", _configStore) + " - " +
                    ReportLocalizationService.GetString("AppBrandName", _configStore);
            if (HeaderTitle != null) HeaderTitle.Text = ReportLocalizationService.GetString("ControlPanelTitle", _configStore);
            if (HeaderSubtitle != null) HeaderSubtitle.Text = ReportLocalizationService.GetString("ControlPanelSubtitle", _configStore);
        }
        if (GeneralTab != null) GeneralTab.Header = ReportLocalizationService.GetString("General", _configStore);
        if (AppLanguageSectionTitle != null) AppLanguageSectionTitle.Text = ReportLocalizationService.GetString("ApplicationLanguage", _configStore);
        if (LanguageLabel != null) LanguageLabel.Text = ReportLocalizationService.GetString("Language", _configStore);
        if (AppLanguageDescription != null) AppLanguageDescription.Text = ReportLocalizationService.GetString("AppLanguageDescription", _configStore);
        if (DocTypesTab != null) DocTypesTab.Header = ReportLocalizationService.GetString("DocumentTypes", _configStore);
        if (BranchesTab != null) BranchesTab.Header = ReportLocalizationService.GetString("Branches", _configStore);
        if (SecurityTab != null) SecurityTab.Header = ReportLocalizationService.GetString("Security", _configStore);
        if (WorkflowTab != null) WorkflowTab.Header = ReportLocalizationService.GetString("Workflow", _configStore);
        if (OcrTab != null) OcrTab.Header = ReportLocalizationService.GetString("OcrTab", _configStore);
        if (OcrSectionIntroTitle != null) OcrSectionIntroTitle.Text = ReportLocalizationService.GetString("OcrTab", _configStore);
        if (OcrSectionIntroBody != null) OcrSectionIntroBody.Text = ReportLocalizationService.GetString("OcrTabIntro", _configStore);
        if (BackupTab != null) BackupTab.Header = ReportLocalizationService.GetString("Backup", _configStore);
        if (SystemTab != null) SystemTab.Header = ReportLocalizationService.GetString("System", _configStore);
        if (AddDocTypeBtn != null) AddDocTypeBtn.Content = ReportLocalizationService.GetString("AddNew", _configStore);
        if (EditDocTypeBtn != null) EditDocTypeBtn.Content = ReportLocalizationService.GetString("Edit", _configStore);
        if (DeleteDocTypeBtn != null) DeleteDocTypeBtn.Content = ReportLocalizationService.GetString("Delete", _configStore);
        if (ShowInactiveDocTypes != null) ShowInactiveDocTypes.Content = ReportLocalizationService.GetString("ShowInactive", _configStore);
        if (AddBranchBtn != null) AddBranchBtn.Content = ReportLocalizationService.GetString("AddNew", _configStore);
        if (EditBranchBtn != null) EditBranchBtn.Content = ReportLocalizationService.GetString("Edit", _configStore);
        if (DeleteBranchBtn != null) DeleteBranchBtn.Content = ReportLocalizationService.GetString("Delete", _configStore);
        if (SaveBtn != null) SaveBtn.Content = ReportLocalizationService.GetString("SaveAllSettings", _configStore);
        if (CloseBtn != null) CloseBtn.Content = ReportLocalizationService.GetString("Close", _configStore);
        if (DocTypesColBranch != null) DocTypesColBranch.Header = ReportLocalizationService.GetString("Branch", _configStore);
        if (DocTypesColSection != null) DocTypesColSection.Header = ReportLocalizationService.GetString("Section", _configStore);
        if (DocTypesColName != null) DocTypesColName.Header = ReportLocalizationService.GetString("Name", _configStore);
        if (DocTypesColKeywords != null) DocTypesColKeywords.Header = ReportLocalizationService.GetString("Keywords", _configStore);
        if (DocTypesColActive != null) DocTypesColActive.Header = ReportLocalizationService.GetString("Active", _configStore);
        if (DocTypesColOrder != null) DocTypesColOrder.Header = ReportLocalizationService.GetString("Order", _configStore);
        if (BranchesColName != null) BranchesColName.Header = ReportLocalizationService.GetString("Name", _configStore);
        if (BranchesColCode != null) BranchesColCode.Header = ReportLocalizationService.GetString("Code", _configStore);
        if (BranchesColActive != null) BranchesColActive.Header = ReportLocalizationService.GetString("Active", _configStore);
        if (BranchesColOrder != null) BranchesColOrder.Header = ReportLocalizationService.GetString("Order", _configStore);
        if (AppearanceSectionTitle != null) AppearanceSectionTitle.Text = ReportLocalizationService.GetString("Appearance", _configStore);
        if (ThemeLabel != null) ThemeLabel.Text = ReportLocalizationService.GetString("Theme", _configStore);
        if (ShortcutsTab != null) ShortcutsTab.Header = ReportLocalizationService.GetString("ShortcutsTab", _configStore);
        if (ShortcutsIntroText != null)
            ShortcutsIntroText.Text = ReportLocalizationService.GetString(
                _isFullControlPanel ? "ShortcutsIntro" : "ShortcutsIntroPreferences", _configStore);
        if (ResetKeyboardShortcutsBtn != null) ResetKeyboardShortcutsBtn.Content = ReportLocalizationService.GetString("ShortcutsResetAll", _configStore);
    }

    private void LoadThemeSettings()
    {
        ThemeCombo?.Items.Clear();
        ThemeCombo?.Items.Add(new ComboBoxItem { Content = "Light", Tag = ThemeService.ThemeLight });
        ThemeCombo?.Items.Add(new ComboBoxItem { Content = "Dark Midnight", Tag = ThemeService.ThemeDarkMidnight });
        var theme = UserSettings.Get<string>("theme", ThemeService.ThemeLight) ?? ThemeService.ThemeLight;
        var themeItem = ThemeCombo?.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == theme);
        if (ThemeCombo != null && themeItem != null)
            ThemeCombo.SelectedItem = themeItem;
        else if (ThemeCombo != null && ThemeCombo.Items.Count > 0)
            ThemeCombo.SelectedItem = ThemeCombo.Items[0];
    }

    private void SaveThemeSettings()
    {
        var themeTag = (ThemeCombo?.SelectedItem as ComboBoxItem)?.Tag as string ?? ThemeService.ThemeLight;
        ThemeService.ApplyTheme(themeTag, save: true);
    }

    #region Document Types

    private void LoadDocumentTypes()
    {
        var includeInactive = ShowInactiveDocTypes?.IsChecked == true;
        var types = _configStore.GetDocumentTypes(includeInactive);
        DocTypesGrid.ItemsSource = types;
        DocTypesCountLabel.Text = ReportLocalizationService.GetString("DocumentTypesCount", _configStore, types.Count);
    }

    private void ShowInactiveDocTypes_Changed(object sender, RoutedEventArgs e) => LoadDocumentTypes();

    private void DocTypesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = DocTypesGrid.SelectedItem != null;
        EditDocTypeBtn.IsEnabled = hasSelection;
        DeleteDocTypeBtn.IsEnabled = hasSelection;
    }

    private void AddDocType_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new EditDocumentTypeDialog(null) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _configStore.InsertDocumentType(dialog.Result);
            ServiceContainer.GetService<IDocumentTypeService>().RefreshFromDatabase();
            LoadDocumentTypes();
            StatusLabel.Text = "Document type added.";
            _isDirty = true;
        }
    }

    private void EditDocType_Click(object sender, RoutedEventArgs e)
    {
        if (DocTypesGrid.SelectedItem is not ConfigDocumentType selected) return;
        var dialog = new EditDocumentTypeDialog(selected) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _configStore.UpdateDocumentType(dialog.Result);
            ServiceContainer.GetService<IDocumentTypeService>().RefreshFromDatabase();
            LoadDocumentTypes();
            StatusLabel.Text = "Document type updated.";
            _isDirty = true;
        }
    }

    private void DeleteDocType_Click(object sender, RoutedEventArgs e)
    {
        if (DocTypesGrid.SelectedItem is not ConfigDocumentType selected) return;
        if (MessageBox.Show($"Delete document type '{selected.Name}'?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        _configStore.DeleteDocumentType(selected.Id);
        ServiceContainer.GetService<IDocumentTypeService>().RefreshFromDatabase();
        LoadDocumentTypes();
        StatusLabel.Text = "Document type deleted.";
        _isDirty = true;
    }

    #endregion

    #region Branches

    private void LoadBranches()
    {
        var branches = _configStore.GetBranches(true);
        BranchesGrid.ItemsSource = branches;
        BranchesCountLabel.Text = ReportLocalizationService.GetString("BranchesCount", _configStore, branches.Count);
    }

    private void BranchesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = BranchesGrid.SelectedItem != null;
        EditBranchBtn.IsEnabled = hasSelection;
        DeleteBranchBtn.IsEnabled = hasSelection;
    }

    private void AddBranch_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new EditBranchDialog(null) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _configStore.InsertBranch(dialog.Result);
            LoadBranches();
            StatusLabel.Text = "Branch added.";
            _isDirty = true;
        }
    }

    private void EditBranch_Click(object sender, RoutedEventArgs e)
    {
        if (BranchesGrid.SelectedItem is not ConfigBranch selected) return;
        var dialog = new EditBranchDialog(selected) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _configStore.UpdateBranch(dialog.Result);
            LoadBranches();
            StatusLabel.Text = "Branch updated.";
            _isDirty = true;
        }
    }

    private void DeleteBranch_Click(object sender, RoutedEventArgs e)
    {
        if (BranchesGrid.SelectedItem is not ConfigBranch selected) return;
        if (MessageBox.Show($"Delete branch '{selected.Name}'?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        _configStore.DeleteBranch(selected.Id);
        LoadBranches();
        StatusLabel.Text = "Branch deleted.";
        _isDirty = true;
    }

    #endregion

    #region Security Settings

    private void LoadSecuritySettings()
    {
        SessionTimeoutBox.Text = _configStore.GetSettingInt("session_timeout_minutes", 30).ToString(CultureInfo.InvariantCulture);
        SessionExpiryBox.Text = _configStore.GetSettingInt("session_expiry_hours", 8).ToString(CultureInfo.InvariantCulture);
        MaxLoginAttemptsBox.Text = _configStore.GetSettingInt("max_login_attempts", 5).ToString(CultureInfo.InvariantCulture);
        LockoutDurationBox.Text = _configStore.GetSettingInt("lockout_duration_minutes", 30).ToString(CultureInfo.InvariantCulture);

        PasswordMinLengthBox.Text = _configStore.GetSettingInt("password_min_length", 8).ToString(CultureInfo.InvariantCulture);
        PasswordUppercaseCheck.IsChecked = _configStore.GetSettingBool("password_require_uppercase", true);
        PasswordLowercaseCheck.IsChecked = _configStore.GetSettingBool("password_require_lowercase", true);
        PasswordDigitCheck.IsChecked = _configStore.GetSettingBool("password_require_digit", true);
        PasswordSpecialCheck.IsChecked = _configStore.GetSettingBool("password_require_special", true);
    }

    private void SaveSecuritySettings()
    {
        var config = ServiceContainer.GetService<AppConfiguration>();
        var user = config.CurrentUserName ?? "system";

        if (int.TryParse(SessionTimeoutBox.Text, out var timeout))
            _configStore.SetSettingInt("session_timeout_minutes", timeout, user);
        if (int.TryParse(SessionExpiryBox.Text, out var expiry))
            _configStore.SetSettingInt("session_expiry_hours", expiry, user);
        if (int.TryParse(MaxLoginAttemptsBox.Text, out var maxAttempts))
            _configStore.SetSettingInt("max_login_attempts", maxAttempts, user);
        if (int.TryParse(LockoutDurationBox.Text, out var lockout))
            _configStore.SetSettingInt("lockout_duration_minutes", lockout, user);

        if (int.TryParse(PasswordMinLengthBox.Text, out var minLength))
            _configStore.SetSettingInt("password_min_length", minLength, user);
        _configStore.SetSettingBool("password_require_uppercase", PasswordUppercaseCheck.IsChecked == true, user);
        _configStore.SetSettingBool("password_require_lowercase", PasswordLowercaseCheck.IsChecked == true, user);
        _configStore.SetSettingBool("password_require_digit", PasswordDigitCheck.IsChecked == true, user);
        _configStore.SetSettingBool("password_require_special", PasswordSpecialCheck.IsChecked == true, user);
    }

    #endregion

    #region Workflow Settings

    private void LoadWorkflowSettings()
    {
        RenameFileOnClassifyCheck.IsChecked = _configStore.GetSettingBool("rename_file_on_classify", true);
        ArchiveRetentionYearsBox.Text = _configStore.GetSettingInt("archive_retention_years", 7).ToString(CultureInfo.InvariantCulture);
        EnableAutoCaptureCheck.IsChecked = _configStore.GetSettingBool("enable_auto_capture", false);
        ShowAutoCaptureTimerCheck.IsChecked = _configStore.GetSettingBool("enable_auto_capture_cooldown_timer", true);
        AutoCaptureCooldownSecondsBox.Text = _configStore.GetSettingInt("auto_capture_cooldown_seconds", 8).ToString(CultureInfo.InvariantCulture);
        DefaultScanAreaModeCheck.IsChecked = _configStore.GetSettingBool("webcam_default_scan_area_mode", false);
        ScanAreaAutoDefaultCheck.IsChecked = _configStore.GetSettingBool("webcam_scan_area_auto_capture", false);
        LoadKpiTargets();
        LoadScheduledReportSettings();
    }

    private void LoadReportLanguageSetting()
    {
        ReportLanguageCombo.Items.Clear();
        ReportLanguageCombo.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
        ReportLanguageCombo.Items.Add(new ComboBoxItem { Content = "Arabic (العربية)", Tag = "ar" });
        var lang = _configStore.GetSettingValue("report_language", "en") ?? "en";
        _initialLanguage = lang;
        var langItem = ReportLanguageCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == lang);
        ReportLanguageCombo.SelectedItem = langItem ?? ReportLanguageCombo.Items[0];
    }

    private void LoadOcrSettings()
    {
        if (OcrUseLayoutAssemblyCheck == null) return;

        OcrUseLayoutAssemblyCheck.IsChecked = _configStore.GetSettingBool("ocr_use_layout_assembly", true);

        if (OcrPsmCombo != null && OcrPsmCombo.Items.Count == 0)
        {
            foreach (var (id, label) in OcrPsmChoices)
                OcrPsmCombo.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        }
        if (OcrMergeModeCombo != null && OcrMergeModeCombo.Items.Count == 0)
        {
            OcrMergeModeCombo.Items.Add(new ComboBoxItem { Content = "Accuracy-first", Tag = "accuracy" });
            OcrMergeModeCombo.Items.Add(new ComboBoxItem { Content = "Balanced", Tag = "balanced" });
        }

        var psm = _configStore.GetSettingInt("ocr_tesseract_psm", 3);
        SelectOcrPsmCombo(psm);

        if (OcrLanguageCombo != null && OcrLanguageCombo.Items.Count == 0)
        {
            OcrLanguageCombo.Items.Add(new ComboBoxItem { Content = "English (eng)", Tag = "eng" });
            OcrLanguageCombo.Items.Add(new ComboBoxItem { Content = "Arabic (ara)", Tag = "ara" });
            OcrLanguageCombo.Items.Add(new ComboBoxItem { Content = "English + Arabic (eng+ara)", Tag = "eng+ara" });
        }

        var lang = (_configStore.GetSettingValue("default_ocr_language", "eng") ?? "eng").Replace('_', '+').Trim();
        if (OcrLanguageCombo != null)
        {
            var langItem = OcrLanguageCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Tag as string, lang, StringComparison.OrdinalIgnoreCase));
            OcrLanguageCombo.SelectedItem = langItem ?? OcrLanguageCombo.Items[0];
        }

        if (OcrPdfDpiBox != null)
        OcrPdfDpiBox.Text = _configStore.GetSettingInt("ocr_pdf_render_dpi", 300).ToString(CultureInfo.InvariantCulture);
        if (OcrTessdataPathBox != null)
            OcrTessdataPathBox.Text = _configStore.GetSettingValue("tesseract_tessdata_path", "") ?? "";
        if (OcrNlMeansHBox != null)
        OcrNlMeansHBox.Text = _configStore.GetSettingInt("ocr_preprocess_nlmeans_h", 3).ToString(CultureInfo.InvariantCulture);
        if (OcrEnableMultipassCheck != null)
            OcrEnableMultipassCheck.IsChecked = _configStore.GetSettingBool("ocr_enable_multipass", true);
        if (OcrOrganizeBilingualCheck != null)
            OcrOrganizeBilingualCheck.IsChecked = _configStore.GetSettingBool("ocr_organize_bilingual_output", true);
        if (OcrStructuredMinConfidenceBox != null)
        OcrStructuredMinConfidenceBox.Text = _configStore.GetSettingInt("ocr_structured_min_confidence", 70).ToString(CultureInfo.InvariantCulture);
        if (OcrMergeModeCombo != null)
        {
            var mergeMode = (_configStore.GetSettingValue("ocr_multipass_merge_mode", "accuracy") ?? "accuracy").Trim();
            var mergeItem = OcrMergeModeCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Tag as string, mergeMode, StringComparison.OrdinalIgnoreCase));
            OcrMergeModeCombo.SelectedItem = mergeItem ?? OcrMergeModeCombo.Items[0];
        }

        if (OcrLanguageHint != null)
            OcrLanguageHint.Text = "eng, ara, or both (eng+ara). Match installed .traineddata files.";

        if (OcrTesseractMultipassHint != null)
            OcrTesseractMultipassHint.Text = "Runs multiple OCR passes with different preprocessing and segmentation, then merges best lines.";
    }

    private void SelectOcrPsmCombo(int psm)
    {
        if (OcrPsmCombo == null) return;
        var found = OcrPsmCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is int t && t == psm);
        if (found == null)
            found = OcrPsmCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is int t && t == 3);
        if (found != null)
            OcrPsmCombo.SelectedItem = found;
    }

    private void OcrTessdataBrowse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder containing .traineddata files",
            UseDescriptionForTitle = true
        };
        if (!string.IsNullOrWhiteSpace(OcrTessdataPathBox?.Text) && Directory.Exists(OcrTessdataPathBox.Text))
            dlg.SelectedPath = OcrTessdataPathBox.Text.Trim();
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && OcrTessdataPathBox != null)
            OcrTessdataPathBox.Text = dlg.SelectedPath;
    }

    private void LoadScheduledReportSettings()
    {
        ScheduledReportsEnabledCheck.IsChecked = _configStore.GetSettingBool("scheduled_reports_enabled", false);
        ScheduledReportTypeCombo.Items.Clear();
        foreach (var name in new[] { "Performance", "ExecutiveSummary", "BranchSummary", "IssuesAndFocus" })
            ScheduledReportTypeCombo.Items.Add(name);
        var reportType = _configStore.GetSettingValue("scheduled_report_type", "Performance");
        var idx = ScheduledReportTypeCombo.Items.IndexOf(reportType);
        ScheduledReportTypeCombo.SelectedIndex = idx >= 0 ? idx : 0;
        ScheduledReportTimeBox.Text = _configStore.GetSettingValue("scheduled_report_time", "08:00") ?? "08:00";
        ScheduledReportOutputDirBox.Text = _configStore.GetSettingValue("scheduled_report_output_dir", "") ?? "";
        ScheduledReportEmailBox.Text = _configStore.GetSettingValue("scheduled_report_email_recipients", "") ?? "";
        SmtpHostBox.Text = _configStore.GetSettingValue("smtp_host", "") ?? "";
        SmtpPortBox.Text = _configStore.GetSettingValue("smtp_port", "587") ?? "587";
        SmtpUserBox.Text = _configStore.GetSettingValue("smtp_user", "") ?? "";
        SmtpPasswordBox.Password = _configStore.GetSecureSettingValue("smtp_password", "") ?? "";
    }

    private void LoadKpiTargets()
    {
        var kpiService = ServiceContainer.GetService<IKpiService>();
        var list = kpiService.GetTargets().Select(t => new KpiTarget
        {
            KpiName = t.KpiName,
            Target = t.Target,
            Warning = t.Warning,
            Critical = t.Critical,
            Period = t.Period,
            Branch = t.Branch,
            Section = t.Section
        }).ToList();
        KpiTargetsGrid.ItemsSource = list;
    }

    private void AddKpiTarget_Click(object sender, RoutedEventArgs e)
    {
        if (KpiTargetsGrid.ItemsSource is not List<KpiTarget> list) return;
        list.Add(new KpiTarget
        {
            KpiName = KpiNames.ClearingRate,
            Target = 80,
            Warning = 70,
            Critical = 60,
            Period = "Monthly",
            Branch = "",
            Section = ""
        });
        KpiTargetsGrid.Items.Refresh();
    }

    private void SaveKpiTargets_Click(object sender, RoutedEventArgs e)
    {
        if (KpiTargetsGrid.ItemsSource is not System.Collections.IEnumerable items) return;
        var list = items.Cast<KpiTarget>()
            .Where(t => !string.IsNullOrWhiteSpace(t.KpiName))
            .Select(t => new KpiTarget
            {
                KpiName = t.KpiName?.Trim() ?? "",
                Branch = string.IsNullOrWhiteSpace(t.Branch) ? null : t.Branch?.Trim(),
                Section = string.IsNullOrWhiteSpace(t.Section) ? null : t.Section?.Trim(),
                Target = t.Target,
                Warning = t.Warning,
                Critical = t.Critical,
                Period = t.Period ?? "Monthly"
            })
            .ToList();
        var kpiService = ServiceContainer.GetService<IKpiService>();
        kpiService.SaveTargets(list);
        MessageBox.Show("KPI targets saved.", "KPI Targets", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveWorkflowSettings()
    {
        var config = ServiceContainer.GetService<AppConfiguration>();
        var user = config.CurrentUserName ?? "system";
        _configStore.SetSettingBool("rename_file_on_classify", RenameFileOnClassifyCheck.IsChecked == true, user);
        _configStore.SetSettingBool("enable_auto_capture", EnableAutoCaptureCheck.IsChecked == true, user);
        _configStore.SetSettingBool("enable_auto_capture_cooldown_timer", ShowAutoCaptureTimerCheck.IsChecked == true, user);
        _configStore.SetSettingBool("webcam_default_scan_area_mode", DefaultScanAreaModeCheck.IsChecked == true, user);
        _configStore.SetSettingBool("webcam_scan_area_auto_capture", ScanAreaAutoDefaultCheck.IsChecked == true, user);
        if (int.TryParse(AutoCaptureCooldownSecondsBox.Text, out var cooldownSeconds))
        {
            cooldownSeconds = Math.Clamp(cooldownSeconds, 1, 30);
            _configStore.SetSettingInt("auto_capture_cooldown_seconds", cooldownSeconds, user);
        }
        if (int.TryParse(ArchiveRetentionYearsBox.Text, out var years) && years > 0 && years <= 100)
            _configStore.SetSettingInt("archive_retention_years", years, user);
        _configStore.SetSettingBool("scheduled_reports_enabled", ScheduledReportsEnabledCheck.IsChecked == true, user);
        _configStore.SetSetting("scheduled_report_type", ScheduledReportTypeCombo.SelectedItem?.ToString() ?? "Performance", user);
        _configStore.SetSetting("scheduled_report_time", ScheduledReportTimeBox.Text?.Trim() ?? "08:00", user);
        _configStore.SetSetting("scheduled_report_output_dir", ScheduledReportOutputDirBox.Text?.Trim() ?? "", user);
        _configStore.SetSetting("scheduled_report_email_recipients", ScheduledReportEmailBox.Text?.Trim() ?? "", user);
        _configStore.SetSetting("smtp_host", SmtpHostBox.Text?.Trim() ?? "", user);
        _configStore.SetSetting("smtp_port", SmtpPortBox.Text?.Trim() ?? "587", user);
        _configStore.SetSetting("smtp_user", SmtpUserBox.Text?.Trim() ?? "", user);
        _configStore.SetSecureSetting("smtp_password", SmtpPasswordBox.Password ?? "", user);
    }

    private void SaveReportLanguageSetting()
    {
        var config = ServiceContainer.GetService<AppConfiguration>();
        var user = config.CurrentUserName ?? "system";
        var langTag = (ReportLanguageCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        _configStore.SetSetting("report_language", langTag ?? "en", user);
    }

    private void SaveOcrSettings()
    {
        if (OcrUseLayoutAssemblyCheck == null) return;
        var config = ServiceContainer.GetService<AppConfiguration>();
        var user = config.CurrentUserName ?? "system";

        _configStore.SetSettingBool("ocr_use_layout_assembly", OcrUseLayoutAssemblyCheck.IsChecked == true, user);

        var psmTag = (OcrPsmCombo?.SelectedItem as ComboBoxItem)?.Tag;
        var psm = psmTag is int pi ? pi : 3;
        _configStore.SetSettingInt("ocr_tesseract_psm", psm, user);

        var ocrLang = (OcrLanguageCombo?.SelectedItem as ComboBoxItem)?.Tag as string ?? "eng";
        _configStore.SetSetting("default_ocr_language", ocrLang, user);

        if (OcrPdfDpiBox != null && int.TryParse(OcrPdfDpiBox.Text, out var dpi) && dpi is >= 150 and <= 400)
            _configStore.SetSettingInt("ocr_pdf_render_dpi", dpi, user);

        _configStore.SetSetting("tesseract_tessdata_path", OcrTessdataPathBox?.Text?.Trim() ?? "", user);

        if (OcrNlMeansHBox != null && int.TryParse(OcrNlMeansHBox.Text, out var h) && h is >= 1 and <= 20)
            _configStore.SetSettingInt("ocr_preprocess_nlmeans_h", h, user);

        _configStore.SetSettingBool("ocr_enable_multipass", OcrEnableMultipassCheck?.IsChecked == true, user);
        _configStore.SetSettingBool("ocr_organize_bilingual_output", OcrOrganizeBilingualCheck?.IsChecked == true, user);
        var mergeMode = (OcrMergeModeCombo?.SelectedItem as ComboBoxItem)?.Tag as string ?? "accuracy";
        _configStore.SetSetting("ocr_multipass_merge_mode", mergeMode, user);
        if (OcrStructuredMinConfidenceBox != null && int.TryParse(OcrStructuredMinConfidenceBox.Text, out var minConf) && minConf is >= 0 and <= 100)
            _configStore.SetSettingInt("ocr_structured_min_confidence", minConf, user);
    }

    private void ResetKpiTargets_Click(object sender, RoutedEventArgs e)
    {
        var kpiService = ServiceContainer.GetService<IKpiService>();
        kpiService.SaveTargets(KpiService.GetDefaultTargetsStatic());
        LoadKpiTargets();
        MessageBox.Show("KPI targets reset to defaults (Clearing Rate 80%, Throughput 50/day, Issue Rate 5%).", "KPI Targets", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    #endregion

    #region Backup Settings

    private void LoadBackupSettings()
    {
        BackupEnabledCheck.IsChecked = _configStore.GetSettingBool("backup_enabled", true);
        BackupIntervalBox.Text = _configStore.GetSettingInt("backup_interval_hours", 24).ToString(CultureInfo.InvariantCulture);
        BackupRetentionBox.Text = _configStore.GetSettingInt("backup_retention_count", 10).ToString(CultureInfo.InvariantCulture);
        BackupIncludeDocsCheck.IsChecked = _configStore.GetSettingBool("backup_include_documents", true);
        BackupIncludeOracleCheck.IsChecked = _configStore.GetSettingBool("include_oracle_data", false);
        OracleDatapumpDirectoryBox.Text = _configStore.GetSettingValue("oracle_datapump_directory", "DATA_PUMP_DIR") ?? "DATA_PUMP_DIR";
        OracleDatapumpLocalFolderBox.Text = _configStore.GetSettingValue("oracle_datapump_local_folder", "") ?? "";
        OracleDumpToolPathBox.Text = _configStore.GetSettingValue("oracle_backup_dump_tool_path", "") ?? "";

        var backups = _backupService.GetBackupHistory();
        var lastBackup = backups.OrderByDescending(b => b.CreatedAt).FirstOrDefault();
        if (LastBackupLabel != null)
            LastBackupLabel.Text = lastBackup != null
                ? $"Last backup: {lastBackup.CreatedAt:yyyy-MM-dd HH:mm}"
                : "No backups found";
    }

    private void SaveBackupSettings()
    {
        var config = ServiceContainer.GetService<AppConfiguration>();
        var user = config.CurrentUserName ?? "system";

        _configStore.SetSettingBool("backup_enabled", BackupEnabledCheck.IsChecked == true, user);
        if (int.TryParse(BackupIntervalBox.Text, out var interval))
            _configStore.SetSettingInt("backup_interval_hours", interval, user);
        if (int.TryParse(BackupRetentionBox.Text, out var retention))
            _configStore.SetSettingInt("backup_retention_count", retention, user);
        _configStore.SetSettingBool("backup_include_documents", BackupIncludeDocsCheck.IsChecked == true, user);
        _configStore.SetSettingBool("include_oracle_data", BackupIncludeOracleCheck.IsChecked == true, user);
        _configStore.SetSetting("oracle_datapump_directory", OracleDatapumpDirectoryBox.Text?.Trim() ?? "DATA_PUMP_DIR", user);
        _configStore.SetSetting("oracle_datapump_local_folder", OracleDatapumpLocalFolderBox.Text?.Trim() ?? "", user);
        _configStore.SetSetting("oracle_backup_dump_tool_path", OracleDumpToolPathBox.Text?.Trim() ?? "", user);
    }

    private async void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BackupNowBtn.IsEnabled = false;
            StatusLabel.Text = "Creating backup...";
            var includeOracle = BackupIncludeOracleCheck.IsChecked == true;
            var result = await _backupService.CreateBackupAsync(
                destinationPath: null,
                includeDocuments: BackupIncludeDocsCheck.IsChecked == true,
                encryptionPassword: null,
                includeOracleData: includeOracle);
            if (result.Success)
            {
                LoadBackupSettings();
                if (result.SkippedFiles.Count > 0)
                {
                    StatusLabel.Text = $"Backup created (some files skipped): {result.BackupPath}";
                    MessageBox.Show(
                        $"Backup created successfully.\n\n{result.SkippedFiles.Count} file(s) could not be included (copy or zip). Check the application log for details.\n\n{result.BackupPath}",
                        "Backup Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    StatusLabel.Text = $"Backup created: {result.BackupPath}";
                    MessageBox.Show($"Backup created successfully!\n\n{result.BackupPath}", "Backup Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                StatusLabel.Text = $"Backup failed: {result.Error}";
                MessageBox.Show($"Backup failed: {result.Error}", "Backup Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Backup failed");
            MessageBox.Show($"Backup failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BackupNowBtn.IsEnabled = true;
        }
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RestoreBackupDialog { Owner = this };
        dlg.ShowDialog();
    }

    #endregion

    #region System Info

    private void LoadSystemInfo()
    {
        var config = ServiceContainer.GetService<AppConfiguration>();

        AppVersionLabel.Text = Defaults.AppVersion;
        DbVersionLabel.Text = _migrationService.GetCurrentVersion().ToString(CultureInfo.InvariantCulture);
        DbPathLabel.Text = "Oracle database";
        BaseDirLabel.Text = config.BaseDirectory;

        BaseDirBox.Text = config.BaseDirectory;
        DbPathBox.Text = config.OracleConnectionString;

        var stats = _documentStore.GetStats();
        TotalDocsLabel.Text = stats.TotalDocuments.ToString("N0", CultureInfo.InvariantCulture);

        var docTypes = _configStore.GetDocumentTypes();
        DocTypesCountSystemLabel.Text = docTypes.Count.ToString(CultureInfo.InvariantCulture);

        var branches = _configStore.GetBranches();
        BranchesCountSystemLabel.Text = branches.Count.ToString(CultureInfo.InvariantCulture);

        var users = _userStore.ListUsers();
        UsersCountLabel.Text = users.Count.ToString(CultureInfo.InvariantCulture);

        DbSizeLabel.Text = "Oracle server (size not shown)";
    }

    private void RefreshSystem_Click(object sender, RoutedEventArgs e)
    {
        LoadSystemInfo();
    }

    private void BrowseBaseDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select base directory for documents",
            SelectedPath = BaseDirBox.Text ?? ""
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
        {
            BaseDirBox.Text = dlg.SelectedPath;
            _isDirty = true;
        }
    }

    private void BrowseDbPath_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "AUDITA now uses Oracle connection strings, not local .db files.\n\n" +
            "Paste a value such as:\n" +
            "User Id=workaudit;Password=***;Data Source=//localhost:1521/FREEPDB1",
            "Oracle Connection",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SavePathSettings()
    {
        var baseDir = BaseDirBox.Text?.Trim() ?? "";
        var oracleConnectionString = DbPathBox.Text?.Trim() ?? "";

        if (!string.IsNullOrEmpty(baseDir))
        {
            UserSettings.Set("base_directory", baseDir);
            Directory.CreateDirectory(baseDir);
        }

        if (!string.IsNullOrEmpty(oracleConnectionString))
            UserSettings.Set("oracle_connection_string", oracleConnectionString);

        // Clear legacy database_path setting if present.
        UserSettings.Set("database_path", "");
    }

    #endregion

    #region Keyboard shortcuts

    private void LoadKeyboardShortcuts()
    {
        _keyboardShortcutService.Reload();
        _keyboardShortcutDraft.Clear();
        _shortcutValueBoxes.Clear();
        foreach (var id in KeyboardShortcutIds.All)
            _keyboardShortcutDraft[id] = _keyboardShortcutService.GetSerializedOrDefault(id)!;

        if (ShortcutsRowsPanel == null) return;
        ShortcutsRowsPanel.Children.Clear();
        foreach (var id in KeyboardShortcutIds.All)
        {
            var labelKey = "ShortcutAction_" + id.Replace(".", "_");
            var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tbLabel = new TextBlock
            {
                Text = ReportLocalizationService.GetString(labelKey, _configStore),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (TryFindResource("PrimaryForeground") is System.Windows.Media.Brush fg)
                tbLabel.Foreground = fg;
            Grid.SetColumn(tbLabel, 0);
            row.Children.Add(tbLabel);

            var tbBox = new System.Windows.Controls.TextBox
            {
                IsReadOnly = true,
                Padding = new Thickness(6, 4, 6, 4),
                Text = _keyboardShortcutService.GetDisplayStringFromSerialized(_keyboardShortcutDraft[id])
            };
            Grid.SetColumn(tbBox, 1);
            row.Children.Add(tbBox);
            _shortcutValueBoxes[id] = tbBox;

            var setBtn = new System.Windows.Controls.Button
            {
                Content = ReportLocalizationService.GetString("ShortcutsSet", _configStore),
                Tag = id,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12, 6, 12, 6),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            setBtn.Click += ShortcutSet_Click;
            Grid.SetColumn(setBtn, 2);
            row.Children.Add(setBtn);

            ShortcutsRowsPanel.Children.Add(row);
        }
    }

    private void ShortcutSet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button b || b.Tag is not string id) return;
        _capturingShortcutId = id;
        StatusLabel.Text = ReportLocalizationService.GetString("ShortcutCapturePress", _configStore);
    }

    private void ResetKeyboardShortcuts_Click(object sender, RoutedEventArgs e)
    {
        foreach (var kv in _keyboardShortcutService.GetDefaultSerializedMap())
            _keyboardShortcutDraft[kv.Key] = kv.Value;
        foreach (var id in KeyboardShortcutIds.All)
        {
            if (_shortcutValueBoxes.TryGetValue(id, out var box))
                box.Text = _keyboardShortcutService.GetDisplayStringFromSerialized(_keyboardShortcutDraft[id]);
        }
        _isDirty = true;
        StatusLabel.Text = ReportLocalizationService.GetString("Ready", _configStore);
    }

    private void ControlPanelWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingShortcutId == null) return;
        if (e.Key == Key.Escape)
        {
            _capturingShortcutId = null;
            StatusLabel.Text = ReportLocalizationService.GetString("Ready", _configStore);
            e.Handled = true;
            return;
        }
        if (IsModifierOnlyKey(e.Key))
            return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierOnlyKey(key))
            return;

        var serialized = KeyboardShortcutService.Serialize(key, Keyboard.Modifiers);
        var id = _capturingShortcutId;
        _capturingShortcutId = null;
        _keyboardShortcutDraft[id] = serialized;
        if (_shortcutValueBoxes.TryGetValue(id, out var box))
            box.Text = _keyboardShortcutService.GetDisplayStringFromSerialized(serialized);
        _isDirty = true;
        StatusLabel.Text = ReportLocalizationService.GetString("Ready", _configStore);
        e.Handled = true;
    }

    private static bool IsModifierOnlyKey(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;

    private void SaveKeyboardShortcuts()
    {
        var err = _keyboardShortcutService.TrySaveOverrides(_keyboardShortcutDraft);
        if (err != null)
            throw new InvalidOperationException(err);
    }

    #endregion

    #region Save & Close

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveThemeSettings();
            SaveReportLanguageSetting();
            SaveKeyboardShortcuts();
            SaveOcrSettings();

            if (_isFullControlPanel)
            {
                SaveSecuritySettings();
                SaveWorkflowSettings();
                SaveBackupSettings();
                SavePathSettings();
            }

            StatusLabel.Text = ReportLocalizationService.GetString("Ready", _configStore);
            _isDirty = false;
            var newLang = (ReportLanguageCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "en";
            if (!string.Equals(newLang, _initialLanguage, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Settings saved successfully!\n\nLanguage changed. Close this dialog to apply.",
                    "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Settings saved successfully!", "Settings Saved",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save settings");
            MessageBox.Show($"Failed to save settings: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_isDirty)
        {
            var result = MessageBox.Show("You have unsaved changes. Save before closing?",
                "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                Save_Click(sender, e);
            }
            else if (result == MessageBoxResult.Cancel)
            {
                return;
            }
        }
        Close();
    }

    #endregion
}
