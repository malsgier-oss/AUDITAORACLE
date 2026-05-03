using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Serilog;
using WorkAudit;
using WorkAudit.Config;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Reports.ReportTemplates;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views;

public partial class ReportsView : UserControl, IDisposable
{
    private static readonly ILogger Log = LoggingService.ForContext<ReportsView>();
    private IDocumentStore? _documentStore;
    private IUserStore? _userStore;
    private IReportService? _reportService;
    private IReportValidationService? _validationService;
    private IReportDraftService? _draftService;
    private ISavedReportConfigService? _savedConfigService;
    private IAuditLogStore? _auditLogStore;
    private IReportAttestationService? _attestationService;
    private IReportAttestationStore? _attestationStore;
    private IReportDistributionService? _distributionService;
    private IReportHistoryStore? _reportHistoryStore;
    private IPermissionService? _permissionService;
    private ReportAttestation? _selectedAttestation;
    private readonly ObservableCollection<ReportHistoryEntry> _reportHistoryAll = new();
    private CancellationTokenSource? _currentGenerationCts;
    private bool _suppressReportScopeSync;

    // Intelligence Dashboard services
    private INotesStore? _notesStore;
    private IIntelligenceService? _intelligenceService;
    private IConfigStore? _configStore;
    private AppConfiguration? _appConfiguration;
    /// <summary>Auditor/Reviewer: full report types but branch locked to home branch.</summary>
    private bool _scopedReportsMode;
    private bool _scopedBranchInvalid;
    private string? _scopedConcreteBranch;

    public ReportsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Dispose();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ServiceContainer.IsInitialized) return;

        _permissionService = ServiceContainer.GetService<IPermissionService>();
        _appConfiguration = ServiceContainer.GetService<AppConfiguration>();
        _scopedReportsMode = !_permissionService.HasMinimumRole(Roles.Manager);

        _documentStore = ServiceContainer.GetService<IDocumentStore>();
        _userStore = ServiceContainer.GetService<IUserStore>();
        _reportService = ServiceContainer.GetService<IReportService>();
        _validationService = ServiceContainer.GetService<IReportValidationService>();
        _draftService = ServiceContainer.GetService<IReportDraftService>();
        _savedConfigService = ServiceContainer.GetService<ISavedReportConfigService>();
        _auditLogStore = ServiceContainer.GetService<IAuditLogStore>();
        _attestationService = ServiceContainer.GetOptionalService<IReportAttestationService>();
        _attestationStore = ServiceContainer.GetOptionalService<IReportAttestationStore>();
        _distributionService = ServiceContainer.GetOptionalService<IReportDistributionService>();
        _reportHistoryStore = ServiceContainer.GetOptionalService<IReportHistoryStore>();

        // Initialize Intelligence Dashboard services
        _notesStore = ServiceContainer.GetService<INotesStore>();
        _intelligenceService = ServiceContainer.GetService<IIntelligenceService>();
        _configStore = ServiceContainer.GetService<IConfigStore>();

        ApplyLocalization();
        ApplyScopedNonManagerVisibility();
        LoadTimeRangeOptions();
        RefreshDashboard();
        LoadPeriodOptions();
        LoadFilterOptions();
        ApplyScopedBranchGateMessage();
        LoadFormatOptions();
        LoadTemplateOptions();
        LoadWatermarkOptions();
        LoadSavedConfigs();
        LoadReportHistory();
        ApplyPeriodPreset(ReportPeriod.Monthly);
        UpdateFavoriteButtonState();
    }

    private void ApplyLocalization()
    {
        var config = ServiceContainer.GetService<IConfigStore>();
        if (ReportsHeader != null)
            ReportsHeader.Text = _scopedReportsMode
                ? ReportLocalizationService.GetString("MyReports", config)
                : ReportLocalizationService.GetString("Reports", config);
        if (ReportsSubtitle != null)
            ReportsSubtitle.Text = _scopedReportsMode
                ? ReportLocalizationService.GetString("AuditorReportsSubtitle", config)
                : "Generate reports and analyze document intelligence";
        if (ReportConfigTitle != null) ReportConfigTitle.Text = ReportLocalizationService.GetString("ReportConfiguration", config);
        if (PeriodLabel != null) PeriodLabel.Text = ReportLocalizationService.GetString("Period", config);
    }

    private void ApplyScopedNonManagerVisibility()
    {
        if (!_scopedReportsMode) return;
        if (RbExecutiveDashboard != null) RbExecutiveDashboard.Visibility = Visibility.Collapsed;
        if (DashboardPanel != null) DashboardPanel.Visibility = Visibility.Collapsed;
        if (SavedConfigsRow != null) SavedConfigsRow.Visibility = Visibility.Collapsed;
        if (AttestationPanel != null) AttestationPanel.Visibility = Visibility.Collapsed;
    }

    private void ApplyScopedBranchGateMessage()
    {
        if (!_scopedReportsMode || !_scopedBranchInvalid) return;
        var raw = _appConfiguration?.CurrentUserBranch?.Trim() ?? "";
        ShowMessage(
            $"Your account does not have a branch assigned (or it is set to '{raw}'). " +
            $"Reports will default to '{_scopedConcreteBranch}', which may not be what you expect. Contact an administrator to set your branch.",
            isError: true);
        SetGenerateButtonsEnabled(false);
        if (CreateDraftBtn != null) CreateDraftBtn.IsEnabled = false;
    }

    private void LoadPeriodOptions()
    {
        PeriodCombo.Items.Clear();
        PeriodCombo.Items.Add(new ComboBoxItem { Content = "Weekly", Tag = ReportPeriod.Weekly });
        PeriodCombo.Items.Add(new ComboBoxItem { Content = "Monthly", Tag = ReportPeriod.Monthly });
        PeriodCombo.Items.Add(new ComboBoxItem { Content = "3 months", Tag = ReportPeriod.Quarter });
        PeriodCombo.Items.Add(new ComboBoxItem { Content = "6 months", Tag = ReportPeriod.HalfYear });
        PeriodCombo.Items.Add(new ComboBoxItem { Content = "Yearly", Tag = ReportPeriod.Yearly });
        PeriodCombo.SelectedIndex = 1;
    }

    private void LoadFormatOptions()
    {
        FormatCombo.Items.Clear();
        FormatCombo.Items.Add(new ComboBoxItem { Content = "PDF", Tag = ReportFormat.Pdf });
        FormatCombo.Items.Add(new ComboBoxItem { Content = "Excel", Tag = ReportFormat.Excel });
        FormatCombo.Items.Add(new ComboBoxItem { Content = "CSV", Tag = ReportFormat.Csv });
        FormatCombo.SelectedIndex = 0;
    }

    private void LoadWatermarkOptions()
    {
        if (WatermarkCombo == null) return;
        WatermarkCombo.Items.Clear();
        WatermarkCombo.Items.Add(new ComboBoxItem { Content = "None", Tag = ReportWatermark.None });
        WatermarkCombo.Items.Add(new ComboBoxItem { Content = "DRAFT", Tag = ReportWatermark.Draft });
        WatermarkCombo.Items.Add(new ComboBoxItem { Content = "CONFIDENTIAL", Tag = ReportWatermark.Confidential });
        WatermarkCombo.SelectedIndex = 0;
    }

    private void LoadTemplateOptions()
    {
        if (TemplateCombo == null) return;
        TemplateCombo.Items.Clear();
        TemplateCombo.Items.Add(ExecutiveTemplate.GetConfig());
        TemplateCombo.Items.Add(BranchManagerTemplate.GetConfig());
        TemplateCombo.Items.Add(AuditorTemplate.GetConfig());
        TemplateCombo.Items.Add(RegulatoryTemplate.GetConfig());
        TemplateCombo.Items.Add(OperationsTemplate.GetConfig());
        TemplateCombo.SelectedIndex = _scopedReportsMode ? 2 : 0;
    }

    private void LoadFilterOptions()
    {
        if (_documentStore == null) return;

        _suppressReportScopeSync = true;
        try
        {
            if (_scopedReportsMode && _appConfiguration != null)
                LoadScopedBranchAndFilters();
            else
                LoadManagerBranchAndFilters();

            CopyComboBoxItemsFromSource(BranchCombo, ToolbarBranchCombo);
            CopyComboBoxItemsFromSource(SectionCombo, ToolbarSectionCombo);
        }
        finally
        {
            _suppressReportScopeSync = false;
        }
    }

    private void LoadManagerBranchAndFilters()
    {
        BranchCombo.Items.Clear();
        BranchCombo.Items.Add("(All)");
        foreach (var b in _documentStore!.GetDistinctBranches())
            BranchCombo.Items.Add(b);
        BranchCombo.SelectedIndex = 0;
        BranchCombo.IsEnabled = true;
        ToolbarBranchCombo.IsEnabled = true;

        SectionCombo.Items.Clear();
        SectionCombo.Items.Add("(All)");
        foreach (var s in Enums.SectionValues)
            SectionCombo.Items.Add(s);
        SectionCombo.SelectedIndex = 0;

        StatusCombo.Items.Clear();
        StatusCombo.Items.Add("(All)");
        foreach (var s in Enums.StatusValues)
            StatusCombo.Items.Add(s);
        StatusCombo.SelectedIndex = 0;

        DocumentTypeCombo.Items.Clear();
        DocumentTypeCombo.Items.Add("(All)");
        foreach (var dt in _documentStore.GetDistinctDocumentTypes(null, null))
            if (!string.IsNullOrEmpty(dt))
                DocumentTypeCombo.Items.Add(dt);
        DocumentTypeCombo.SelectedIndex = 0;

        EngagementCombo.Items.Clear();
        EngagementCombo.Items.Add("(All)");
        foreach (var eng in _documentStore.GetDistinctEngagements(null))
            if (!string.IsNullOrEmpty(eng))
                EngagementCombo.Items.Add(eng);
        EngagementCombo.SelectedIndex = 0;

        UserCombo.Items.Clear();
        UserCombo.Items.Add("(All)");
        if (_userStore != null)
        {
            foreach (var u in _userStore.ListUsers(isActive: true))
                UserCombo.Items.Add(u.Username);
        }
        UserCombo.SelectedIndex = 0;
    }

    private void LoadScopedBranchAndFilters()
    {
        var raw = _appConfiguration!.CurrentUserBranch;
        var resolved = Branches.ToConcreteBranchOrDefault(raw);
        var rawTrimmed = raw?.Trim() ?? "";
        var hasExplicitBranch = !string.IsNullOrWhiteSpace(rawTrimmed)
            && !Branches.ScopesToAllBranches(rawTrimmed)
            && string.Equals(resolved, rawTrimmed, StringComparison.OrdinalIgnoreCase);
        _scopedBranchInvalid = !hasExplicitBranch;
        _scopedConcreteBranch = resolved;

        BranchCombo.Items.Clear();
        BranchCombo.Items.Add(resolved);
        BranchCombo.SelectedIndex = 0;
        BranchCombo.IsEnabled = false;
        ToolbarBranchCombo.IsEnabled = false;

        SectionCombo.Items.Clear();
        SectionCombo.Items.Add("(All)");
        foreach (var s in Enums.SectionValues)
            SectionCombo.Items.Add(s);
        SectionCombo.SelectedIndex = 0;

        StatusCombo.Items.Clear();
        StatusCombo.Items.Add("(All)");
        foreach (var s in Enums.StatusValues)
            StatusCombo.Items.Add(s);
        StatusCombo.SelectedIndex = 0;

        DocumentTypeCombo.Items.Clear();
        DocumentTypeCombo.Items.Add("(All)");
        foreach (var dt in _documentStore!.GetDistinctDocumentTypes(resolved, null))
            if (!string.IsNullOrEmpty(dt))
                DocumentTypeCombo.Items.Add(dt);
        DocumentTypeCombo.SelectedIndex = 0;

        EngagementCombo.Items.Clear();
        EngagementCombo.Items.Add("(All)");
        foreach (var eng in _documentStore.GetDistinctEngagements(resolved))
            if (!string.IsNullOrEmpty(eng))
                EngagementCombo.Items.Add(eng);
        EngagementCombo.SelectedIndex = 0;

        UserCombo.Items.Clear();
        UserCombo.Items.Add("(All)");
        var me = _appConfiguration.CurrentUserName?.Trim();
        if (!string.IsNullOrEmpty(me))
            UserCombo.Items.Add(me);
        UserCombo.SelectedIndex = 0;
    }

    private static void CopyComboBoxItemsFromSource(System.Windows.Controls.ComboBox source, System.Windows.Controls.ComboBox target)
    {
        target.Items.Clear();
        foreach (var it in source.Items)
            target.Items.Add(it);
        target.SelectedIndex = source.SelectedIndex >= 0 && source.SelectedIndex < target.Items.Count
            ? source.SelectedIndex
            : 0;
    }

    private static void MatchComboSelection(System.Windows.Controls.ComboBox target, System.Windows.Controls.ComboBox source)
    {
        var want = source.SelectedItem?.ToString();
        if (want == null)
        {
            target.SelectedIndex = 0;
            return;
        }
        foreach (var it in target.Items)
        {
            if (string.Equals(it.ToString(), want, StringComparison.Ordinal))
            {
                target.SelectedItem = it;
                return;
            }
        }
        target.SelectedIndex = 0;
    }

    private void ToolbarScopeFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressReportScopeSync || !IsLoaded)
            return;
        _suppressReportScopeSync = true;
        try
        {
            MatchComboSelection(BranchCombo, ToolbarBranchCombo);
            MatchComboSelection(SectionCombo, ToolbarSectionCombo);
        }
        finally
        {
            _suppressReportScopeSync = false;
        }
        RefreshDashboard();
    }

    private void ReportScopeFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressReportScopeSync || !IsLoaded)
            return;
        _suppressReportScopeSync = true;
        try
        {
            MatchComboSelection(ToolbarBranchCombo, BranchCombo);
            MatchComboSelection(ToolbarSectionCombo, SectionCombo);
        }
        finally
        {
            _suppressReportScopeSync = false;
        }
        RefreshDashboard();
    }

    private static List<Document> ApplyReportScopeFilter(IReadOnlyList<Document> docs, string? branchItem, string? sectionItem)
    {
        const string allLabel = "(All)";
        var allBranch = string.IsNullOrEmpty(branchItem) || string.Equals(branchItem, allLabel, StringComparison.Ordinal);
        var allSection = string.IsNullOrEmpty(sectionItem) || string.Equals(sectionItem, allLabel, StringComparison.Ordinal);
        IEnumerable<Document> q = docs;
        if (!allBranch)
        {
            q = q.Where(d => !string.IsNullOrWhiteSpace(d.Branch) &&
                             string.Equals(d.Branch!.Trim(), branchItem!.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        if (!allSection)
        {
            q = q.Where(d => !string.IsNullOrWhiteSpace(d.Section) &&
                             string.Equals(d.Section!.Trim(), sectionItem!.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        return q.ToList();
    }

    private void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeriodCombo.SelectedItem is ComboBoxItem item && item.Tag is ReportPeriod preset)
            ApplyPeriodPreset(preset);
    }

    private void ApplyPeriodPreset(ReportPeriod preset)
    {
        if (_reportService == null) return;
        var (from, to) = _reportService.GetDateRangeFromPreset(preset);
        DateFromPicker.SelectedDate = from;
        DateToPicker.SelectedDate = to;
    }

    private ReportConfig GetConfig()
    {
        var config = new ReportConfig
        {
            DateFrom = DateFromPicker.SelectedDate ?? DateTime.Today.AddMonths(-1),
            DateTo = DateToPicker.SelectedDate ?? DateTime.Today,
            Preset = ((PeriodCombo.SelectedItem as ComboBoxItem)?.Tag as ReportPeriod?) ?? ReportPeriod.Monthly,
            Format = ((FormatCombo.SelectedItem as ComboBoxItem)?.Tag as ReportFormat?) ?? ReportFormat.Pdf,
            Language = (ReportLanguageCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en",
            IncludeCharts = IncludeChartsCheck?.IsChecked ?? true,
            IncludeTableOfContents = IncludeTocCheck?.IsChecked ?? true,
            IncludeBranding = IncludeBrandingCheck?.IsChecked ?? true,
            IncludeDisclaimer = IncludeDisclaimerCheck?.IsChecked ?? true
        };

        if (_scopedReportsMode && !string.IsNullOrEmpty(_scopedConcreteBranch))
            config.Branch = _scopedConcreteBranch;
        else if (BranchCombo.SelectedItem is string branch && branch != "(All)")
            config.Branch = branch;
        if (SectionCombo.SelectedItem is string section && section != "(All)") config.Section = section;
        if (StatusCombo.SelectedItem is string status && status != "(All)") config.Status = status;
        if (DocumentTypeCombo.SelectedItem is string dt && dt != "(All)") config.DocumentType = dt;
        if (EngagementCombo?.SelectedItem is string eng && eng != "(All)") config.Engagement = eng;
        if (UserCombo.SelectedItem is string user && user != "(All)") config.UserFilter = user;

        config.ReportTemplate = (TemplateCombo?.SelectedItem as ReportTemplateConfig)?.Name switch
        {
            "Branch Manager" => ReportTemplate.BranchManager,
            "Auditor" => ReportTemplate.Auditor,
            "Regulatory" => ReportTemplate.Regulatory,
            "Operations" => ReportTemplate.Operations,
            _ => ReportTemplate.Executive
        };
        config.Watermark = (WatermarkCombo?.SelectedItem as ComboBoxItem)?.Tag as ReportWatermark? ?? ReportWatermark.None;
        config.ExportPerBranch = ExportPerBranchCheck?.IsChecked ?? false;
        config.ZipPerBranch = ZipPerBranchCheck?.IsChecked ?? false;
        config.ReportType = RbDailySummary?.IsChecked == true ? ReportType.DailySummary
            : RbBranchSummary?.IsChecked == true ? ReportType.BranchSummary
            : RbSectionSummary?.IsChecked == true ? ReportType.SectionSummary
            : RbStatusSummary?.IsChecked == true ? ReportType.StatusSummary
            : RbDocumentType?.IsChecked == true ? ReportType.DocumentTypeSummary
            : RbPerformance?.IsChecked == true ? ReportType.Performance
            : RbIssuesFocus?.IsChecked == true ? ReportType.IssuesAndFocus
            : RbUserActivity?.IsChecked == true ? ReportType.UserActivity
            : RbAssignmentSummary?.IsChecked == true ? ReportType.AssignmentSummary
            : RbAuditTrail?.IsChecked == true ? ReportType.AuditTrail
            : RbExecutiveSummary?.IsChecked == true || RbExecutiveDashboard?.IsChecked == true ? ReportType.ExecutiveSummary
            : ReportType.BranchSummary;

        return config;
    }

    private void LoadSavedConfigs()
    {
        if (_scopedReportsMode) return;
        if (_savedConfigService == null) return;
        SavedConfigCombo.Items.Clear();
        SavedConfigCombo.Items.Add(new SavedConfigComboItem { Name = "(Select saved config)", DisplayName = "(Select saved config)", Config = null });
        foreach (var c in _savedConfigService.List())
            SavedConfigCombo.Items.Add(new SavedConfigComboItem { Name = c.Name, DisplayName = (c.IsFavorite ? "★ " : "") + c.Name, Config = c });
        SavedConfigCombo.SelectedIndex = 0;
    }

    private void ApplyConfig(SavedReportConfig cfg)
    {
        _suppressReportScopeSync = true;
        try
        {
            var c = cfg.Config;
            DateFromPicker.SelectedDate = c.DateFrom;
            DateToPicker.SelectedDate = c.DateTo;
            PeriodCombo.SelectedItem = PeriodCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as ReportPeriod?) == c.Preset) ?? PeriodCombo.Items[1];
            FormatCombo.SelectedItem = FormatCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as ReportFormat?) == c.Format) ?? FormatCombo.Items[0];
            IncludeChartsCheck.IsChecked = c.IncludeCharts;
            if (IncludeTocCheck != null) IncludeTocCheck.IsChecked = c.IncludeTableOfContents;
            if (IncludeBrandingCheck != null) IncludeBrandingCheck.IsChecked = c.IncludeBranding;
            if (IncludeDisclaimerCheck != null) IncludeDisclaimerCheck.IsChecked = c.IncludeDisclaimer;
            if (!string.IsNullOrEmpty(c.Branch)) { BranchCombo.SelectedItem = c.Branch; } else { BranchCombo.SelectedIndex = 0; }
            if (!string.IsNullOrEmpty(c.Section)) { SectionCombo.SelectedItem = c.Section; } else { SectionCombo.SelectedIndex = 0; }
            if (!string.IsNullOrEmpty(c.Status)) { StatusCombo.SelectedItem = c.Status; } else { StatusCombo.SelectedIndex = 0; }
            if (!string.IsNullOrEmpty(c.DocumentType)) { DocumentTypeCombo.SelectedItem = c.DocumentType; } else { DocumentTypeCombo.SelectedIndex = 0; }
            if (EngagementCombo != null) { if (!string.IsNullOrEmpty(c.Engagement)) { EngagementCombo.SelectedItem = c.Engagement; } else { EngagementCombo.SelectedIndex = 0; } }
            if (!string.IsNullOrEmpty(c.UserFilter)) { UserCombo.SelectedItem = c.UserFilter; } else { UserCombo.SelectedIndex = 0; }
            if (WatermarkCombo != null)
                WatermarkCombo.SelectedItem = WatermarkCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as ReportWatermark?) == c.Watermark) ?? WatermarkCombo.Items[0];
            if (ExportPerBranchCheck != null) ExportPerBranchCheck.IsChecked = c.ExportPerBranch;
            if (ZipPerBranchCheck != null) ZipPerBranchCheck.IsChecked = c.ZipPerBranch;
            RbDailySummary.IsChecked = c.ReportType == ReportType.DailySummary;
            RbBranchSummary.IsChecked = c.ReportType == ReportType.BranchSummary;
            RbSectionSummary.IsChecked = c.ReportType == ReportType.SectionSummary;
            RbStatusSummary.IsChecked = c.ReportType == ReportType.StatusSummary;
            RbDocumentType.IsChecked = c.ReportType == ReportType.DocumentTypeSummary;
            RbPerformance.IsChecked = c.ReportType == ReportType.Performance;
            RbIssuesFocus.IsChecked = c.ReportType == ReportType.IssuesAndFocus;
            RbUserActivity.IsChecked = c.ReportType == ReportType.UserActivity;
            RbAssignmentSummary.IsChecked = c.ReportType == ReportType.AssignmentSummary;
            RbAuditTrail.IsChecked = c.ReportType == ReportType.AuditTrail;
            RbExecutiveSummary.IsChecked = c.ReportType == ReportType.ExecutiveSummary;
            var templateIdx = c.ReportTemplate switch
            {
                ReportTemplate.BranchManager => 1,
                ReportTemplate.Auditor => 2,
                ReportTemplate.Regulatory => 3,
                ReportTemplate.Operations => 4,
                _ => 0
            };
            if (TemplateCombo != null && templateIdx < TemplateCombo.Items.Count)
                TemplateCombo.SelectedIndex = templateIdx;
        }
        finally
        {
            MatchComboSelection(ToolbarBranchCombo, BranchCombo);
            MatchComboSelection(ToolbarSectionCombo, SectionCombo);
            _suppressReportScopeSync = false;
        }

        if (IsLoaded)
            RefreshDashboard();
    }

    private void SavedConfigCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedConfigCombo.SelectedItem is SavedConfigComboItem item && item.Config != null)
            ApplyConfig(item.Config);
        UpdateFavoriteButtonState();
    }

    private void UpdateFavoriteButtonState()
    {
        if (_scopedReportsMode) return;
        if (FavoriteConfigBtn == null) return;
        var isFav = SavedConfigCombo.SelectedItem is SavedConfigComboItem item && item.Config?.IsFavorite == true;
        FavoriteConfigBtn.Content = isFav ? "★" : "☆";
        FavoriteConfigBtn.Background = isFav ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7)) : System.Windows.Media.Brushes.LightGray;
        FavoriteConfigBtn.IsEnabled = SavedConfigCombo.SelectedItem is SavedConfigComboItem i && i.Config != null;
    }

    private void FavoriteConfigBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SavedConfigCombo.SelectedItem is not SavedConfigComboItem item || item.Config == null || _savedConfigService == null) return;
        var configId = item.Config.Id;
        item.Config.IsFavorite = !item.Config.IsFavorite;
        _savedConfigService.Save(item.Config);
        LoadSavedConfigs();
        var toSelect = SavedConfigCombo.Items.Cast<SavedConfigComboItem>().FirstOrDefault(c => c.Config?.Id == configId);
        if (toSelect != null) SavedConfigCombo.SelectedItem = toSelect;
        UpdateFavoriteButtonState();
        ShowMessage(item.Config.IsFavorite ? $"Added to favorites: {item.Config.Name}" : $"Removed from favorites: {item.Config.Name}", isError: false);
    }

    private void SaveConfigBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_savedConfigService == null) return;
        var dlg = new Dialogs.SaveReportConfigDialog();
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() != true) return;
        var saved = new SavedReportConfig { Name = dlg.ConfigName, Config = GetConfig() };
        _savedConfigService.Save(saved);
        LoadSavedConfigs();
        ShowMessage($"Saved: {saved.Name}", isError: false);
    }

    private void ManageConfigsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_savedConfigService == null) return;
        var dlg = new Dialogs.ManageSavedConfigsDialog(_savedConfigService);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true && dlg.LoadRequested && dlg.SelectedConfig != null)
        {
            ApplyConfig(dlg.SelectedConfig);
            LoadSavedConfigs();
            ShowMessage($"Loaded: {dlg.SelectedConfig.Name}", isError: false);
        }
    }

    private void LoadReportHistory()
    {
        _reportHistoryAll.Clear();
        if (_reportHistoryStore != null)
        {
            var from = DateTime.UtcNow.AddDays(-30);
            var to = DateTime.UtcNow;
            // Manager+: global history. Auditor/Reviewer: only their own generations.
            var history = _reportHistoryStore.List(from, to, 50, userId: _scopedReportsMode ? GetCurrentUserId() : null);
            if (history.Count > 0)
            {
                foreach (var h in history)
                {
                    var ts = DateTime.TryParse(h.GeneratedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : h.GeneratedAt;
                    _reportHistoryAll.Add(new ReportHistoryEntry(ts, h.ReportType, h.FilePath));
                }
                ApplyReportHistoryFilter();
                return;
            }
        }
        if (_auditLogStore == null) return;
        var entries = _auditLogStore.Query(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, null, Domain.AuditAction.ReportGenerated, Domain.AuditCategory.Report, false, 50, 0);
        foreach (var e in entries.Where(x => x.Success && !string.IsNullOrEmpty(x.NewValue)))
        {
            var ts = DateTime.TryParse(e.Timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : e.Timestamp;
            var reportType = "Report";
            if (!string.IsNullOrEmpty(e.Details) && e.Details.StartsWith("Report type: "))
                reportType = e.Details.Split(',')[0].Replace("Report type: ", "").Trim();
            _reportHistoryAll.Add(new ReportHistoryEntry(ts, reportType, e.NewValue!));
        }
        ApplyReportHistoryFilter();
    }

    private void ReportHistoryFilter_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyReportHistoryFilter();
    }

    private void ApplyReportHistoryFilter()
    {
        var filter = ReportHistoryFilter?.Text?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(filter))
            ReportHistoryList.ItemsSource = _reportHistoryAll;
        else
            ReportHistoryList.ItemsSource = _reportHistoryAll.Where(h => h.DisplayText.ToLowerInvariant().Contains(filter) || h.ReportType.ToLowerInvariant().Contains(filter)).ToList();
    }

    private void ReportHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReportHistoryList.SelectedItem is not ReportHistoryEntry entry) return;

        if (File.Exists(entry.FilePath))
        {
            try
            {
                _distributionService?.LogView(entry.FilePath, entry.ReportType, GetCurrentUserId(), GetCurrentUsername());
                Process.Start(new ProcessStartInfo(entry.FilePath) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Debug(ex, "Could not open file: {Path}", entry.FilePath); }
        }

        RefreshAttestationPanel(entry.FilePath, entry.ReportType);
    }

    private sealed record ReportHistoryEntry(string Timestamp, string ReportType, string FilePath)
    {
        public string DisplayText => $"{Timestamp} — {ReportType}";
    }

    private sealed class SavedConfigComboItem { public string Name { get; set; } = ""; public string DisplayName { get; set; } = ""; public SavedReportConfig? Config { get; set; } }

    private async void GeneratePdfBtn_Click(object sender, RoutedEventArgs e)
    {
        await GenerateReportAsync(ReportFormat.Pdf);
    }

    private async void GenerateExcelBtn_Click(object sender, RoutedEventArgs e)
    {
        await GenerateReportAsync(ReportFormat.Excel);
    }

    private async void GenerateCsvBtn_Click(object sender, RoutedEventArgs e)
    {
        await GenerateReportAsync(ReportFormat.Csv);
    }

    private async System.Threading.Tasks.Task GenerateReportAsync(ReportFormat format)
    {
        if (_reportService == null || _validationService == null)
        {
            ShowMessage("Reports service not available.", isError: true);
            return;
        }

        if (_scopedReportsMode && _scopedBranchInvalid)
        {
            ShowMessage("Fix your branch assignment before generating reports.", isError: true);
            return;
        }

        var config = GetConfig();
        config.Format = format;

        // Validate configuration before generating
        var validation = _validationService.ValidateConfig(config);
        if (!validation.IsValid)
        {
            ShowMessage($"Validation failed:\n{validation.GetErrorMessage()}", isError: true);
            return;
        }

        // Show warnings if any
        if (validation.HasWarnings)
        {
            var warningResult = MessageBox.Show(
                $"Warning:\n{validation.GetWarningMessage()}\n\nContinue anyway?",
                "Validation Warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            
            if (warningResult != MessageBoxResult.Yes)
                return;
        }

        // Check document count
        try
        {
            var docCount = await _validationService.GetDocumentCountAsync(config);
            if (docCount == 0)
            {
                var result = MessageBox.Show(
                    "No documents found matching the selected criteria.\n\n" +
                    "Try adjusting your filters or date range.\n\n" +
                    "Generate empty report anyway?",
                    "No Data",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result != MessageBoxResult.Yes)
                    return;
            }
            else if (docCount < 0)
            {
                // Validation service returns -1 when its own preview query throws; surface that
                // instead of swallowing it. The actual generator may still succeed, but the user
                // deserves to know that the preview did not run.
                var result = MessageBox.Show(
                    "Could not preview the document count for this report (the validation query failed).\n\n" +
                    "The report itself may still generate. Continue anyway?",
                    "Preview Unavailable",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }
            else
            {
                ShowMessage($"Preview: This report will include ~{docCount:N0} documents", isError: false);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Document count preview failed for {ReportType}", config.ReportType);
        }

        // Create cancellation token source
        _currentGenerationCts = new CancellationTokenSource();
        var ct = _currentGenerationCts.Token;

        SetBusy(true);
        ShowMessage("Starting report generation...");

        var startTime = DateTime.UtcNow;

        try
        {
            // Create progress reporter
            var progress = new Progress<ReportProgress>(p =>
            {
                Dispatcher.Invoke(() =>
                {
                    ShowMessage(p.GetDisplayText(), isError: false);
                    
                    // Update progress bar if determinate
                    if (ProgressBar != null && p.PercentComplete > 0)
                    {
                        ProgressBar.IsIndeterminate = false;
                        ProgressBar.Value = p.PercentComplete;
                    }
                });
            });

            var path = await _reportService.GenerateAsync(config, progress, ct);

            if (string.IsNullOrEmpty(path))
            {
                ShowMessage("Report generation failed.", isError: true);
                return;
            }

            var elapsed = DateTime.UtcNow - startTime;
            ShowMessage($"Report generated in {elapsed.TotalSeconds:F1}s: {Path.GetFileName(path)}", isError: false);

            if (format is ReportFormat.Excel or ReportFormat.Csv)
                _distributionService?.LogExport(path, config.ReportType.ToString(), GetCurrentUserId(), GetCurrentUsername(), format.ToString());

            // Ask to open or save
            var result = MessageBox.Show(
                $"Report generated successfully.\n\n{path}\n\nOpen file?",
                "Report Generated",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes && File.Exists(path))
            {
                try
                {
                    _distributionService?.LogView(path, config.ReportType.ToString(), GetCurrentUserId(), GetCurrentUsername());
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch
                {
                    // Ignore if we can't open (e.g. no default app)
                }
            }

            if (config.Format == ReportFormat.Pdf)
                RefreshAttestationPanel(path, config.ReportType.ToString());

            _ = Dispatcher.BeginInvoke(new Action(LoadReportHistory), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (OperationCanceledException)
        {
            ShowMessage("Report generation cancelled.", isError: false);
            Log.Information("Report generation cancelled by user");
        }
        catch (ArgumentException ex)
        {
            ShowMessage(ex.Message, isError: true);
        }
        catch (NotImplementedException ex)
        {
            ShowMessage(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            ShowMessage($"Error: {ex.Message}", isError: true);
            Log.Error(ex, "Report generation error");
        }
        finally
        {
            _currentGenerationCts?.Dispose();
            _currentGenerationCts = null;
            SetBusy(false);
        }
    }

    private void CancelGeneration_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGenerationCts != null && !_currentGenerationCts.IsCancellationRequested)
        {
            _currentGenerationCts.Cancel();
            ShowMessage("Cancelling report generation...", isError: false);
        }
    }

    private void CreateDraftBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_scopedReportsMode && _scopedBranchInvalid)
        {
            ShowMessage("Fix your branch assignment before creating drafts.", isError: true);
            return;
        }

        if (_draftService == null)
        {
            ShowMessage("Draft service not available.", isError: true);
            return;
        }

        var config = GetConfig();

        // Validate configuration
        if (_validationService != null)
        {
            var validation = _validationService.ValidateConfig(config);
            if (!validation.IsValid)
            {
                ShowMessage($"Validation failed: {validation.GetErrorMessage()}", isError: true);
                return;
            }
        }

        try
        {
            var draft = _draftService.CreateDraft(config, GetCurrentUserId(), GetCurrentUsername());
            
            ShowMessage($"Draft created: {draft.Uuid}", isError: false);
            Log.Information("Draft {DraftId} created for report type {ReportType}", draft.Id, config.ReportType);

            // Open the editor (navigate to editor view if exists, or show message)
            var result = MessageBox.Show(
                $"Draft created successfully.\n\nWould you like to open it in the editor now?",
                "Draft Created",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes && Window.GetWindow(this) is MainWindow mw)
                mw.NavigateToReportEditor(draft.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create draft");
            ShowMessage($"Error creating draft: {ex.Message}", isError: true);
        }
    }

    private void SetBusy(bool busy)
    {
        var allowInput = !busy && !(_scopedReportsMode && _scopedBranchInvalid);
        GeneratePdfBtn.IsEnabled = allowInput;
        GenerateExcelBtn.IsEnabled = allowInput;
        GenerateCsvBtn.IsEnabled = allowInput;
        if (CreateDraftBtn != null)
            CreateDraftBtn.IsEnabled = allowInput;

        if (CancelGenerationBtn != null)
            CancelGenerationBtn.IsEnabled = busy;
        
        if (ProgressBar != null)
        {
            ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ProgressBar.IsIndeterminate = true; // Start as indeterminate
            ProgressBar.Value = 0;
        }
    }

    private string GetCurrentUserId() => ServiceContainer.GetService<AppConfiguration>().CurrentUserId ?? "";
    private string GetCurrentUsername() => ServiceContainer.GetService<AppConfiguration>().CurrentUserName ?? "";

    private void RefreshAttestationPanel(string reportPath, string reportType)
    {
        _selectedAttestation = null;
        AttestationPanel.Visibility = Visibility.Collapsed;
        if (_scopedReportsMode) return;
        if (_attestationStore == null || string.IsNullOrEmpty(reportPath)) return;

        var a = _attestationStore.GetByReportPath(reportPath);
        if (a == null) return;

        _selectedAttestation = a;
        AttestationPanel.Visibility = Visibility.Visible;
        AttestationStatusText.Text = $"Status: {a.Status}  |  Generated: {a.GeneratedAt} by {a.GeneratedByUsername ?? "-"}" +
            (string.IsNullOrEmpty(a.ReviewedAt) ? "" : $"  |  Reviewed: {a.ReviewedAt} by {a.ReviewedByUsername ?? "-"}") +
            (string.IsNullOrEmpty(a.ApprovedAt) ? "" : $"  |  Approved: {a.ApprovedAt} by {a.ApprovedByUsername ?? "-"}");

        var canReviewOrApprove = _permissionService?.HasMinimumRole(Roles.Manager) ?? false;
        AttestationButtonsPanel.Visibility = canReviewOrApprove && a.Status != AttestationStatus.Approved ? Visibility.Visible : Visibility.Collapsed;
        ReviewBtn.Visibility = a.Status == AttestationStatus.Generated ? Visibility.Visible : Visibility.Collapsed;
        ApproveBtn.Visibility = (a.Status == AttestationStatus.Generated || a.Status == AttestationStatus.Reviewed) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReviewBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAttestation == null || _attestationService == null) return;
        _attestationService.MarkReviewed(_selectedAttestation.Id, GetCurrentUserId(), GetCurrentUsername());
        _selectedAttestation = _attestationStore?.GetById(_selectedAttestation.Id) ?? _selectedAttestation;
        RefreshAttestationPanel(_selectedAttestation.ReportPath, _selectedAttestation.ReportType);
        ShowMessage("Report marked as Reviewed.", isError: false);
    }

    private void RbExecutiveDashboard_Checked(object sender, RoutedEventArgs e)
    {
        DashboardPanel.Visibility = Visibility.Visible;
        ExecutiveDashboardControl.SetPeriod(DateFromPicker.SelectedDate ?? DateTime.Today.AddMonths(-1), DateToPicker.SelectedDate ?? DateTime.Today);
        // The "Executive Dashboard" radio is a live-view toggle, not a generatable report — the
        // GetConfig() switch maps it to ExecutiveSummary, which would mislead users who expected the
        // dashboard widgets to come out of the PDF. Disable the export buttons instead.
        SetGenerateButtonsEnabled(false);
        ShowMessage("Executive Dashboard is a live view. Pick a different report type to enable PDF / Excel / CSV export.", isError: false);
    }

    private void RbExecutiveDashboard_Unchecked(object sender, RoutedEventArgs e)
    {
        DashboardPanel.Visibility = Visibility.Collapsed;
        SetGenerateButtonsEnabled(true);
    }

    private void SetGenerateButtonsEnabled(bool enabled)
    {
        var effective = enabled && !(_scopedReportsMode && _scopedBranchInvalid);
        if (GeneratePdfBtn != null) GeneratePdfBtn.IsEnabled = effective;
        if (GenerateExcelBtn != null) GenerateExcelBtn.IsEnabled = effective;
        if (GenerateCsvBtn != null) GenerateCsvBtn.IsEnabled = effective;
    }

    private void DateRange_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (RbExecutiveDashboard?.IsChecked == true && DashboardPanel?.Visibility == Visibility.Visible)
            ExecutiveDashboardControl?.SetPeriod(DateFromPicker.SelectedDate ?? DateTime.Today.AddMonths(-1), DateToPicker.SelectedDate ?? DateTime.Today);
    }

    private void ApproveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAttestation == null || _attestationService == null) return;
        _attestationService.MarkApproved(_selectedAttestation.Id, GetCurrentUserId(), GetCurrentUsername());
        _selectedAttestation = _attestationStore?.GetById(_selectedAttestation.Id) ?? _selectedAttestation;
        RefreshAttestationPanel(_selectedAttestation.ReportPath, _selectedAttestation.ReportType);
        ShowMessage("Report approved.", isError: false);
    }

    private void ShowMessage(string message, bool isError = false)
    {
        if (MessageText != null)
        {
            MessageText.Text = message;
            MessageText.Foreground = isError
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69));
        }
        if (StatusText != null)
            StatusText.Text = message;
    }

    #region Intelligence Dashboard

    private void LoadTimeRangeOptions()
    {
        if (TimeRangeCombo == null) return;
        
        TimeRangeCombo.Items.Clear();
        TimeRangeCombo.Items.Add("Today");
        TimeRangeCombo.Items.Add("This Week");
        TimeRangeCombo.Items.Add("This Month");
        TimeRangeCombo.Items.Add("Last 30 Days");
        TimeRangeCombo.Items.Add("All Time");
        TimeRangeCombo.SelectedIndex = 2; // Default to "This Month"
    }

    private void TimeRangeCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) RefreshDashboard();
    }

    private (DateTime start, DateTime end) GetDateRangeFromTimeSelector()
    {
        var selected = TimeRangeCombo?.SelectedItem?.ToString() ?? "This Month";
        var now = DateTime.Now;

        return selected switch
        {
            "Today" => (now.Date, now.Date.AddDays(1)),
            "This Week" => (now.Date.AddDays(-(int)now.DayOfWeek), now.Date.AddDays(7 - (int)now.DayOfWeek)),
            "This Month" => (new DateTime(now.Year, now.Month, 1), new DateTime(now.Year, now.Month, 1).AddMonths(1)),
            "Last 30 Days" => (now.AddDays(-30), now),
            "All Time" => (new DateTime(2000, 1, 1), now), // Use reasonable start date instead of DateTime.MinValue
            _ => (new DateTime(now.Year, now.Month, 1), new DateTime(now.Year, now.Month, 1).AddMonths(1))
        };
    }

    private void RefreshDashboardBtn_Click(object sender, RoutedEventArgs e)
    {
        RefreshDashboard();
    }

    private void RefreshDashboard()
    {
        if (_documentStore == null || _intelligenceService == null) return;

        try
        {
            // Get date range from time range selector
            var (dateFrom, dateTo) = GetDateRangeFromTimeSelector();

            // Load documents for the period
            var docs = _documentStore.ListDocuments(
                dateFrom: dateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                dateTo: dateTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59",
                limit: 10000
            );

            var branchScope = BranchCombo.SelectedItem?.ToString();
            var sectionScope = SectionCombo.SelectedItem?.ToString();
            docs = ApplyReportScopeFilter(docs, branchScope, sectionScope);

            // Generate executive summary (English only). Passing the actual issue documents to
            // GenerateRecommendations matches what ExecutiveSummaryReport does, so the dashboard's
            // recommendations escalate when there are real outstanding issues instead of always
            // returning the generic "Maintain current compliance level" message.
            var issueDocs = docs.Where(d => d.Status == Enums.Status.Issue).Cast<object>().ToList();
            var summary = _intelligenceService.GenerateExecutiveSummary(docs, issueDocs, "en");
            if (AutoSummaryText != null)
                AutoSummaryText.Text = summary;

            var recommendations = _intelligenceService.GenerateRecommendations(issueDocs, "en");
            if (RecommendationsList != null)
                RecommendationsList.ItemsSource = recommendations;

            // Populate KPI cards
            PopulateKpiCards(docs);
        }
        catch (Exception ex)
        {
            ShowMessage($"Error refreshing dashboard: {ex.Message}", isError: true);
        }
    }

    private int _kpiCardIndex; // Track which column to add KPI card to

    private void PopulateKpiCards(List<Document> docs)
    {
        if (KpiCardsContainer == null) return;

        KpiCardsContainer.Children.Clear();
        _kpiCardIndex = 0; // Reset counter

        var total = docs.Count;
        var cleared = docs.Count(d => d.Status == Enums.Status.Cleared);
        var issues = docs.Count(d => d.Status == Enums.Status.Issue);
        var active = docs.Count(d => d.Status != Enums.Status.Archived);
        var clearingRate = active > 0 ? (decimal)cleared / active * 100 : 0;

        // Get total note count
        var docIds = docs.Select(d => d.Id).ToList();
        var noteCounts = _notesStore?.GetCountsByDocuments(docIds) ?? new Dictionary<int, int>();
        var totalNotes = noteCounts.Values.Sum();

        // Create KPI cards with colorful values matching Dashboard
        AddKpiCard("📁", "Total Files", total.ToString("N0", CultureInfo.InvariantCulture), "#0E639C");       // Blue
        AddKpiCard("⚠️", "Critical Issues", issues.ToString("N0", CultureInfo.InvariantCulture), "#DC3545");  // Red
        AddKpiCard("✅", "Compliance Rate", $"{clearingRate:F1}%", "#28A745");  // Green
        AddKpiCard("📝", "Total Notes", totalNotes.ToString("N0", CultureInfo.InvariantCulture), "#6C757D");  // Gray
        AddKpiCard("📊", "Active", active.ToString("N0", CultureInfo.InvariantCulture), "#17A2B8");            // Teal
    }

    private void AddKpiCard(string icon, string label, string value, string colorHex)
    {
        if (KpiCardsContainer == null) return;

        var card = new Border
        {
            Height = 110,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1)
        };

        // Try to use dynamic resources
        card.Background = TryFindResource("PanelBackground") as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.White;
        card.BorderBrush = TryFindResource("BorderBrush") as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.LightGray;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Icon
        var iconText = new TextBlock
        {
            Text = icon,
            FontSize = 24,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        Grid.SetRow(iconText, 0);
        grid.Children.Add(iconText);

        // Value (with colorful styling)
        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        // Apply colorful foreground
        try
        {
            var brush = new System.Windows.Media.BrushConverter().ConvertFromString(colorHex) as System.Windows.Media.Brush;
            valueText.Foreground = brush ?? System.Windows.Media.Brushes.Black;
        }
        catch
        {
            valueText.Foreground = TryFindResource("PrimaryForeground") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Black;
        }
        Grid.SetRow(valueText, 1);
        grid.Children.Add(valueText);

        // Label
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 11,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        labelText.Foreground = TryFindResource("SecondaryForeground") as System.Windows.Media.Brush
            ?? new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(108, 117, 125)); // Gray fallback
        Grid.SetRow(labelText, 2);
        grid.Children.Add(labelText);

        card.Child = grid;
        
        // Set Grid column (0, 2, 4, 6, 8 - odd columns are spacing)
        Grid.SetColumn(card, _kpiCardIndex * 2);
        _kpiCardIndex++;
        
        KpiCardsContainer.Children.Add(card);
    }

    #endregion

    public void Dispose()
    {
        _currentGenerationCts?.Cancel();
        _currentGenerationCts?.Dispose();
        _currentGenerationCts = null;
        Unloaded -= OnUnloaded;
        GC.SuppressFinalize(this);
    }
}
