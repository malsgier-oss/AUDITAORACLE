using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Serilog;
using WorkAudit.Core;
using WorkAudit.Core.Compliance;
using WorkAudit.Core.Export;
using WorkAudit.Core.Helpers;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Dialogs;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views;

internal sealed class CustodianFilterItem
{
    public string DisplayName { get; set; } = "";
    public int? Id { get; set; }
}

public partial class ArchiveView : UserControl
{
    private readonly ILogger _log = LoggingService.ForContext<ArchiveView>();
    private readonly IDocumentStore _store;
    private readonly IDocumentTypeService _docTypeService;
    private readonly ISearchExportService _exportService;
    private readonly IArchiveService _archiveService;
    private readonly ILegalHoldService _legalHoldService;
    private readonly IImmutabilityService _immutabilityService;
    private readonly IChainOfCustodyService _chainOfCustodyService;
    private readonly IAuditTrailService _auditTrail;
    private readonly IPermissionService _permissionService;
    private readonly ISavedArchiveSearchService _savedSearches;
    private readonly IExportEncryptionService _encryptionService;
    private readonly ICustodianService _custodianService;
    private readonly IDisposalService _disposalService;
    private readonly ISessionService _sessionService;
    private List<Document> _currentResults = new();
    private readonly PdfPreviewLoadCoordinator _pdfPreviewLoad = new();


    private bool IsManagerOrAdmin => _permissionService.HasMinimumRole(Roles.Manager);
    private bool CanPickAnyBranch => IsManagerOrAdmin || Branches.ScopesToAllBranches(_sessionService.CurrentUser?.Branch);
    private string CurrentUserBranch => Branches.ToConcreteBranchOrDefault(_sessionService.CurrentUser?.Branch);

    private void ApplyArchiveToolbarVisibility()
    {
        var showStats = IsManagerOrAdmin;
        var canLegalHold = _permissionService.HasPermission(Permissions.ArchiveLegalHold);
        var canDispose = _permissionService.HasPermission(Permissions.ArchiveDispose);
        var canExport = _permissionService.HasPermission(Permissions.ArchiveExport);

        ArchiveStatsRow.Visibility = showStats ? Visibility.Visible : Visibility.Collapsed;
        ApplyHoldBtn.Visibility = canLegalHold ? Visibility.Visible : Visibility.Collapsed;
        ReleaseHoldBtn.Visibility = canLegalHold ? Visibility.Visible : Visibility.Collapsed;
        RequestDisposalBtn.Visibility = canDispose ? Visibility.Visible : Visibility.Collapsed;
        ApproveDisposalBtn.Visibility = canDispose ? Visibility.Visible : Visibility.Collapsed;
        RejectDisposalBtn.Visibility = canDispose ? Visibility.Visible : Visibility.Collapsed;
        AssignCustodianBtn.Visibility = IsManagerOrAdmin ? Visibility.Visible : Visibility.Collapsed;
        ExportBtn.Visibility = canExport ? Visibility.Visible : Visibility.Collapsed;
        EncryptExportCheck.Visibility = canExport ? Visibility.Visible : Visibility.Collapsed;
        ChainOfCustodyBtn.Visibility = canExport ? Visibility.Visible : Visibility.Collapsed;
    }

    public ArchiveView(
        IDocumentStore store,
        IDocumentTypeService docTypeService,
        ISearchExportService exportService,
        IArchiveService archiveService,
        ILegalHoldService legalHoldService,
        IImmutabilityService immutabilityService,
        IChainOfCustodyService chainOfCustodyService,
        IAuditTrailService auditTrail,
        IPermissionService permissionService,
        ISavedArchiveSearchService savedSearches,
        IExportEncryptionService encryptionService,
        ICustodianService custodianService,
        IDisposalService disposalService,
        ISessionService sessionService)
    {
        InitializeComponent();
        _store = store;
        _docTypeService = docTypeService;
        _exportService = exportService;
        _archiveService = archiveService;
        _legalHoldService = legalHoldService;
        _immutabilityService = immutabilityService;
        _chainOfCustodyService = chainOfCustodyService;
        _auditTrail = auditTrail;
        _permissionService = permissionService;
        _savedSearches = savedSearches;
        _encryptionService = encryptionService;
        _custodianService = custodianService;
        _disposalService = disposalService;
        _sessionService = sessionService;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        SectionFilter.SelectionChanged += ArchiveFilterScope_SelectionChanged;
        BranchFilter.SelectionChanged += ArchiveFilterScope_SelectionChanged;
        ArchivePdfPreviewTools?.SetTargetViewer(PdfPreviewViewer);
        ApplyLocalization();
        PopulateFilterOptions();
        PopulateSavedSearches();
        LoadFilterPersistence();
        ApplyArchiveToolbarVisibility();
        RefreshArchiveStats();
        RunSearch();
        UpdateButtonStates();
    }

    /// <summary>Refresh the archive search results and stats. Called when switching to Archive tab.</summary>
    public void Refresh()
    {
        ApplyArchiveToolbarVisibility();
        RunSearch();
        RefreshArchiveStats();
        UpdateButtonStates();
    }

    private void ApplyLocalization()
    {
        var config = ServiceContainer.GetService<IConfigStore>();
        if (FiltersHeaderLabel != null) FiltersHeaderLabel.Text = ReportLocalizationService.GetString("Filters", config);
        if (BranchLabel != null) BranchLabel.Text = ReportLocalizationService.GetString("Branch", config);
        if (SectionLabel != null) SectionLabel.Text = ReportLocalizationService.GetString("Section", config);
        if (TypeLabel != null) TypeLabel.Text = ReportLocalizationService.GetString("Type", config);
        if (LegalHoldLabel != null) LegalHoldLabel.Text = ReportLocalizationService.GetString("LegalHold", config);
        if (CaptureDateFromLabel != null) CaptureDateFromLabel.Text = ReportLocalizationService.GetString("DocumentDateFrom", config);
        if (CaptureDateToLabel != null) CaptureDateToLabel.Text = ReportLocalizationService.GetString("DocumentDateTo", config);
        if (ExpiringWithinLabel != null) ExpiringWithinLabel.Text = ReportLocalizationService.GetString("ExpiringWithin", config);
        if (TagLabel != null) TagLabel.Text = ReportLocalizationService.GetString("Tag", config);
        if (CustodianLabel != null) CustodianLabel.Text = ReportLocalizationService.GetString("Custodian", config);
        if (DisposalLabel != null) DisposalLabel.Text = ReportLocalizationService.GetString("Disposal", config);
        if (TextSearchLabel != null) TextSearchLabel.Text = ReportLocalizationService.GetString("TextSearch", config);
        if (SearchBtn != null) SearchBtn.Content = ReportLocalizationService.GetString("Search", config);
        if (ClearBtn != null) ClearBtn.Content = ReportLocalizationService.GetString("Clear", config);
        if (ArchiveStatsLabel != null) ArchiveStatsLabel.Text = ReportLocalizationService.GetString("ArchiveStats", config);
        if (SaveSearchBtn != null) SaveSearchBtn.Content = ReportLocalizationService.GetString("SaveSearch", config);
        if (ColumnsBtn != null) ColumnsBtn.Content = ReportLocalizationService.GetString("Columns", config);
        if (ApplyHoldBtn != null) ApplyHoldBtn.Content = ReportLocalizationService.GetString("ApplyLegalHold", config);
        if (ReleaseHoldBtn != null) ReleaseHoldBtn.Content = ReportLocalizationService.GetString("ReleaseLegalHold", config);
        if (BulkTagBtn != null) BulkTagBtn.Content = ReportLocalizationService.GetString("BulkTag", config);
        if (AssignCustodianBtn != null) AssignCustodianBtn.Content = ReportLocalizationService.GetString("AssignCustodian", config);
        if (RequestDisposalBtn != null) RequestDisposalBtn.Content = ReportLocalizationService.GetString("RequestDisposal", config);
        if (ApproveDisposalBtn != null) ApproveDisposalBtn.Content = ReportLocalizationService.GetString("ApproveDisposal", config);
        if (RejectDisposalBtn != null) RejectDisposalBtn.Content = ReportLocalizationService.GetString("RejectDisposal", config);
        if (ExportBtn != null) ExportBtn.Content = ReportLocalizationService.GetString("ExportArchive", config);
        if (EncryptExportCheck != null) EncryptExportCheck.Content = ReportLocalizationService.GetString("Encrypt", config);
        if (ChainOfCustodyBtn != null) ChainOfCustodyBtn.Content = ReportLocalizationService.GetString("ChainOfCustody", config);
        if (PreviewPlaceholderText != null) PreviewPlaceholderText.Text = ReportLocalizationService.GetString("SelectDocumentToPreview", config);
    }

    private void ArchiveView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            RunSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (ExportBtn.IsEnabled)
                ExportBtn_Click(ExportBtn, e);
            e.Handled = true;
        }
    }

    private void PopulateFilterOptions()
    {
        BranchFilter.Items.Clear();
        if (CanPickAnyBranch)
        {
            BranchFilter.Items.Add(Branches.AllBranchesLabel);
            foreach (var b in Branches.All) BranchFilter.Items.Add(b);
            BranchFilter.IsEnabled = true;
        }
        else
        {
            BranchFilter.Items.Add(CurrentUserBranch);
            BranchFilter.IsEnabled = false;
        }

        SectionFilter.Items.Clear();
        SectionFilter.Items.Add("");
        foreach (var s in Enums.SectionValues) SectionFilter.Items.Add(s);

        RefreshTypeFilterOptions();

        LegalHoldFilter.Items.Clear();
        LegalHoldFilter.Items.Add(new ComboBoxItem { Content = "All", Tag = (bool?)null });
        LegalHoldFilter.Items.Add(new ComboBoxItem { Content = "Yes", Tag = (bool?)true });
        LegalHoldFilter.Items.Add(new ComboBoxItem { Content = "No", Tag = (bool?)false });
        LegalHoldFilter.SelectedIndex = 0;

        ExpiringWithinFilter.Items.Clear();
        ExpiringWithinFilter.Items.Add(new ComboBoxItem { Content = "All", Tag = (int?)null });
        ExpiringWithinFilter.Items.Add(new ComboBoxItem { Content = "30 days", Tag = (int?)30 });
        ExpiringWithinFilter.Items.Add(new ComboBoxItem { Content = "90 days", Tag = (int?)90 });
        ExpiringWithinFilter.Items.Add(new ComboBoxItem { Content = "1 year", Tag = (int?)365 });
        ExpiringWithinFilter.SelectedIndex = 0;

        TagFilter.Items.Clear();
        TagFilter.Items.Add("");
        foreach (var t in _store.GetDistinctTags(Enums.Status.Archived))
            TagFilter.Items.Add(t);

        CustodianFilter.Items.Clear();
        CustodianFilter.Items.Add(new CustodianFilterItem { DisplayName = "-- All --", Id = null });
        foreach (var u in _custodianService.ListCustodians())
            CustodianFilter.Items.Add(new CustodianFilterItem { DisplayName = u.DisplayName ?? u.Username, Id = u.Id });
        CustodianFilter.SelectedIndex = 0;

        DisposalFilter.Items.Clear();
        DisposalFilter.Items.Add(new ComboBoxItem { Content = "All", Tag = (string?)null });
        DisposalFilter.Items.Add(new ComboBoxItem { Content = "Pending", Tag = "Pending" });
        DisposalFilter.Items.Add(new ComboBoxItem { Content = "Approved", Tag = "Approved" });
        DisposalFilter.Items.Add(new ComboBoxItem { Content = "Rejected", Tag = "Rejected" });
        DisposalFilter.SelectedIndex = 0;
    }

    private void LoadFilterPersistence()
    {
        var persistedBranch = FilterPersistence.Load(Constants.FilterPrefix.Archive, Constants.FilterKey.Branch);
        if (CanPickAnyBranch)
        {
            var valid = persistedBranch == Branches.AllBranchesLabel || string.IsNullOrWhiteSpace(persistedBranch) || Branches.All.Contains(persistedBranch.Trim());
            BranchFilter.Text = valid ? (string.IsNullOrWhiteSpace(persistedBranch) ? Branches.AllBranchesLabel : persistedBranch.Trim()) : Branches.AllBranchesLabel;
        }
        else
        {
            BranchFilter.Text = CurrentUserBranch;
        }
        SectionFilter.Text = FilterPersistence.Load(Constants.FilterPrefix.Archive, Constants.FilterKey.Section);
        RefreshTypeFilterOptions(FilterPersistence.Load(Constants.FilterPrefix.Archive, Constants.FilterKey.DocumentType));
        SearchBox.Text = FilterPersistence.Load(Constants.FilterPrefix.Archive, Constants.FilterKey.TextQuery);
        DateFromFilter.SelectedDate = FilterPersistence.LoadDate(Constants.FilterPrefix.Archive, Constants.FilterKey.DateFrom);
        DateToFilter.SelectedDate = FilterPersistence.LoadDate(Constants.FilterPrefix.Archive, Constants.FilterKey.DateTo);
        var lh = FilterPersistence.Load(Constants.FilterPrefix.Archive, Constants.FilterKey.LegalHold);
        if (lh == "Yes") LegalHoldFilter.SelectedIndex = 1;
        else if (lh == "No") LegalHoldFilter.SelectedIndex = 2;
        var expDays = FilterPersistence.Load(Constants.FilterPrefix.Archive, Constants.FilterKey.ExpiringWithinDays);
        if (int.TryParse(expDays, out var d))
        {
            var idx = ExpiringWithinFilter.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (i.Tag as int?) == d);
            if (idx >= 0) ExpiringWithinFilter.SelectedIndex = idx;
        }
        TagFilter.Text = FilterPersistence.Load(Constants.FilterPrefix.Archive, Constants.FilterKey.Tag);
        var custIdStr = FilterPersistence.Load(Constants.FilterPrefix.Archive, "CustodianId");
        if (int.TryParse(custIdStr, out var custId))
        {
            for (var i = 0; i < CustodianFilter.Items.Count; i++)
            {
                if (CustodianFilter.Items[i] is CustodianFilterItem cfi && cfi.Id == custId) { CustodianFilter.SelectedItem = CustodianFilter.Items[i]; break; }
            }
        }
        var dispStr = FilterPersistence.Load(Constants.FilterPrefix.Archive, "DisposalStatus");
        if (!string.IsNullOrEmpty(dispStr))
        {
            for (var i = 0; i < DisposalFilter.Items.Count; i++)
            {
                if (DisposalFilter.Items[i] is ComboBoxItem ci && (ci.Tag as string) == dispStr) { DisposalFilter.SelectedIndex = i; break; }
            }
        }
    }

    private void SaveFilterPersistence()
    {
        FilterPersistence.Save(Constants.FilterPrefix.Archive, Constants.FilterKey.Branch, BranchFilter.Text);
        FilterPersistence.Save(Constants.FilterPrefix.Archive, Constants.FilterKey.Section, SectionFilter.Text);
        FilterPersistence.Save(Constants.FilterPrefix.Archive, Constants.FilterKey.DocumentType, TypeFilter.Text);
        FilterPersistence.Save(Constants.FilterPrefix.Archive, Constants.FilterKey.TextQuery, SearchBox.Text);
        FilterPersistence.SaveDate(Constants.FilterPrefix.Archive, Constants.FilterKey.DateFrom, DateFromFilter.SelectedDate);
        FilterPersistence.SaveDate(Constants.FilterPrefix.Archive, Constants.FilterKey.DateTo, DateToFilter.SelectedDate);
        var lhItem = LegalHoldFilter.SelectedItem as ComboBoxItem;
        var lhTag = lhItem?.Tag as bool?;
        FilterPersistence.Save(Constants.FilterPrefix.Archive, Constants.FilterKey.LegalHold, lhTag == true ? "Yes" : lhTag == false ? "No" : "");
        var expItem = ExpiringWithinFilter.SelectedItem as ComboBoxItem;
        var expTag = expItem?.Tag as int?;
        FilterPersistence.Save(Constants.FilterPrefix.Archive, Constants.FilterKey.ExpiringWithinDays, expTag?.ToString() ?? "");
        FilterPersistence.Save(Constants.FilterPrefix.Archive, Constants.FilterKey.Tag, TagFilter.Text?.Trim() ?? "");
        var cust = CustodianFilter.SelectedItem as CustodianFilterItem;
        FilterPersistence.Save(Constants.FilterPrefix.Archive, "CustodianId", cust?.Id?.ToString() ?? "");
        var dispItem = DisposalFilter.SelectedItem as ComboBoxItem;
        FilterPersistence.Save(Constants.FilterPrefix.Archive, "DisposalStatus", (dispItem?.Tag as string) ?? "");
    }

    private void ArchiveFilterScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshTypeFilterOptions();
    }

    private void RefreshTypeFilterOptions(string? preferredType = null)
    {
        var hasExplicitPreferred = preferredType != null;
        var previous = preferredType;
        if (!hasExplicitPreferred && string.IsNullOrWhiteSpace(previous))
            previous = TypeFilter.SelectedItem as string ?? TypeFilter.Text?.Trim();
        if (string.IsNullOrWhiteSpace(previous))
            previous = null;

        string? branch;
        if (CanPickAnyBranch)
        {
            var txt = BranchFilter.Text?.Trim();
            branch = string.IsNullOrEmpty(txt) || txt == Branches.AllBranchesLabel ? null : txt;
        }
        else
        {
            branch = CurrentUserBranch;
        }

        var section = string.IsNullOrWhiteSpace(SectionFilter.Text) ? null : SectionFilter.Text.Trim();
        var types = _docTypeService.GetDocumentTypesForSection(section, branch);

        TypeFilter.Items.Clear();
        TypeFilter.Items.Add("");
        foreach (var t in types)
            TypeFilter.Items.Add(t);

        RestoreTypeFilterSelection(previous);
    }

    private void RestoreTypeFilterSelection(string? previous)
    {
        if (string.IsNullOrWhiteSpace(previous))
            return;

        foreach (var item in TypeFilter.Items)
        {
            if (item is string str && string.Equals(str, previous, StringComparison.OrdinalIgnoreCase))
            {
                TypeFilter.SelectedItem = item;
                return;
            }
        }

        TypeFilter.Items.Add(previous);
        TypeFilter.SelectedItem = previous;
    }

    private void RefreshArchiveStats()
    {
        if (!IsManagerOrAdmin)
        {
            ArchiveStatsText.Text = "—";
            return;
        }

        var stats = _store.GetStats();
        ArchiveStatsText.Text = $"{stats.ArchivedCount} total | {stats.ArchivedLegalHoldCount} legal hold | {stats.ArchivedExpiringWithin90DaysCount} expiring within 90 days";
    }

    private void RunSearch()
    {
        string? branch;
        if (CanPickAnyBranch)
        {
            var txt = BranchFilter.Text?.Trim();
            branch = (string.IsNullOrEmpty(txt) || txt == Branches.AllBranchesLabel) ? null : txt;
        }
        else
        {
            branch = CurrentUserBranch;
        }
        var section = string.IsNullOrWhiteSpace(SectionFilter.Text) ? null : SectionFilter.Text.Trim();
        var docType = string.IsNullOrWhiteSpace(TypeFilter.Text) ? null : TypeFilter.Text.Trim();
        var dateFrom = DateFromFilter.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var dateTo = DateToFilter.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var textQuery = SearchBox.Text?.Trim();
        bool? legalHoldOnly = null;
        if (LegalHoldFilter.SelectedItem is ComboBoxItem { Tag: bool b })
            legalHoldOnly = b;
        string? retentionExpiryBefore = null;
        if (ExpiringWithinFilter.SelectedItem is ComboBoxItem { Tag: int days })
            retentionExpiryBefore = DateTime.UtcNow.AddDays(days).ToString("yyyy-MM-dd");

        var tagFilter = string.IsNullOrWhiteSpace(TagFilter.Text) ? null : TagFilter.Text.Trim();
        int? custodianId = null;
        if (CustodianFilter.SelectedItem is CustodianFilterItem cfi && cfi.Id.HasValue)
            custodianId = cfi.Id;
        var dispItem = DisposalFilter.SelectedItem as ComboBoxItem;
        var disposalStatus = dispItem?.Tag as string;
        var list = _store.ListDocuments(
            branch: branch,
            section: section,
            documentType: docType,
            status: Enums.Status.Archived,
            dateFrom: dateFrom,
            dateTo: dateTo,
            textSearch: string.IsNullOrEmpty(textQuery) ? null : textQuery,
            limit: 5000,
            legalHoldOnly: legalHoldOnly,
            retentionExpiryBefore: retentionExpiryBefore,
            tagFilter: tagFilter,
            custodianId: custodianId,
            disposalStatus: disposalStatus);

        _currentResults = list;
        ResultsGrid.ItemsSource = list;
        ResultCountText.Text = $"{list.Count} archived document(s)";
        SaveFilterPersistence();
        RefreshArchiveStats();
        UpdateBulkLabel();
    }

    private void UpdateBulkLabel()
    {
        var count = ResultsGrid.SelectedItems.Count;
        BulkActionLabel.Text = count > 0 ? $"{count} selected" : "Use Ctrl+Click / Shift+Click for multi-select";
    }

    private void UpdateButtonStates()
    {
        var selected = ResultsGrid.SelectedItems.Cast<Document>().ToList();
        var hasSelection = selected.Count > 0;
        var anyWithHold = selected.Any(d => d.LegalHold);
        var anyWithoutHold = selected.Any(d => !d.LegalHold);
        var canLegalHold = _permissionService.HasPermission(Permissions.ArchiveLegalHold);
        var canExport = _permissionService.HasPermission(Permissions.ArchiveExport);

        ApplyHoldBtn.IsEnabled = hasSelection && anyWithoutHold && canLegalHold;
        ReleaseHoldBtn.IsEnabled = hasSelection && anyWithHold && canLegalHold;
        BulkTagBtn.IsEnabled = hasSelection;
        AssignCustodianBtn.IsEnabled = hasSelection && IsManagerOrAdmin;
        var canDispose = _permissionService.HasPermission(Permissions.ArchiveDispose);
        var anyPending = selected.Any(d => d.DisposalStatus == "Pending");
        var anyNotPending = selected.Any(d => !d.LegalHold && d.DisposalStatus != "Pending");
        RequestDisposalBtn.IsEnabled = hasSelection && anyNotPending && canDispose;
        ApproveDisposalBtn.IsEnabled = hasSelection && anyPending && canDispose;
        RejectDisposalBtn.IsEnabled = hasSelection && anyPending && canDispose;
        ExportBtn.IsEnabled = hasSelection && canExport;
        ChainOfCustodyBtn.IsEnabled = selected.Count >= 1 && canExport;
    }

    private void SearchBtn_Click(object sender, RoutedEventArgs e)
    {
        RunSearch();
    }
    private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) RunSearch(); }

    private void PopulateSavedSearches()
    {
        SavedSearchesCombo.Items.Clear();
        SavedSearchesCombo.Items.Add(new ComboBoxItem { Content = "-- Load saved search --", Tag = (SavedArchiveSearch?)null });
        foreach (var s in _savedSearches.List())
            SavedSearchesCombo.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s });
        SavedSearchesCombo.SelectedIndex = 0;
        SavedSearchesCombo.SelectionChanged += SavedSearchesCombo_SelectionChanged;
    }

    private void SavedSearchesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedSearchesCombo.SelectedItem is ComboBoxItem { Tag: SavedArchiveSearch s })
        {
            ApplySavedSearch(s);
            SavedSearchesCombo.SelectedIndex = 0;
        }
    }

    private void ApplySavedSearch(SavedArchiveSearch s)
    {
        if (CanPickAnyBranch)
        {
            var b = (s.Branch ?? "").Trim();
            BranchFilter.Text = (string.IsNullOrEmpty(b) || b == Branches.AllBranchesLabel || !Branches.All.Contains(b)) ? Branches.AllBranchesLabel : b;
        }
        else
        {
            BranchFilter.Text = CurrentUserBranch;
        }
        SectionFilter.Text = s.Section ?? "";
        RefreshTypeFilterOptions(s.DocumentType ?? "");
        SearchBox.Text = s.TextQuery ?? "";
        DateFromFilter.SelectedDate = ParseDate(s.DateFrom);
        DateToFilter.SelectedDate = ParseDate(s.DateTo);
        LegalHoldFilter.SelectedIndex = s.LegalHold == "Yes" ? 1 : s.LegalHold == "No" ? 2 : 0;
        var expIdx = ExpiringWithinFilter.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (i.Tag as int?) == s.ExpiringWithinDays);
        if (expIdx >= 0) ExpiringWithinFilter.SelectedIndex = expIdx;
        TagFilter.Text = s.Tag ?? "";
        SaveFilterPersistence();
        RunSearch();
    }

    private static DateTime? ParseDate(string? d)
    {
        if (string.IsNullOrEmpty(d)) return null;
        return DateTime.TryParse(d, out var dt) ? dt : null;
    }

    private void SaveSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox("Name for this search:", "Save Archive Search", "My Search");
        if (string.IsNullOrWhiteSpace(name)) return;
        var search = CaptureCurrentFilters();
        search.Name = name.Trim();
        _savedSearches.Save(search);
        SavedSearchesCombo.SelectionChanged -= SavedSearchesCombo_SelectionChanged;
        PopulateSavedSearches();
        MessageBox.Show($"Saved search \"{name}\".", "Save Search", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private SavedArchiveSearch CaptureCurrentFilters()
    {
        var lhItem = LegalHoldFilter.SelectedItem as ComboBoxItem;
        var lhTag = lhItem?.Tag as bool?;
        var expItem = ExpiringWithinFilter.SelectedItem as ComboBoxItem;
        var expTag = expItem?.Tag as int?;
        return new SavedArchiveSearch
        {
            Branch = CanPickAnyBranch
                ? (BranchFilter.Text == Branches.AllBranchesLabel || string.IsNullOrWhiteSpace(BranchFilter.Text) ? null : BranchFilter.Text.Trim())
                : CurrentUserBranch,
            Section = string.IsNullOrWhiteSpace(SectionFilter.Text) ? null : SectionFilter.Text.Trim(),
            DocumentType = string.IsNullOrWhiteSpace(TypeFilter.Text) ? null : TypeFilter.Text.Trim(),
            TextQuery = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim(),
            DateFrom = DateFromFilter.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            DateTo = DateToFilter.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            LegalHold = lhTag == true ? "Yes" : lhTag == false ? "No" : null,
            ExpiringWithinDays = expTag,
            Tag = string.IsNullOrWhiteSpace(TagFilter.Text) ? null : TagFilter.Text.Trim()
        };
    }

    private void ColumnsBtn_Click(object sender, RoutedEventArgs e)
    {
        var ctx = new ContextMenu();
        var columns = new List<(DataGridColumn col, string label)>
        {
            (ColId, "ID"), (ColType, "Type"), (ColDocumentDate, "Document Date"), (ColArchived, "Archived"), (ColRetention, "Retention Expiry"),
            (ColTags, "Tags"), (ColDisposal, "Disposal"), (ColHold, "Hold"), (ColSize, "Size"), (ColSection, "Section")
        };
        foreach (var (col, label) in columns)
        {
            var c = col;
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = c.Visibility == Visibility.Visible };
            item.Click += (_, _) => { c.Visibility = c.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; };
            ctx.Items.Add(item);
        }
        ctx.PlacementTarget = ColumnsBtn;
        ctx.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ctx.IsOpen = true;
    }

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        BranchFilter.Text = CanPickAnyBranch ? Branches.AllBranchesLabel : CurrentUserBranch;
        SectionFilter.Text = "";
        TypeFilter.Text = "";
        SearchBox.Text = "";
        DateFromFilter.SelectedDate = null;
        DateToFilter.SelectedDate = null;
        LegalHoldFilter.SelectedIndex = 0;
        ExpiringWithinFilter.SelectedIndex = 0;
        TagFilter.Text = "";
        CustodianFilter.SelectedIndex = 0;
        DisposalFilter.SelectedIndex = 0;
        SaveFilterPersistence();
        RunSearch();
    }

    private static readonly string[] PreviewExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".tif" };

    private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBulkLabel();
        UpdateButtonStates();
        var doc = ResultsGrid.SelectedItems.Count == 1 && ResultsGrid.SelectedItem is Document d ? d : null;
        ServiceContainer.GetService<ICurrentDocumentContextService>().SetCurrentDocument(doc);
        if (doc != null)
        {
            RefreshDetailText(doc);
            _ = VerifyHashOnSelection(doc);
        }
        else
        {
            DetailText.Text = ResultsGrid.SelectedItems.Count > 1
                ? $"{ResultsGrid.SelectedItems.Count} documents selected."
                : "Select a document to view details.";
        }
        UpdatePreviewFromSelection();
    }

    private void UpdatePreviewFromSelection()
    {
        if (DocumentPreviewViewer == null || PreviewPlaceholder == null) return;
        var doc = ResultsGrid.SelectedItems.Count == 1 && ResultsGrid.SelectedItem is Document d ? d : null;
        if (doc == null)
        {
            DocumentPreviewViewer.Clear();
            DocumentPreviewViewer.Visibility = Visibility.Collapsed;
            SetPdfPreviewSource(null);
            if (PdfPreviewViewer != null) PdfPreviewViewer.Visibility = Visibility.Collapsed;
            if (ArchivePdfPreviewTools != null) ArchivePdfPreviewTools.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewPlaceholderText.Text = ResultsGrid.SelectedItems.Count > 1 ? $"{ResultsGrid.SelectedItems.Count} documents selected." : "Select a document to preview.";
            return;
        }
        if (!_permissionService.CanAccessDocument(doc))
        {
            DocumentPreviewViewer.Clear();
            DocumentPreviewViewer.Visibility = Visibility.Collapsed;
            SetPdfPreviewSource(null);
            if (PdfPreviewViewer != null) PdfPreviewViewer.Visibility = Visibility.Collapsed;
            if (ArchivePdfPreviewTools != null) ArchivePdfPreviewTools.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewPlaceholderText.Text = "Locked (requires Manager/Admin)";
            _log.Information("Archive preview denied for document {DocId} (branch/aging gate)", doc.Id);
            return;
        }
        var path = doc.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            DocumentPreviewViewer.Clear();
            DocumentPreviewViewer.Visibility = Visibility.Collapsed;
            SetPdfPreviewSource(null);
            if (PdfPreviewViewer != null) PdfPreviewViewer.Visibility = Visibility.Collapsed;
            if (ArchivePdfPreviewTools != null) ArchivePdfPreviewTools.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewPlaceholderText.Text = "No file path.";
            return;
        }
        if (!File.Exists(path))
        {
            DocumentPreviewViewer.Clear();
            DocumentPreviewViewer.Visibility = Visibility.Collapsed;
            SetPdfPreviewSource(null);
            if (PdfPreviewViewer != null) PdfPreviewViewer.Visibility = Visibility.Collapsed;
            if (ArchivePdfPreviewTools != null) ArchivePdfPreviewTools.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewPlaceholderText.Text = "File not found.";
            return;
        }
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".pdf")
        {
            DocumentPreviewViewer.Clear();
            DocumentPreviewViewer.Visibility = Visibility.Collapsed;
            if (ArchivePdfPreviewTools != null)
            {
                ArchivePdfPreviewTools.Visibility = Visibility.Visible;
                ArchivePdfPreviewTools.SetTargetViewer(PdfPreviewViewer);
            }
            if (PdfPreviewViewer != null)
                SetPdfPreviewSource(path, () =>
                {
                    PdfPreviewViewer.Visibility = Visibility.Visible;
                    Dispatcher.BeginInvoke(new Action(() => ArchivePdfPreviewTools?.SetTargetViewer(PdfPreviewViewer)), System.Windows.Threading.DispatcherPriority.Loaded);
                });
            else
                SetPdfPreviewSource(path);
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            return;
        }
        if (!PreviewExtensions.Contains(ext))
        {
            DocumentPreviewViewer.Clear();
            DocumentPreviewViewer.Visibility = Visibility.Collapsed;
            SetPdfPreviewSource(null);
            if (PdfPreviewViewer != null) PdfPreviewViewer.Visibility = Visibility.Collapsed;
            if (ArchivePdfPreviewTools != null) ArchivePdfPreviewTools.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewPlaceholderText.Text = "Preview not available for this format.";
            return;
        }
        SetPdfPreviewSource(null);
        if (PdfPreviewViewer != null) PdfPreviewViewer.Visibility = Visibility.Collapsed;
        if (ArchivePdfPreviewTools != null) ArchivePdfPreviewTools.Visibility = Visibility.Collapsed;
        DocumentPreviewViewer.SetSource(path);
        DocumentPreviewViewer.Visibility = Visibility.Visible;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void SetPdfPreviewSource(string? filePath, Action? onSourceApplied = null)
    {
        _pdfPreviewLoad.SetSource(PdfPreviewViewer, filePath, onSourceApplied);
    }

    private async System.Threading.Tasks.Task VerifyHashOnSelection(Document doc)
    {
        if (!doc.IsImmutable) return;
        var ok = _immutabilityService.VerifyHash(doc);
        if (!ok)
        {
            DetailText.Text += "\n\n⚠ Hash verification FAILED - document may have been tampered.";
        }
    }

    private void RefreshDetailText(Document doc)
    {
        var archivedAt = string.IsNullOrEmpty(doc.ArchivedAt) ? "-" : doc.ArchivedAt.Length > 19 ? doc.ArchivedAt[..19] : doc.ArchivedAt;
        var lines = new List<string>
        {
            $"ID: {doc.Id}",
            $"Type: {doc.DocumentType ?? "-"}",
            $"Status: {doc.Status}",
            $"Archived: {archivedAt}",
            $"Retention Expiry: {doc.RetentionExpiryDate ?? "-"}",
            $"Legal Hold: {(doc.LegalHold ? "Yes" : "No")}",
            doc.LegalHold ? $"Case: {doc.LegalHoldCaseNumber ?? "-"}" : "",
            doc.LegalHold ? $"Reason: {doc.LegalHoldReason ?? "-"}" : "",
            $"Section: {doc.Section}",
            $"Date: {(!string.IsNullOrWhiteSpace(doc.ExtractedDate) ? doc.ExtractedDate.Trim() : "-")}",
            $"Account / party: {doc.AccountName ?? "-"}",
            $"Account no.: {doc.AccountNumber ?? "-"}",
            $"Transaction ref.: {doc.TransactionReference ?? "-"}",
            $"File: {Path.GetFileName(doc.FilePath) ?? "-"}",
            $"Hash: {doc.ImmutableHash ?? "-"}"
        };
        DetailText.Text = string.Join("\n", lines);
    }

    private async void ApplyHoldBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = ResultsGrid.SelectedItems.Cast<Document>().Where(d => !d.LegalHold).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select documents without legal hold.", "Apply Legal Hold", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new LegalHoldDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.CaseNumber) || string.IsNullOrWhiteSpace(dlg.Reason))
            return;

        try
        {
            var count = await _legalHoldService.ApplyLegalHoldAsync(selected, dlg.CaseNumber.Trim(), dlg.Reason.Trim());
            MessageBox.Show($"Applied legal hold to {count} document(s).", "Legal Hold", MessageBoxButton.OK, MessageBoxImage.Information);
            RunSearch();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed: {ex.Message}", "Legal Hold", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ReleaseHoldBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = ResultsGrid.SelectedItems.Cast<Document>().Where(d => d.LegalHold).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select documents with legal hold.", "Release Legal Hold", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"Release legal hold from {selected.Count} document(s)?", "Release Legal Hold", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            var count = await _legalHoldService.ReleaseLegalHoldAsync(selected);
            MessageBox.Show($"Released legal hold from {count} document(s).", "Legal Hold", MessageBoxButton.OK, MessageBoxImage.Information);
            RunSearch();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed: {ex.Message}", "Release Legal Hold", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BulkTagBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = ResultsGrid.SelectedItems.Cast<Document>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select documents to tag.", "Bulk Tag", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new BulkTagDialog(selected.Count) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || !dlg.Applied)
            return;

        var toAdd = BulkTagDialog.ParseTags(dlg.AddTags);
        var toRemove = BulkTagDialog.ParseTags(dlg.RemoveTags);
        if (toAdd.Length == 0 && toRemove.Length == 0)
            return;

        var updated = 0;
        foreach (var doc in selected)
        {
            var current = doc.GetTags().ToList();
            foreach (var t in toAdd)
                if (!current.Contains(t, StringComparer.OrdinalIgnoreCase))
                    current.Add(t);
            foreach (var t in toRemove)
                current.RemoveAll(x => x.Equals(t, StringComparison.OrdinalIgnoreCase));
            var newTags = string.Join(",", current);
            if (_store.UpdateTags(doc.Id, newTags))
                updated++;
        }

        MessageBox.Show($"Updated tags on {updated} document(s).", "Bulk Tag", MessageBoxButton.OK, MessageBoxImage.Information);
        RunSearch();
    }

    private void AssignCustodianBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = ResultsGrid.SelectedItems.Cast<Document>().ToList();
        if (selected.Count == 0) { MessageBox.Show("Select documents.", "Assign Custodian", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        var dlg = new AssignCustodianDialog(selected.Count) { Owner = Window.GetWindow(this) };
        dlg.CustodianCombo.ItemsSource = _custodianService.ListCustodians();
        if (dlg.ShowDialog() != true) return;

        var custodianId = dlg.ClearCustodian ? (int?)null : dlg.SelectedCustodian?.Id;
        var updated = _custodianService.AssignCustodianBulk(selected.Select(d => d.Id), custodianId) ? selected.Count : 0;
        MessageBox.Show($"Updated custodian on {selected.Count} document(s).", "Assign Custodian", MessageBoxButton.OK, MessageBoxImage.Information);
        RunSearch();
    }

    private void RequestDisposalBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = ResultsGrid.SelectedItems.Cast<Document>().Where(d => !d.LegalHold && d.DisposalStatus != "Pending").ToList();
        if (selected.Count == 0) { MessageBox.Show("Select documents without legal hold and not already pending disposal.", "Request Disposal", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        var user = _sessionService.CurrentUser;
        if (user == null) { MessageBox.Show("Not signed in.", "Request Disposal", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        if (MessageBox.Show($"Request disposal approval for {selected.Count} document(s)?", "Request Disposal", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _disposalService.RequestDisposal(selected, user.Id);
        MessageBox.Show($"Disposal requested for {selected.Count} document(s).", "Request Disposal", MessageBoxButton.OK, MessageBoxImage.Information);
        RunSearch();
    }

    private void ApproveDisposalBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = ResultsGrid.SelectedItems.Cast<Document>().Where(d => d.DisposalStatus == "Pending").ToList();
        if (selected.Count == 0) { MessageBox.Show("Select documents with Pending disposal status.", "Approve Disposal", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        var user = _sessionService.CurrentUser;
        if (user == null) { MessageBox.Show("Not signed in.", "Approve Disposal", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        if (MessageBox.Show($"Approve disposal of {selected.Count} document(s)? This marks them as approved for disposal.", "Approve Disposal", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _disposalService.ApproveDisposal(selected, user.Id);
        MessageBox.Show($"Approved disposal for {selected.Count} document(s).", "Approve Disposal", MessageBoxButton.OK, MessageBoxImage.Information);
        RunSearch();
    }

    private void RejectDisposalBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = ResultsGrid.SelectedItems.Cast<Document>().Where(d => d.DisposalStatus == "Pending").ToList();
        if (selected.Count == 0) { MessageBox.Show("Select documents with Pending disposal status.", "Reject Disposal", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        var user = _sessionService.CurrentUser;
        if (user == null) { MessageBox.Show("Not signed in.", "Reject Disposal", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        var reason = Microsoft.VisualBasic.Interaction.InputBox("Rejection reason (optional):", "Reject Disposal", "");
        _disposalService.RejectDisposal(selected, user.Id, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
        MessageBox.Show($"Rejected disposal for {selected.Count} document(s).", "Reject Disposal", MessageBoxButton.OK, MessageBoxImage.Information);
        RunSearch();
    }

    private async void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        var docs = _currentResults;
        var selected = ResultsGrid.SelectedItems.Cast<Document>().ToList();
        if (selected.Count > 0) docs = selected;

        if (docs.Count == 0)
        {
            MessageBox.Show("No documents to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var accessibleDocs = docs.Where(d => _permissionService.CanAccessDocument(d)).ToList();
        var lockedCount = docs.Count - accessibleDocs.Count;
        if (lockedCount > 0)
        {
            _log.Information("Export: {LockedCount} document(s) excluded (branch/aging gate)", lockedCount);
            MessageBox.Show($"{lockedCount} document(s) are locked (requires Manager/Admin) and were excluded from export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        if (accessibleDocs.Count == 0)
        {
            MessageBox.Show("No documents to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        docs = accessibleDocs;

        var encrypt = EncryptExportCheck.IsChecked == true;
        var dlg = new SaveFileDialog
        {
            Filter = encrypt
                ? "Encrypted Excel (*.xlsx.encrypted)|*.xlsx.encrypted|Encrypted PDF (*.pdf.encrypted)|*.pdf.encrypted"
                : "Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|PDF (*.pdf)|*.pdf",
            DefaultExt = encrypt ? ".xlsx.encrypted" : ".xlsx",
            FileName = $"WorkAudit_Archive_Export_{DateTime.Now:yyyyMMdd_HHmmss}"
        };
        if (dlg.ShowDialog() != true) return;

        var isPdf = dlg.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || dlg.FileName.EndsWith(".pdf.encrypted", StringComparison.OrdinalIgnoreCase);

        var progressService = ServiceContainer.GetService<IProcessingProgressService>();
        var isHeavyExport = isPdf; // PDF export is CPU-intensive

        if (encrypt)
        {
            var pwdDlg = new ExportPasswordDialog(requireConfirm: true) { Owner = Window.GetWindow(this) };
            if (pwdDlg.ShowDialog() != true || string.IsNullOrEmpty(pwdDlg.Password)) return;
            try
            {
                ExportBtn.IsEnabled = false;
                if (isHeavyExport) progressService.Start(0, "Exporting to PDF...");
                var ext = isPdf ? ".pdf" : ".xlsx";
                var tempPath = Path.Combine(Path.GetTempPath(), $"WorkAudit_Export_{Guid.NewGuid():N}{ext}");
                var destPath = dlg.FileName.EndsWith(".encrypted", StringComparison.OrdinalIgnoreCase) ? dlg.FileName : dlg.FileName + ".encrypted";
                try
                {
                    if (isPdf)
                        await System.Threading.Tasks.Task.Run(() => _exportService.ExportToPdf(docs, tempPath));
                    else
                        _exportService.ExportToExcel(docs, tempPath);
                    await System.Threading.Tasks.Task.Run(() => _encryptionService.EncryptFile(tempPath, destPath, pwdDlg.Password!));
                }
                finally
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    if (isHeavyExport) progressService.Complete();
                    ExportBtn.IsEnabled = true;
                }
                var outPath = destPath;
                var ids = docs.Count <= 20 ? string.Join(",", docs.Select(d => d.Id)) : string.Join(",", docs.Take(20).Select(d => d.Id)) + $" (and {docs.Count - 20} more)";
                var details = $"Exported {docs.Count} documents (encrypted, {(isPdf ? "PDF" : "Excel")}) to {outPath} | Document IDs: {ids}";
                _ = _auditTrail.LogDocumentActionAsync(AuditAction.ArchiveExported, docs[0], details);
                MessageBox.Show($"Exported {docs.Count} documents (encrypted).", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (isHeavyExport) progressService.Complete();
                ExportBtn.IsEnabled = true;
                MessageBox.Show($"Export failed: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }

        try
        {
            if (isPdf)
            {
                ExportBtn.IsEnabled = false;
                progressService.Start(0, "Exporting to PDF...");
                try
                {
                    await System.Threading.Tasks.Task.Run(() => _exportService.ExportToPdf(docs, dlg.FileName));
                }
                finally
                {
                    progressService.Complete();
                    ExportBtn.IsEnabled = true;
                }
            }
            else if (dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                _exportService.ExportToCsv(docs, dlg.FileName);
            }
            else
            {
                _exportService.ExportToExcel(docs, dlg.FileName);
            }
            var ids = docs.Count <= 20 ? string.Join(",", docs.Select(d => d.Id)) : string.Join(",", docs.Take(20).Select(d => d.Id)) + $" (and {docs.Count - 20} more)";
            var details = $"Exported {docs.Count} documents to {dlg.FileName} | Document IDs: {ids}";
            _ = _auditTrail.LogDocumentActionAsync(AuditAction.ArchiveExported, docs[0], details);
            MessageBox.Show($"Exported {docs.Count} documents.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            if (isHeavyExport) progressService.Complete();
            ExportBtn.IsEnabled = true;
            MessageBox.Show($"Export failed: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ChainOfCustodyBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = ResultsGrid.SelectedItems.Cast<Document>().Where(_permissionService.CanAccessDocument).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select one or more documents first.", "Chain of Custody", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.Count == 1)
        {
            var doc = selected[0];
            var dlg = new SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                FileName = $"ChainOfCustody_{doc.Id}_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dlg.ShowDialog() != true) return;

            var path = _chainOfCustodyService.GenerateReport(doc, dlg.FileName);
            if (path != null)
                MessageBox.Show($"Report saved to:\n{path}", "Chain of Custody", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show("Failed to generate report.", "Chain of Custody", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            var dlg = new SaveFileDialog
            {
                Filter = "ZIP (*.zip)|*.zip",
                DefaultExt = ".zip",
                FileName = $"ChainOfCustody_Batch_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dlg.ShowDialog() != true) return;

            var path = _chainOfCustodyService.GenerateBatchReport(selected, dlg.FileName);
            if (path != null)
                MessageBox.Show($"Batch report saved to:\n{path}\n({selected.Count} documents)", "Chain of Custody", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show("Failed to generate batch report.", "Chain of Custody", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
