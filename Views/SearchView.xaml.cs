using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Serilog;
using WorkAudit.Config;
using WorkAudit.Core;
using WorkAudit.Core.Import;
using WorkAudit.Core.Export;
using WorkAudit.Core.Helpers;
using WorkAudit.Core.TextExtraction;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views;

public partial class SearchView : UserControl
{
    private readonly ILogger _log = LoggingService.ForContext<SearchView>();
    private readonly IDocumentStore _store;
    private readonly IDocumentTypeService _docTypeService;
    private readonly ISavedSearchService _savedSearches;
    private readonly ISearchExportService _exportService;
    private readonly IAuditTrailService _auditTrail;
    private readonly IChangeHistoryService _changeHistory;
    private readonly IPermissionService _permissionService;
    private List<Document> _currentResults = new();

    private bool SearchCanPickAnyBranch => _permissionService.HasMinimumRole(Roles.Manager);

    private string SearchLockedUserBranch =>
        Branches.ToConcreteBranchOrDefault(ServiceContainer.GetService<ISessionService>().CurrentUser?.Branch);

    public SearchView(IDocumentStore store, IDocumentTypeService docTypeService, ISavedSearchService savedSearches, ISearchExportService exportService, IAuditTrailService auditTrail, IChangeHistoryService changeHistory, IPermissionService permissionService)
    {
        InitializeComponent();
        _store = store;
        _docTypeService = docTypeService;
        _savedSearches = savedSearches;
        _exportService = exportService;
        _auditTrail = auditTrail;
        _changeHistory = changeHistory;
        _permissionService = permissionService;

        Loaded += OnLoaded;
        BranchFilter.SelectionChanged += BranchOrSectionFilter_SelectionChanged;
        SectionFilter.SelectionChanged += BranchOrSectionFilter_SelectionChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        PopulateFilterOptions();
        LoadFilterPersistence();
        PopulateSavedSearches();
        RunSearch();
        UpdateButtonStates();
    }

    private void PopulateFilterOptions()
    {
        BranchFilter.Items.Clear();
        if (SearchCanPickAnyBranch)
        {
            BranchFilter.Items.Add("");
            foreach (var b in Branches.All) BranchFilter.Items.Add(b);
            BranchFilter.IsEnabled = true;
        }
        else
        {
            BranchFilter.Items.Add(SearchLockedUserBranch);
            BranchFilter.IsEnabled = false;
        }

        SectionFilter.Items.Clear();
        SectionFilter.Items.Add("");
        foreach (var s in Enums.SectionValues) SectionFilter.Items.Add(s);

        StatusFilter.Items.Clear();
        StatusFilter.Items.Add("");
        foreach (var s in Enums.StatusValues) StatusFilter.Items.Add(s);

        TypeFilter.Items.Clear();
        TypeFilter.Items.Add("");
        foreach (var t in _docTypeService.GetDocumentTypesForSection(section: null)) TypeFilter.Items.Add(t);

        RefreshTypeFilterOptions();
    }

    private void UpdateTypeFilterEnabled()
    {
        TypeFilter.IsEnabled = true;
    }

    private void BranchOrSectionFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
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

        var branch = string.IsNullOrWhiteSpace(BranchFilter.Text) ? null : BranchFilter.Text.Trim();
        var section = string.IsNullOrWhiteSpace(SectionFilter.Text) ? null : SectionFilter.Text.Trim();

        TypeFilter.Items.Clear();
        TypeFilter.Items.Add("");
        foreach (var t in _docTypeService.GetDocumentTypesForSection(section, branch))
            TypeFilter.Items.Add(t);

        UpdateTypeFilterEnabled();
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

    private void LoadFilterPersistence()
    {
        BranchFilter.Text = FilterPersistence.Load(Constants.FilterPrefix.Search, Constants.FilterKey.Branch);
        if (!SearchCanPickAnyBranch)
            BranchFilter.Text = SearchLockedUserBranch;
        SectionFilter.Text = FilterPersistence.Load(Constants.FilterPrefix.Search, Constants.FilterKey.Section);
        var persistedType = FilterPersistence.Load(Constants.FilterPrefix.Search, Constants.FilterKey.DocumentType);
        StatusFilter.Text = FilterPersistence.Load(Constants.FilterPrefix.Search, Constants.FilterKey.Status);
        SearchBox.Text = FilterPersistence.Load(Constants.FilterPrefix.Search, Constants.FilterKey.TextQuery);
        UseFtsCheck.IsChecked = FilterPersistence.LoadBool(Constants.FilterPrefix.Search, Constants.FilterKey.UseFts);
        DateFromFilter.SelectedDate = FilterPersistence.LoadDate(Constants.FilterPrefix.Search, Constants.FilterKey.DateFrom);
        DateToFilter.SelectedDate = FilterPersistence.LoadDate(Constants.FilterPrefix.Search, Constants.FilterKey.DateTo);
        RefreshTypeFilterOptions(persistedType);
    }

    private void SaveFilterPersistence()
    {
        FilterPersistence.Save(Constants.FilterPrefix.Search, Constants.FilterKey.Branch, BranchFilter.Text);
        FilterPersistence.Save(Constants.FilterPrefix.Search, Constants.FilterKey.Section, SectionFilter.Text);
        FilterPersistence.Save(Constants.FilterPrefix.Search, Constants.FilterKey.DocumentType, TypeFilter.Text);
        FilterPersistence.Save(Constants.FilterPrefix.Search, Constants.FilterKey.Status, StatusFilter.Text);
        FilterPersistence.Save(Constants.FilterPrefix.Search, Constants.FilterKey.TextQuery, SearchBox.Text);
        FilterPersistence.SaveBool(Constants.FilterPrefix.Search, Constants.FilterKey.UseFts, UseFtsCheck.IsChecked == true);
        FilterPersistence.SaveDate(Constants.FilterPrefix.Search, Constants.FilterKey.DateFrom, DateFromFilter.SelectedDate);
        FilterPersistence.SaveDate(Constants.FilterPrefix.Search, Constants.FilterKey.DateTo, DateToFilter.SelectedDate);
    }

    private void PopulateSavedSearches()
    {
        SavedSearchesCombo.Items.Clear();
        SavedSearchesCombo.Items.Add(new ComboBoxItem { Content = "-- Load saved search --", Tag = (SavedSearch?)null });
        foreach (var s in _savedSearches.List())
            SavedSearchesCombo.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s });
        SavedSearchesCombo.SelectedIndex = 0;
    }

    private void RunSearch()
    {
        var textQuery = SearchBox.Text?.Trim();
        var useFts = UseFtsCheck.IsChecked == true && !string.IsNullOrEmpty(textQuery);

        List<Document> list;

        var branch = string.IsNullOrWhiteSpace(BranchFilter.Text) ? null : BranchFilter.Text.Trim();
        if (!SearchCanPickAnyBranch)
            branch = SearchLockedUserBranch;

        if (useFts && !string.IsNullOrEmpty(textQuery))
        {
            list = _store.FullTextSearch(textQuery!, limit: 5000, branch: branch);
        }
        else
        {
            var section = string.IsNullOrWhiteSpace(SectionFilter.Text) ? null : SectionFilter.Text.Trim();
            var docType = string.IsNullOrWhiteSpace(TypeFilter.Text) ? null : TypeFilter.Text.Trim();
            var status = string.IsNullOrWhiteSpace(StatusFilter.Text) ? null : StatusFilter.Text.Trim();
            var dateFrom = DateFromFilter.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var dateTo = DateToFilter.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            list = _store.ListDocuments(
                branch: branch,
                section: section,
                documentType: docType,
                status: status,
                dateFrom: dateFrom,
                dateTo: dateTo,
                textSearch: string.IsNullOrEmpty(textQuery) ? null : textQuery,
                limit: 5000);
        }

        list = list.Where(d => _permissionService.CanAccessDocument(d)).ToList();

        _currentResults = list;
        ResultsGrid.ItemsSource = list;
        ResultCountText.Text = $"{list.Count} document(s)";
        SaveFilterPersistence();
        UpdateBulkLabel();
    }

    private void UpdateBulkLabel()
    {
        var count = ResultsGrid.SelectedItems.Count;
        BulkActionLabel.Text = count > 0 ? $"{count} selected" : "Use Ctrl+Click / Shift+Click for multi-select";
    }

    private void UpdateButtonStates()
    {
        var singleSelected = ResultsGrid.SelectedItems.Count == 1 && ResultsGrid.SelectedItem is Document;
        var canEdit = _permissionService.HasPermission(Permissions.DocumentEdit);
        ChangeTypeBtn.IsEnabled = singleSelected && canEdit;
        BulkStatusBtn.IsEnabled = ResultsGrid.SelectedItems.Count > 0 && canEdit;
        AssignBtn.IsEnabled = ResultsGrid.SelectedItems.Count > 0 && _permissionService.HasMinimumRole(Roles.Manager);
    }

    private void SearchBtn_Click(object sender, RoutedEventArgs e)
    {
        RunSearch();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) RunSearch(); }

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        BranchFilter.Text = SearchCanPickAnyBranch ? "" : SearchLockedUserBranch;
        SectionFilter.Text = "";
        RefreshTypeFilterOptions();
        StatusFilter.Text = "";
        SearchBox.Text = "";
        DateFromFilter.SelectedDate = null;
        DateToFilter.SelectedDate = null;
        UseFtsCheck.IsChecked = false;
        SaveFilterPersistence();
        RunSearch();
    }

    private void SaveSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox("Search name:", "Save Search", "My Search");
        if (string.IsNullOrWhiteSpace(name)) return;

        var search = new SavedSearch
        {
            Name = name.Trim(),
            Branch = string.IsNullOrWhiteSpace(BranchFilter.Text) ? null : BranchFilter.Text.Trim(),
            Section = string.IsNullOrWhiteSpace(SectionFilter.Text) ? null : SectionFilter.Text.Trim(),
            DocumentType = string.IsNullOrWhiteSpace(TypeFilter.Text) ? null : TypeFilter.Text.Trim(),
            Status = string.IsNullOrWhiteSpace(StatusFilter.Text) ? null : StatusFilter.Text.Trim(),
            TextQuery = SearchBox.Text?.Trim(),
            UseFullTextSearch = UseFtsCheck.IsChecked == true,
            DateFrom = DateFromFilter.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            DateTo = DateToFilter.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        };
        _savedSearches.Save(search);
        PopulateSavedSearches();
    }

    private void SavedSearchesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedSearchesCombo.SelectedItem is ComboBoxItem { Tag: SavedSearch s })
        {
            BranchFilter.Text = SearchCanPickAnyBranch ? (s.Branch ?? "") : SearchLockedUserBranch;
            SectionFilter.Text = s.Section ?? "";
            RefreshTypeFilterOptions(s.DocumentType ?? "");
            StatusFilter.Text = s.Status ?? "";
            SearchBox.Text = s.TextQuery ?? "";
            UseFtsCheck.IsChecked = s.UseFullTextSearch;
            if (!string.IsNullOrEmpty(s.DateFrom) && System.DateTime.TryParse(s.DateFrom, out var df)) DateFromFilter.SelectedDate = df;
            else DateFromFilter.SelectedDate = null;
            if (!string.IsNullOrEmpty(s.DateTo) && System.DateTime.TryParse(s.DateTo, out var dt)) DateToFilter.SelectedDate = dt;
            else DateToFilter.SelectedDate = null;
            RunSearch();
            SavedSearchesCombo.SelectedIndex = 0;
        }
    }

    private void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        var docs = _currentResults;
        if (docs.Count == 0)
        {
            MessageBox.Show("No documents to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",
            DefaultExt = ".xlsx",
            FileName = $"WorkAudit_Export_{System.DateTime.Now:yyyyMMdd_HHmmss}"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            if (dlg.FileName.EndsWith(".csv", System.StringComparison.OrdinalIgnoreCase))
                _exportService.ExportToCsv(docs, dlg.FileName);
            else
                _exportService.ExportToExcel(docs, dlg.FileName);
            MessageBox.Show($"Exported {docs.Count} documents.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBulkLabel();
        UpdateButtonStates();
        var doc = ResultsGrid.SelectedItems.Count == 1 && ResultsGrid.SelectedItem is Document d ? d : null;
        ServiceContainer.GetService<ICurrentDocumentContextService>().SetCurrentDocument(doc);
        if (doc != null)
        {
            RefreshDetailText(doc);
        }
        else if (ResultsGrid.SelectedItems.Count > 1)
        {
            var first = ResultsGrid.SelectedItems.Cast<Document>().FirstOrDefault();
            ServiceContainer.GetService<ICurrentDocumentContextService>().SetCurrentDocument(first);
            DetailText.Text = $"{ResultsGrid.SelectedItems.Count} documents selected. Use bulk actions below.";
        }
        else
        {
            ServiceContainer.GetService<ICurrentDocumentContextService>().SetCurrentDocument(null);
            DetailText.Text = "Select a document to view details.";
        }
    }

    private void RefreshDetailText(Document doc)
    {
        if (NoteAnchors.IsJournalAnchorDocument(doc.Uuid))
        {
            DetailText.Text =
                "This is an internal system record used for daily journal notes. It is not an audit document.";
            return;
        }
        var confidenceStr = doc.ClassificationConfidence.HasValue ? $"{doc.ClassificationConfidence.Value:P0}" : "-";
        var lines = new List<string>
        {
            $"ID: {doc.Id}",
            $"Type: {doc.DocumentType ?? "-"}",
            $"Confidence: {confidenceStr}",
            $"Status: {doc.StatusDisplay}",
            $"Section: {doc.Section}",
            $"Date: {(!string.IsNullOrWhiteSpace(doc.ExtractedDate) ? doc.ExtractedDate.Trim() : "-")}",
            $"Amounts: {doc.Amounts ?? "-"}",
            $"Account / party: {doc.AccountName ?? "-"}",
            $"Account no.: {doc.AccountNumber ?? "-"}",
            $"Transaction ref.: {doc.TransactionReference ?? "-"}",
            $"File: {Path.GetFileName(doc.FilePath) ?? "-"}",
            "",
            doc.Explanation ?? "No explanation"
        };
        DetailText.Text = string.Join(Environment.NewLine, lines);
    }

    private async void ChangeTypeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not Document doc)
        {
            MessageBox.Show("Select a single document first.", "Change type", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var combo = new System.Windows.Controls.ComboBox
        {
            ItemsSource = DocumentTypeInfo.BuildPickerItems(_docTypeService.GetAllDocumentTypes()),
            SelectedItem = DocumentTypeInfo.PickerSelectedValue(doc.DocumentType),
            MinWidth = 280,
            Margin = new System.Windows.Thickness(0, 0, 0, 12)
        };
        var panel = new StackPanel { Margin = new System.Windows.Thickness(16) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "New document type:", Margin = new System.Windows.Thickness(0, 0, 0, 6) });
        panel.Children.Add(combo);
        var btnPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new System.Windows.Thickness(0, 8, 0, 0)
        };
        var okBtn = new System.Windows.Controls.Button { Content = "OK", Padding = new System.Windows.Thickness(16, 6, 16, 6), Margin = new System.Windows.Thickness(0, 0, 8, 0) };
        var cancelBtn = new System.Windows.Controls.Button { Content = "Cancel", Padding = new System.Windows.Thickness(16, 6, 16, 6) };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        var dlg = new Window
        {
            Title = "Change document type",
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Content = panel
        };
        okBtn.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        cancelBtn.Click += (_, _) => { dlg.DialogResult = false; dlg.Close(); };

        if (dlg.ShowDialog() != true || combo.SelectedItem is not string newTypePicked)
            return;

        var typeToStore = DocumentTypeInfo.NormalizePickerToStorage(newTypePicked);
        if (_store.UpdateDocumentType(doc.Id, typeToStore))
        {
            var oldType = doc.DocumentType ?? "";
            doc.DocumentType = string.IsNullOrEmpty(typeToStore) ? null : typeToStore;
            _changeHistory.RecordFieldChange(doc.Uuid, doc.Id, "document_type", oldType, typeToStore);
            await _auditTrail.LogDocumentActionAsync(AuditAction.DocumentClassified, doc, $"Type changed to {typeToStore}");

            var freshGet = _store.GetResult(doc.Id);
            ClassificationMovesResult? moves = null;
            if (!string.IsNullOrWhiteSpace(typeToStore) && freshGet.IsSuccess && freshGet.Value != null)
            {
                var appConfig = ServiceContainer.GetService<AppConfiguration>();
                var baseDir = !string.IsNullOrEmpty(appConfig.BaseDirectory) ? appConfig.BaseDirectory : Defaults.GetDefaultBaseDir();
                var fileRenameService = ServiceContainer.GetService<IFileRenameService>();
                moves = ClassificationPathHelper.ApplyMovesForDocumentsSharingFiles(
                    _store,
                    fileRenameService,
                    new List<Document> { freshGet.Value },
                    typeToStore,
                    baseDir,
                    d => DocumentFilePathResolver.ResolveExistingPath(d, baseDir));
            }

            var after = _store.GetResult(doc.Id);
            var forDetail = after.IsSuccess && after.Value != null ? after.Value : doc;
            RefreshDetailText(forDetail);
            RunSearch();

            var msg = $"Document type updated to \"{newTypePicked}\".";
            if (moves != null)
                msg += Environment.NewLine + Environment.NewLine + ClassificationPathHelper.FormatMoveFootnote(moves);
            MessageBox.Show(msg, "Change type", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Failed to update document type.", "Change type", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BulkStatusBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = ResultsGrid.SelectedItems.Cast<Document>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select documents first.", "Bulk Action", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var statuses = Enums.StatusValues;
        var choice = Microsoft.VisualBasic.Interaction.InputBox(
            $"Set status for {selected.Count} document(s). Enter: " + string.Join(", ", statuses),
            "Set Status", "Reviewed");
        if (string.IsNullOrWhiteSpace(choice)) return;
        var status = statuses.FirstOrDefault(s => s.Equals(choice.Trim(), StringComparison.OrdinalIgnoreCase));
        if (status == null)
        {
            MessageBox.Show($"Invalid status. Use: {string.Join(", ", statuses)}", "Bulk Action", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ocrService = ServiceContainer.GetService<IOcrService>();
        var configStore = ServiceContainer.GetService<IConfigStore>();
        var updated = 0;
        foreach (var doc in selected)
        {
            var oldStatus = doc.Status;
            if (_store.UpdateStatus(doc.Id, status))
            {
                updated++;
                doc.Status = status;
                _changeHistory.RecordFieldChange(doc.Uuid, doc.Id, "status", oldStatus, status);
                await _auditTrail.LogDocumentActionAsync(AuditAction.DocumentStatusChanged, doc,
                    $"Status set to {status}", oldValue: oldStatus, newValue: status);
                if (string.Equals(status, Enums.Status.ReadyForAudit, StringComparison.Ordinal))
                    DocumentWorkspaceOcr.TryEnqueueOcrIfNeeded(doc, ocrService, configStore);
            }
        }
        MessageBox.Show($"Updated {updated} of {selected.Count} documents to {status}.", "Bulk Action", MessageBoxButton.OK, MessageBoxImage.Information);
        RunSearch();
    }

    private void AssignBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = ResultsGrid.SelectedItems.Cast<Document>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select documents first.", "Assign", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var session = ServiceContainer.GetService<ISessionService>();
        var user = session?.CurrentUser;
        if (user == null)
        {
            MessageBox.Show("You must be logged in to assign documents.", "Assign", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dlg = new Dialogs.AssignDocumentDialog(selected, user);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true)
            RunSearch();
    }
}
