using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Serilog;
using WorkAudit.Config;
using WorkAudit.Core;
using WorkAudit.Core.Export;
using WorkAudit.Core.Helpers;
using WorkAudit.Core.Import;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Core.Reports;
using WorkAudit.Core.TextExtraction;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Views;

/// <summary>
/// Display model for ProcessingView queue DataGrid.
/// </summary>
public class ProcessingDocumentItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isMergeHighlight;

    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string Section { get; set; } = "";
    public string? ExtractedDate { get; set; }
    public Document? Document { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Transient highlight after a merge completes (row moved to bottom).</summary>
    public bool IsMergeHighlight
    {
        get => _isMergeHighlight;
        set
        {
            if (_isMergeHighlight == value) return;
            _isMergeHighlight = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Processing and categorization pipeline view.
/// Step 2 of the audit document lifecycle workflow.
/// </summary>
public partial class ProcessingView : UserControl, IDeleteKeyHandler
{
    private readonly IDocumentStore _store;
    private readonly IDocumentTypeService _docTypeService;
    private readonly IFileRenameService _fileRenameService;
    private readonly IPermissionService _permissionService;
    private readonly IAuditTrailService _auditTrail;
    private readonly ISecureDeleteService _secureDelete;
    private readonly IChangeHistoryService _changeHistory;
    private readonly AppConfiguration _appConfig;
    private readonly IProcessingProgressService _progressService;
    private readonly IProcessingMergeQueueService _mergeQueue;
    private readonly IKeyboardShortcutService _shortcuts;
    private readonly ILogger _log = LoggingService.ForContext<ProcessingView>();
    private DispatcherTimer? _mergeHighlightTimer;
    private readonly ObservableCollection<ProcessingDocumentItem> _items = new();
    private static readonly string[] PreviewExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".tif" };
    private readonly PdfPreviewLoadCoordinator _pdfPreviewLoad = new();
    private bool _previewCollapsed;
    private bool _viewLoaded;
    /// <summary>Row index for Shift+click range checkbox selection; reset when the queue reloads.</summary>
    private int _selectionAnchorIndex = -1;

    public ProcessingView()
    {
        _store = ServiceContainer.GetService<IDocumentStore>();
        _docTypeService = ServiceContainer.GetService<IDocumentTypeService>();
        _fileRenameService = ServiceContainer.GetService<IFileRenameService>();
        _appConfig = ServiceContainer.GetService<AppConfiguration>();
        _permissionService = ServiceContainer.GetService<IPermissionService>();
        _auditTrail = ServiceContainer.GetService<IAuditTrailService>();
        _secureDelete = ServiceContainer.GetService<ISecureDeleteService>();
        _changeHistory = ServiceContainer.GetService<IChangeHistoryService>();
        _progressService = ServiceContainer.GetService<IProcessingProgressService>();
        _mergeQueue = ServiceContainer.GetService<IProcessingMergeQueueService>();
        _shortcuts = ServiceContainer.GetService<IKeyboardShortcutService>();
        InitializeComponent();

        if (QueueGrid != null)
            QueueGrid.ItemsSource = _items;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _viewLoaded = true;
        ProcessingPdfPreviewTools?.SetTargetViewer(PdfPreviewViewer);
        ApplyLocalization();
        if (FilterType != null)
        {
            var config = ServiceContainer.GetService<IConfigStore>();
            FilterType.Items.Clear();
            FilterType.Items.Add(new ComboBoxItem { Content = ReportLocalizationService.GetString("AllTypes", config) });
            FilterType.Items.Add(new ComboBoxItem { Content = DocumentTypeInfo.UnclassifiedType });
            foreach (var t in _docTypeService.GetAllDocumentTypes())
                FilterType.Items.Add(new ComboBoxItem { Content = t });
            FilterType.SelectedIndex = 0;
        }
        RefreshData();
        _mergeQueue.MergeCompleted += OnMergeQueueCompleted;
        _mergeQueue.MergeFailed += OnMergeQueueFailed;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _mergeQueue.MergeCompleted -= OnMergeQueueCompleted;
        _mergeQueue.MergeFailed -= OnMergeQueueFailed;
    }

    private void OnMergeQueueCompleted(object? sender, ProcessingMergeCompletedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            PresentMergedDocument(e.ImportedDocumentId, e.NextSelectionDocumentId)));
    }

    private void OnMergeQueueFailed(object? sender, ProcessingMergeFailedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            var config = ServiceContainer.GetService<IConfigStore>();
            var title = ReportLocalizationService.GetString("MergeToPdfTitle", config);
            MessageBox.Show(e.Message, title, MessageBoxButton.OK,
                e.ShowAsWarning ? MessageBoxImage.Warning : MessageBoxImage.Error);
        }));
    }

    /// <summary>Refresh list, move merged row to bottom (color highlight only), select the following row, focus grid.</summary>
    private void PresentMergedDocument(int importedDocumentId, int? nextSelectionDocumentId)
    {
        RefreshData(highlightMergedDocumentId: importedDocumentId, deferPreviewUntilAfterSelection: true);
        QueueGrid?.Items.Refresh();

        ProcessingDocumentItem? selectRow = null;
        if (nextSelectionDocumentId.HasValue)
        {
            foreach (var row in _items)
            {
                if (row.Id == nextSelectionDocumentId.Value && row.Id != importedDocumentId)
                {
                    selectRow = row;
                    break;
                }
            }
        }

        if (selectRow == null)
        {
            foreach (var row in _items)
            {
                if (row.Id != importedDocumentId)
                {
                    selectRow = row;
                    break;
                }
            }
        }

        ProcessingDocumentItem? mergedRow = null;
        foreach (var row in _items)
        {
            if (row.Id == importedDocumentId)
            {
                mergedRow = row;
                break;
            }
        }

        if (selectRow != null && QueueGrid != null)
        {
            QueueGrid.SelectedItem = selectRow;
            QueueGrid.ScrollIntoView(selectRow);
        }
        else if (QueueGrid != null)
            QueueGrid.SelectedItem = null;

        if (mergedRow != null && QueueGrid != null)
            QueueGrid.ScrollIntoView(mergedRow);

        UpdateSelectionState();
        UpdatePreviewFromSelection();
        FocusQueueGridForKeyboardNavigation();
        ScheduleMergeHighlightClear();
    }

    /// <summary>First document below the bottom-most merged row in the current grid (before refresh).</summary>
    private int? ResolveNextSelectionDocumentIdAfterMerge(IReadOnlyList<Document> mergeable)
    {
        var mergedIds = new HashSet<int>(mergeable.Select(d => d.Id));
        var maxMergedIdx = -1;
        for (var i = 0; i < _items.Count; i++)
        {
            var doc = _items[i].Document;
            if (doc != null && mergedIds.Contains(doc.Id))
                maxMergedIdx = i;
        }

        for (var j = maxMergedIdx + 1; j < _items.Count; j++)
        {
            var doc = _items[j].Document;
            if (doc != null && !mergedIds.Contains(doc.Id))
                return doc.Id;
        }

        return null;
    }

    private void ScheduleMergeHighlightClear()
    {
        _mergeHighlightTimer?.Stop();
        _mergeHighlightTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _mergeHighlightTimer.Tick -= MergeHighlightTimer_Tick;
        _mergeHighlightTimer.Tick += MergeHighlightTimer_Tick;
        _mergeHighlightTimer.Start();
    }

    private void MergeHighlightTimer_Tick(object? sender, EventArgs e)
    {
        if (_mergeHighlightTimer != null)
            _mergeHighlightTimer.Stop();
        foreach (var row in _items)
            row.IsMergeHighlight = false;
        QueueGrid?.Items.Refresh();
    }

    private static ProcessingDocumentItem CreateProcessingDocumentItem(Document doc, IConfigStore config)
    {
        var fileName = string.IsNullOrEmpty(doc.FilePath) ? "-" : Path.GetFileName(doc.FilePath);
        return new ProcessingDocumentItem
        {
            Id = doc.Id,
            FileName = fileName,
            DocumentType = ReportLocalizationService.GetDocumentTypeListLabel(doc.DocumentType, config),
            Section = doc.Section ?? Enums.SectionValues[0],
            ExtractedDate = doc.ExtractedDate ?? "-",
            Document = doc
        };
    }

    private void ApplyLocalization()
    {
        var config = ServiceContainer.GetService<IConfigStore>();
        if (ShownSectionLabel != null) ShownSectionLabel.Text = ReportLocalizationService.GetString("ShownLabel", config);
        if (QueueCountLabel != null) QueueCountLabel.Text = ReportLocalizationService.GetString("DocumentsCount", config, 0);
        if (RefreshBtn != null)
        {
            RefreshBtn.Content = ReportLocalizationService.GetString("Refresh", config);
            RefreshBtn.ToolTip = ReportLocalizationService.GetString("ProcessingRefreshTooltip", config,
                _shortcuts.GetDisplayString(KeyboardShortcutIds.ProcessingRefresh));
        }
        if (SelectAllSidebarBtn != null)
        {
            SelectAllSidebarBtn.Content = ReportLocalizationService.GetString("SelectAll", config);
            SelectAllSidebarBtn.ToolTip = ReportLocalizationService.GetString("SelectAllTooltip", config,
                _shortcuts.GetDisplayString(KeyboardShortcutIds.ProcessingSelectAllChecks),
                _shortcuts.GetDisplayString(KeyboardShortcutIds.ProcessingGridSelectAll));
        }
        if (SetTypeSectionBtn != null) SetTypeSectionBtn.Content = ReportLocalizationService.GetString("SetTypeSection", config);
        if (MergeToPdfBtn != null)
        {
            MergeToPdfBtn.Content = ReportLocalizationService.GetString("MergeToPdf", config);
            MergeToPdfBtn.ToolTip = ReportLocalizationService.GetString("MergeToPdfTooltipExtended", config,
                _shortcuts.GetDisplayString(KeyboardShortcutIds.ProcessingMerge),
                _shortcuts.GetDisplayString(KeyboardShortcutIds.ProcessingMergeAlternate));
        }
        if (MergeToPdfToolbarBtn != null)
        {
            MergeToPdfToolbarBtn.Content = ReportLocalizationService.GetString("MergeToPdf", config);
            MergeToPdfToolbarBtn.ToolTip = ReportLocalizationService.GetString("MergeToPdfTooltipExtended", config,
                _shortcuts.GetDisplayString(KeyboardShortcutIds.ProcessingMerge),
                _shortcuts.GetDisplayString(KeyboardShortcutIds.ProcessingMergeAlternate));
        }
        if (DeleteSelectedBtn != null) DeleteSelectedBtn.Content = ReportLocalizationService.GetString("DeleteSelected", config);
        if (FiltersLabel != null) FiltersLabel.Text = ReportLocalizationService.GetString("FilterByType", config);
        if (ClearFiltersBtn != null) ClearFiltersBtn.Content = ReportLocalizationService.GetString("ClearFilter", config);
        if (RecentlyProcessedLabel != null) RecentlyProcessedLabel.Text = ReportLocalizationService.GetString("RecentlyProcessedDocuments", config);
        if (SelectAllBtn != null) SelectAllBtn.Content = ReportLocalizationService.GetString("SelectAll", config);
        if (ClearSelectionBtn != null)
        {
            ClearSelectionBtn.Content = ReportLocalizationService.GetString("Clear", config);
            ClearSelectionBtn.ToolTip = ReportLocalizationService.GetString("ProcessingClearSelectionTooltip", config,
                _shortcuts.GetDisplayString(KeyboardShortcutIds.ProcessingClearChecks));
        }
        if (EmptyStateText != null) EmptyStateText.Text = ReportLocalizationService.GetString("NoDocumentsLast7Days", config);
        if (TogglePreviewBtn != null) TogglePreviewBtn.ToolTip = ReportLocalizationService.GetString("HidePreview", config);
        if (DocumentPreviewLabel != null) DocumentPreviewLabel.Text = ReportLocalizationService.GetString("DocumentPreview", config);
        if (PreviewPlaceholderText != null) PreviewPlaceholderText.Text = ReportLocalizationService.GetString("SelectDocumentToPreview", config);
        if (SummaryLabel != null) SummaryLabel.Text = ReportLocalizationService.GetString("Summary", config);
        if (FinishBtn != null) FinishBtn.Content = ReportLocalizationService.GetString("MoveToWorkspace", config);
    }

    private void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        RefreshData();
    }

    /// <param name="highlightMergedDocumentId">When set, <see cref="PresentMergedDocument"/> will fix selection after refresh; skip preview update to avoid stale grid selection pointing at deleted merge sources.</param>
    private void RefreshData(int? highlightMergedDocumentId = null, bool deferPreviewUntilAfterSelection = false)
    {
        if (_store == null) return;
        var config = ServiceContainer.GetService<IConfigStore>();
        var dateTo = DateTime.UtcNow;
        var dateFrom = dateTo.AddDays(-7);
        var dateFromStr = dateFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var dateToStr = dateTo.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var branch = _permissionService.GetEffectiveDocumentListBranchFilter();

        string? filterType = null;
        var allTypesStr = ReportLocalizationService.GetString("AllTypes", config);
        if (FilterType?.SelectedItem is ComboBoxItem typeItem && typeItem.Content is string typeStr && typeStr != allTypesStr)
            filterType = typeStr;

        var documents = _store.ListDocuments(branch: branch, dateFrom: dateFromStr, dateTo: dateToStr, status: null, documentType: filterType, limit: 200, dateFilterField: "capture")
            .Where(d => d.Status != Enums.Status.ReadyForAudit && d.Status != Enums.Status.Cleared && d.Status != Enums.Status.Archived)
            .ToList();

        var session = ServiceContainer.GetService<ISessionService>();
        _log.Debug("ProcessingView RefreshData: branch={Branch}, dateFrom={DateFrom}, dateTo={DateTo}, documentType={DocType}, count={Count}",
            branch ?? "(all)", dateFromStr, dateToStr, filterType ?? "(all)", documents.Count);
        if (session?.CurrentUser == null)
            _log.Debug("ProcessingView: CurrentUser is null, branch filter uses fallback {Branch}", branch ?? Domain.Branches.Default);
        else if (branch != null && branch != session.CurrentUser.Branch)
            _log.Debug("ProcessingView: branch filter {Branch} differs from CurrentUser.Branch {UserBranch}", branch, session.CurrentUser.Branch);

        _items.Clear();
        var total = documents.Count;
        _selectionAnchorIndex = -1;
        foreach (var doc in documents)
            _items.Add(CreateProcessingDocumentItem(doc, config));

        if (highlightMergedDocumentId.HasValue)
        {
            var highlightId = highlightMergedDocumentId.Value;
            ProcessingDocumentItem? toMove = null;
            foreach (var row in _items)
            {
                if (row.Id == highlightId)
                {
                    toMove = row;
                    break;
                }
            }

            if (toMove == null)
            {
                // Always append the merge result last after completion, even if type/date query omitted it from `documents`.
                var extra = _store.Get(highlightId);
                if (extra != null)
                    toMove = CreateProcessingDocumentItem(extra, config);
            }

            if (toMove != null)
            {
                var wasInList = false;
                foreach (var row in _items)
                {
                    if (ReferenceEquals(row, toMove) || row.Id == toMove.Id)
                    {
                        wasInList = true;
                        break;
                    }
                }

                if (wasInList)
                    _items.Remove(toMove);
                foreach (var row in _items)
                    row.IsMergeHighlight = false;
                toMove.IsMergeHighlight = true;
                _items.Add(toMove);
            }
        }

        total = _items.Count;
        if (QueueCountLabel != null) QueueCountLabel.Text = ReportLocalizationService.GetString("DocumentsCount", config, total);
        CompletedCountLabel.Text = ReportLocalizationService.GetString("CompletedLast7Days", config, total);

        var stats = _store.GetStats(branch);
        var needReview = documents.Count(d => !d.IsClassified);
        DraftCountLabel.Text = ReportLocalizationService.GetString("DraftCount", config, stats.DraftCount);
        ReviewedCountLabel.Text = ReportLocalizationService.GetString("ReviewedCount", config, stats.ReviewedCount);
        NeedReviewCountLabel.Text = ReportLocalizationService.GetString("NeedReviewCount", config, needReview);
        TotalCountLabel.Text = ReportLocalizationService.GetString("TotalCount", config, stats.TotalDocuments);

        if (EmptyStateOverlay != null) EmptyStateOverlay.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (EmptyStateText != null) EmptyStateText.Text = total == 0 ? ReportLocalizationService.GetString("NoDocumentsLast7Days", config) : "";

        ClearQueueGridViewSort();
        UpdateSelectionState();
        if (!deferPreviewUntilAfterSelection)
            UpdatePreviewFromSelection();
    }

    /// <summary>WPF DataGrid uses ListCollectionView sort; clear so row order matches <see cref="_items"/> (e.g. merged row last).</summary>
    private void ClearQueueGridViewSort()
    {
        var view = CollectionViewSource.GetDefaultView(_items);
        view?.SortDescriptions.Clear();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_viewLoaded)
            RefreshData();
    }

    private void ClearFiltersBtn_Click(object sender, RoutedEventArgs e)
    {
        if (FilterType != null) FilterType.SelectedIndex = 0;
        RefreshData();
    }

    private void FinishBtn_Click(object sender, RoutedEventArgs e)
    {
        if (QueueGrid == null) return;
        var config = ServiceContainer.GetService<IConfigStore>();
        var selected = GetCheckedItemsInGridOrder().ToList();
        if (selected.Count == 0)
        {
            var msg = config != null ? ReportLocalizationService.GetString("SelectDocumentsFirst", config) : "Please select one or more documents first.";
            MessageBox.Show(msg, "Move to Workspace", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        var docs = selected.Select(x => x.Document!).ToList();
        
        // Validate that all documents have valid file paths before moving to workspace
        var docsWithInvalidPaths = docs.Where(d => string.IsNullOrEmpty(d.FilePath) || !System.IO.File.Exists(d.FilePath)).ToList();
        if (docsWithInvalidPaths.Count > 0)
        {
            var docIds = string.Join(", ", docsWithInvalidPaths.Select(d => d.Id));
            var msg = docsWithInvalidPaths.Count == 1
                ? $"Document {docIds} has an invalid or missing file path and cannot be moved to workspace.\n\nPlease check that the file exists and the document record is correct."
                : $"{docsWithInvalidPaths.Count} documents ({docIds}) have invalid or missing file paths and cannot be moved to workspace.\n\nPlease check that the files exist and the document records are correct.";
            
            var result = MessageBox.Show(msg + "\n\nDo you want to skip these documents and move the others?", 
                "Invalid File Paths Detected", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.No)
                return;
            
            // Remove invalid docs from the list
            docs = docs.Except(docsWithInvalidPaths).ToList();
            if (docs.Count == 0)
            {
                MessageBox.Show("No valid documents to move.", "Move to Workspace", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        
        var targetStatus = Enums.Status.ReadyForAudit;
        var auditTrail = ServiceContainer.GetService<IAuditTrailService>();
        var changeHistory = ServiceContainer.GetService<IChangeHistoryService>();
        var ocrService = ServiceContainer.GetService<IOcrService>();
        var updated = 0;
        var statusUpdatedDocs = new List<Document>();
        try
        {
            foreach (var doc in docs)
            {
                if (doc.Status == Enums.Status.Archived) continue;
                var pathBefore = doc.FilePath;
                _log.Information("MoveToWorkspace: doc {DocId} file_path before status update: {Path}", doc.Id, pathBefore ?? "(null)");
                var oldStatus = doc.Status;
                if (!_store.UpdateStatus(doc.Id, targetStatus)) continue;
                updated++;
                statusUpdatedDocs.Add(doc);
                doc.Status = targetStatus;
                changeHistory.RecordFieldChange(doc.Uuid, doc.Id, "status", oldStatus, targetStatus);
                _ = auditTrail.LogDocumentActionAsync(AuditAction.DocumentStatusChanged, doc, $"Status set to {targetStatus}");
                DocumentWorkspaceOcr.TryEnqueueOcrIfNeeded(doc, ocrService, config);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Move to Workspace failed");
            MessageBox.Show($"Error: {ex.Message}", "Move to Workspace", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // After status update: reload from DB and fix stale file_path (e.g. DB never updated after classify move while UI still had correct path).
        var pathSyncWarnings = new List<string>();
        foreach (var doc in statusUpdatedDocs)
        {
            var reloaded = _store.GetResult(doc.Id);
            if (!reloaded.IsSuccess || reloaded.Value == null)
            {
                _log.Warning("MoveToWorkspace: could not reload doc {DocId} after status update", doc.Id);
                pathSyncWarnings.Add($"Document {doc.Id}: could not reload from database.");
                continue;
            }

            var fromDb = reloaded.Value;
            var dbPathOk = !string.IsNullOrEmpty(fromDb.FilePath) && File.Exists(fromDb.FilePath);
            var sessionPathOk = !string.IsNullOrEmpty(doc.FilePath) && File.Exists(doc.FilePath);

            _log.Information("MoveToWorkspace: doc {DocId} after status — DB path: {DbPath}, session path: {SessionPath}", doc.Id, fromDb.FilePath ?? "(null)", doc.FilePath ?? "(null)");

            if (dbPathOk)
            {
                // DB is source of truth when it points to an existing file.
                if (!string.Equals(doc.FilePath, fromDb.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    doc.FilePath = fromDb.FilePath ?? "";
                    doc.DocumentType = fromDb.DocumentType;
                    doc.Section = string.IsNullOrEmpty(fromDb.Section) ? doc.Section : fromDb.Section;
                    _log.Information("MoveToWorkspace: synced session document {DocId} file_path from database", doc.Id);
                }
                continue;
            }

            // DB path missing or file not on disk — recover from session if we still have a valid path.
            if (sessionPathOk)
            {
                var sessionPath = doc.FilePath!;
                if (_store.UpdateDocumentFilePath(doc.Id, sessionPath))
                {
                    _log.Warning("MoveToWorkspace: restored file_path in DB for doc {DocId} from session path (DB was stale): {Path}", doc.Id, doc.FilePath);
                }
                else
                {
                    _log.Error("MoveToWorkspace: failed to persist restored file_path for doc {DocId}", doc.Id);
                    pathSyncWarnings.Add($"Document {doc.Id}: could not save corrected file path to database.");
                }
                continue;
            }

            _log.Warning("MoveToWorkspace: doc {DocId} has no valid file path in DB or session after status update", doc.Id);
            pathSyncWarnings.Add($"Document {doc.Id}: file path is missing or the file was not found on disk.");
        }

        if (pathSyncWarnings.Count > 0)
        {
            var detail = string.Join(Environment.NewLine, pathSyncWarnings);
            MessageBox.Show(
                "One or more documents may have a storage path problem after moving to Workspace:" + Environment.NewLine + Environment.NewLine + detail +
                Environment.NewLine + Environment.NewLine + "If the files exist on disk, restore them to the expected location or correct the stored paths.",
                "Move to Workspace — path check",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        UncheckAllItems();
        RefreshData();
        UpdatePreviewFromSelection();
        if (updated > 0)
        {
            DocumentsMovedToWorkspaceRequested.Raise(updated);
            var msg = config != null
                ? ReportLocalizationService.GetString("DocumentsMovedToWorkspace", config, updated)
                : $"{updated} document(s) moved to Workspace.";
            MessageBox.Show(msg, "Move to Workspace", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("No documents were updated. They may already be archived.", "Move to Workspace", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateSelectionState()
    {
        var checkedItems = GetCheckedItemsInGridOrder().ToList();
        var count = checkedItems.Count;
        var hasAny = count > 0;
        if (SetTypeSectionBtn != null) SetTypeSectionBtn.IsEnabled = hasAny;
        var mergeEnabled = CountMergeableSelectedForPdf() >= 2;
        if (MergeToPdfBtn != null) MergeToPdfBtn.IsEnabled = mergeEnabled;
        if (MergeToPdfToolbarBtn != null) MergeToPdfToolbarBtn.IsEnabled = mergeEnabled;
        if (DeleteSelectedBtn != null) DeleteSelectedBtn.IsEnabled = hasAny;
        // FinishBtn always enabled so user gets feedback when clicking (e.g. "Select documents first")
        SelectedCountLabel.Text = count > 0 ? $"{count} selected" : "";
    }

    private IEnumerable<ProcessingDocumentItem> GetCheckedItemsInGridOrder() =>
        _items.Where(x => x.IsSelected && x.Document != null);

    private void UncheckAllItems()
    {
        _selectionAnchorIndex = -1;
        foreach (var row in _items)
            row.IsSelected = false;
    }

    private void ProcessingView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryProcessProcessingShortcut(e, KeyboardShortcutIds.ProcessingMergeAlternate,
                () => { if (MergeToPdfBtn != null && MergeToPdfBtn.IsEnabled) MergeToPdfBtn_Click(MergeToPdfBtn, e); }))
            return;

        if (TryProcessProcessingShortcut(e, KeyboardShortcutIds.ProcessingMerge,
                () => { if (MergeToPdfBtn != null && MergeToPdfBtn.IsEnabled) MergeToPdfBtn_Click(MergeToPdfBtn, e); }))
            return;

        if (TryProcessProcessingShortcut(e, KeyboardShortcutIds.ProcessingRefresh,
                () => { if (RefreshBtn != null) RefreshBtn_Click(RefreshBtn, e); }))
            return;

        if (TryProcessProcessingShortcut(e, KeyboardShortcutIds.ProcessingSelectAllChecks,
                () => SelectAllBtn_Click(sender, e)))
            return;

        if (TryProcessProcessingShortcut(e, KeyboardShortcutIds.ProcessingClearChecks,
                () => { UncheckAllItems(); UpdateSelectionState(); }))
            return;

        if (TryProcessProcessingShortcut(e, KeyboardShortcutIds.ProcessingUncheckRow,
                () =>
                {
                    if (QueueGrid?.SelectedItem is ProcessingDocumentItem row && row.Document != null)
                    {
                        row.IsSelected = false;
                        UpdateSelectionState();
                    }
                }))
            return;
    }

    private bool TryProcessProcessingShortcut(KeyEventArgs e, string commandId, Action action)
    {
        if (!_shortcuts.Matches(e, commandId)) return false;
        if (RequiresLetterFocusGuard(e) && !ShouldProcessLetterShortcuts()) return false;
        action();
        e.Handled = true;
        return true;
    }

    private static bool RequiresLetterFocusGuard(KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return false;
        return e.Key is >= Key.A and <= Key.Z;
    }

    /// <summary>Returns false when focus is in a control that should receive letter keys for typing.</summary>
    private static bool ShouldProcessLetterShortcuts()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (focused == null) return true;
        if (FindVisualAncestor<System.Windows.Controls.TextBox>(focused) != null) return false;
        if (FindVisualAncestor<PasswordBox>(focused) != null) return false;
        if (FindVisualAncestor<System.Windows.Controls.RichTextBox>(focused) != null) return false;
        if (FindVisualAncestor<System.Windows.Controls.ComboBox>(focused) != null) return false;
        return true;
    }

    private void QueueGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (QueueGrid == null) return;
        if (FindVisualAncestor<System.Windows.Controls.CheckBox>(e.OriginalSource as DependencyObject) != null)
            return;
        var row = FindVisualAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not ProcessingDocumentItem item) return;
        var index = _items.IndexOf(item);
        if (index < 0) return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            if (_selectionAnchorIndex >= 0)
            {
                var a = Math.Min(_selectionAnchorIndex, index);
                var b = Math.Max(_selectionAnchorIndex, index);
                for (var i = a; i <= b; i++)
                    _items[i].IsSelected = true;
                e.Handled = true;
                UpdateSelectionState();
            }
            else
            {
                _selectionAnchorIndex = index;
                item.IsSelected = true;
                e.Handled = true;
                UpdateSelectionState();
            }
            return;
        }

        _selectionAnchorIndex = index;
        EnsureCurrentRowChecked();
    }

    /// <summary>Keeps the checkbox for the preview row checked (accumulates with other checked rows).</summary>
    private void EnsureCurrentRowChecked()
    {
        if (QueueGrid?.SelectedItem is not ProcessingDocumentItem item || item.Document == null)
            return;
        item.IsSelected = true;
    }

    /// <summary>
    /// Moves keyboard focus to the grid/selected row. Selection alone does not receive arrow keys while focus stays on the merge button.
    /// Uses <see cref="DispatcherPriority.ApplicationIdle"/> so focus runs after progress UI and layout (including row containers).
    /// </summary>
    private void FocusQueueGridForKeyboardNavigation()
    {
        if (QueueGrid == null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (QueueGrid == null) return;
            var item = QueueGrid.SelectedItem;
            if (item == null)
            {
                Keyboard.Focus(QueueGrid);
                return;
            }

            QueueGrid.ScrollIntoView(item);
            QueueGrid.UpdateLayout();

            if (QueueGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
            {
                row.Focusable = true;
                Keyboard.Focus(row);
                return;
            }

            Keyboard.Focus(QueueGrid);
        }));
    }

    private static T? FindVisualAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match)
                return match;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void QueueRowCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateSelectionState));
    }

    /// <summary>Resolves a path that exists on disk (absolute, or relative to the app base directory).</summary>
    private string? ResolveExistingDocumentPath(Document? doc)
    {
        if (doc == null) return null;
        var baseDir = !string.IsNullOrEmpty(_appConfig.BaseDirectory) ? _appConfig.BaseDirectory : Defaults.GetDefaultBaseDir();
        return DocumentFilePathResolver.ResolveExistingPath(doc, baseDir);
    }

    private int CountMergeableSelectedForPdf()
    {
        var n = 0;
        foreach (var row in _items)
        {
            if (!row.IsSelected) continue;
            var doc = row.Document;
            if (doc == null) continue;
            if (!_permissionService.CanAccessDocument(doc)) continue;
            var resolved = ResolveExistingDocumentPath(doc);
            if (resolved == null) continue;
            if (!SearchExportService.IsSupportedForCombinedPdfExport(resolved)) continue;
            n++;
        }
        return n;
    }

    private List<Document> GetOrderedMergeableDocumentsForMerge(out int skippedLocked, out int skippedOther)
    {
        skippedLocked = 0;
        skippedOther = 0;
        var result = new List<Document>();
        foreach (var row in _items)
        {
            if (!row.IsSelected) continue;
            var doc = row.Document;
            if (doc == null) continue;
            if (!_permissionService.CanAccessDocument(doc))
            {
                skippedLocked++;
                continue;
            }
            var resolvedPath = ResolveExistingDocumentPath(doc);
            if (resolvedPath == null)
            {
                skippedOther++;
                continue;
            }
            if (!SearchExportService.IsSupportedForCombinedPdfExport(resolvedPath))
            {
                skippedOther++;
                continue;
            }
            result.Add(doc);
        }
        return result;
    }

    private static string BuildMergeLossyRasterFileListBlock(IReadOnlyList<string> fullPaths, IConfigStore config)
    {
        const int maxNames = 8;
        var distinct = fullPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var sb = new StringBuilder();
        for (var i = 0; i < distinct.Count && i < maxNames; i++)
        {
            var name = Path.GetFileName(distinct[i]);
            if (!string.IsNullOrEmpty(name))
                sb.AppendLine(name);
        }

        if (distinct.Count > maxNames)
            sb.AppendLine(ReportLocalizationService.GetString("MergeToPdfLossyRasterMoreFiles", config, distinct.Count - maxNames));
        return sb.ToString().TrimEnd();
    }

    private void MergeToPdfBtn_Click(object sender, RoutedEventArgs e)
    {
        var config = ServiceContainer.GetService<IConfigStore>();
        var title = ReportLocalizationService.GetString("MergeToPdfTitle", config);
        var mergeable = GetOrderedMergeableDocumentsForMerge(out var skippedLocked, out var skippedOther);
        if (mergeable.Count < 2)
        {
            var msg = ReportLocalizationService.GetString("MergeToPdfNeedTwo", config);
            if (skippedLocked > 0)
                msg += Environment.NewLine + Environment.NewLine + ReportLocalizationService.GetString("MergeToPdfLockedExcluded", config, skippedLocked);
            MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (skippedLocked > 0 || skippedOther > 0)
            _log.Information("Merge to PDF: skipped {Locked} inaccessible and {Other} non-mergeable selected item(s).", skippedLocked, skippedOther);

        var cannotDeleteSource = mergeable.Where(d => !_permissionService.CanDeleteDocument(d)).ToList();
        if (cannotDeleteSource.Count > 0)
        {
            MessageBox.Show(ReportLocalizationService.GetString("MergeToPdfCannotDeleteSources", config), title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var lossyPdfPaths = new List<string>();
        foreach (var d in mergeable)
        {
            var p = ResolveExistingDocumentPath(d);
            if (p == null) continue;
            if (!string.Equals(Path.GetExtension(p), ".pdf", StringComparison.OrdinalIgnoreCase)) continue;
            if (!SearchExportService.CanMergePdfLosslessly(p))
                lossyPdfPaths.Add(p);
        }

        lossyPdfPaths = lossyPdfPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool allowLossyPdfFallback;
        if (lossyPdfPaths.Count > 0)
        {
            var fileBlock = BuildMergeLossyRasterFileListBlock(lossyPdfPaths, config);
            var msg = ReportLocalizationService.GetString("MergeToPdfLossyRasterPrompt", config, fileBlock);
            var mbTitle = ReportLocalizationService.GetString("MergeToPdfLossyRasterTitle", config);
            var result = MessageBox.Show(msg, mbTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
                return;
            allowLossyPdfFallback = true;
        }
        else
        {
            allowLossyPdfFallback = false;
        }

        var ids = mergeable.Select(d => d.Id).ToList();
        var nextSelectId = ResolveNextSelectionDocumentIdAfterMerge(mergeable);
        try
        {
            _mergeQueue.Enqueue(ids, nextSelectId, allowLossyPdfFallback);
            var idSet = new HashSet<int>(ids);
            foreach (var row in _items)
            {
                if (idSet.Contains(row.Id))
                    row.IsSelected = false;
            }
            UpdateSelectionState();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Merge enqueue failed");
            MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdatePreviewFromSelection()
    {
        if (DocumentPreviewViewer == null || PreviewPlaceholder == null) return;
        if (QueueGrid == null) return;
        var item = QueueGrid.SelectedItem as ProcessingDocumentItem;
        if (item != null && !_items.Contains(item))
        {
            var replacement = _items.FirstOrDefault(x => x.Id == item.Id);
            QueueGrid.SelectedItem = replacement;
            item = replacement;
        }

        if (item == null)
        {
            DocumentPreviewViewer.Clear();
            DocumentPreviewViewer.Visibility = Visibility.Collapsed;
            SetPdfPreviewSource(null);
            if (PdfPreviewViewer != null) PdfPreviewViewer.Visibility = Visibility.Collapsed;
            if (ProcessingPdfPreviewTools != null) ProcessingPdfPreviewTools.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewPlaceholderText.Text = "Select a document to preview.";
            return;
        }

        var doc = _store?.Get(item.Id) ?? item.Document;
        if (doc == null)
        {
            DocumentPreviewViewer.Clear();
            DocumentPreviewViewer.Visibility = Visibility.Collapsed;
            SetPdfPreviewSource(null);
            if (PdfPreviewViewer != null) PdfPreviewViewer.Visibility = Visibility.Collapsed;
            if (ProcessingPdfPreviewTools != null) ProcessingPdfPreviewTools.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewPlaceholderText.Text = "Document no longer available.";
            return;
        }

        item.Document = doc;
        var path = ResolveExistingDocumentPath(doc);
        if (string.IsNullOrEmpty(doc.FilePath))
        {
            DocumentPreviewViewer.Clear();
            DocumentPreviewViewer.Visibility = Visibility.Collapsed;
            SetPdfPreviewSource(null);
            if (PdfPreviewViewer != null) PdfPreviewViewer.Visibility = Visibility.Collapsed;
            if (ProcessingPdfPreviewTools != null) ProcessingPdfPreviewTools.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewPlaceholderText.Text = "No file path.";
            return;
        }
        if (path == null)
        {
            DocumentPreviewViewer.Clear();
            DocumentPreviewViewer.Visibility = Visibility.Collapsed;
            SetPdfPreviewSource(null);
            if (PdfPreviewViewer != null) PdfPreviewViewer.Visibility = Visibility.Collapsed;
            if (ProcessingPdfPreviewTools != null) ProcessingPdfPreviewTools.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewPlaceholderText.Text = "File not found.";
            return;
        }
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".pdf")
        {
            DocumentPreviewViewer.Clear();
            DocumentPreviewViewer.Visibility = Visibility.Collapsed;
            if (ProcessingPdfPreviewTools != null)
            {
                ProcessingPdfPreviewTools.Visibility = Visibility.Visible;
                ProcessingPdfPreviewTools.SetTargetViewer(PdfPreviewViewer);
            }
            if (PdfPreviewViewer != null)
                SetPdfPreviewSource(path, () =>
                {
                    PdfPreviewViewer.Visibility = Visibility.Visible;
                    Dispatcher.BeginInvoke(new Action(() => ProcessingPdfPreviewTools?.SetTargetViewer(PdfPreviewViewer)), DispatcherPriority.Loaded);
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
            if (ProcessingPdfPreviewTools != null) ProcessingPdfPreviewTools.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewPlaceholderText.Text = "Preview not available for this format.";
            return;
        }
        SetPdfPreviewSource(null);
        if (PdfPreviewViewer != null) PdfPreviewViewer.Visibility = Visibility.Collapsed;
        if (ProcessingPdfPreviewTools != null) ProcessingPdfPreviewTools.Visibility = Visibility.Collapsed;
        DocumentPreviewViewer.SetSource(path);
        DocumentPreviewViewer.Visibility = Visibility.Visible;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void SetPdfPreviewSource(string? filePath, Action? onSourceApplied = null)
    {
        _pdfPreviewLoad.SetSource(PdfPreviewViewer, filePath, onSourceApplied);
    }

    private void QueueGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.RemovedItems.Count > 0
            && e.AddedItems[0] is ProcessingDocumentItem added
            && e.RemovedItems[0] is ProcessingDocumentItem removed)
        {
            var newIdx = _items.IndexOf(added);
            var oldIdx = _items.IndexOf(removed);
            if (newIdx >= 0 && oldIdx >= 0 && newIdx < oldIdx)
            {
                removed.IsSelected = false;
                UpdateSelectionState();
            }
        }

        var first = QueueGrid?.SelectedItem as ProcessingDocumentItem;
        Document? ctx = null;
        if (first != null)
        {
            ctx = _store.Get(first.Id) ?? first.Document;
            if (ctx != null)
                first.Document = ctx;
        }
        ServiceContainer.GetService<ICurrentDocumentContextService>().SetCurrentDocument(ctx);
        EnsureCurrentRowChecked();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdatePreviewFromSelection));
    }

    private void TogglePreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        _previewCollapsed = !_previewCollapsed;
        if (_previewCollapsed)
        {
            PreviewColumn.Width = new GridLength(0);
            TogglePreviewBtn.Content = "▶";
            TogglePreviewBtn.ToolTip = "Show preview";
        }
        else
        {
            PreviewColumn.Width = new GridLength(3, GridUnitType.Star);
            TogglePreviewBtn.Content = "◀";
            TogglePreviewBtn.ToolTip = "Hide preview";
        }
    }

    private void SelectAllBtn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _items)
            row.IsSelected = true;
        UpdateSelectionState();
    }

    private void ClearSelectionBtn_Click(object sender, RoutedEventArgs e)
    {
        UncheckAllItems();
        UpdateSelectionState();
    }

    private void QueueGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (QueueGrid == null) return;
        if (_shortcuts.Matches(e, KeyboardShortcutIds.ProcessingGridSelectAll))
        {
            SelectAllBtn_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (_shortcuts.Matches(e, KeyboardShortcutIds.ProcessingSetTypeSection))
        {
            if (SetTypeSectionBtn != null && SetTypeSectionBtn.IsEnabled) SetTypeSectionBtn_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (_shortcuts.Matches(e, KeyboardShortcutIds.ProcessingDeleteSelected))
        {
            DeleteSelectedDocuments();
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    public bool TryHandleDeleteKey()
    {
        if (!GetCheckedItemsInGridOrder().Any()) return false;
        DeleteSelectedDocuments();
        return true;
    }

    private void DeleteSelectedBtn_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedDocuments();
    }

    private async void DeleteSelectedDocuments()
    {
        var selected = GetCheckedItemsInGridOrder().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select one or more documents to delete.", "Delete", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var denied = selected.Where(x => x.Document != null && !_permissionService.CanDeleteDocument(x.Document)).ToList();
        if (denied.Count > 0)
        {
            MessageBox.Show($"You do not have permission to delete {denied.Count} document(s).", "Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var count = selected.Count;
        var message = count == 1
            ? "Permanently delete this document? This cannot be undone."
            : $"Permanently delete {count} selected documents? This cannot be undone.";
        if (MessageBox.Show(message, "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var deleted = 0;
        foreach (var item in selected)
        {
            if (item.Document == null) continue;
            var doc = item.Document;
            try
            {
                await _auditTrail.LogDocumentActionAsync(AuditAction.DocumentDeleted, doc, "Deleted from Process tab");
                if (!string.IsNullOrEmpty(doc.FilePath) && System.IO.File.Exists(doc.FilePath))
                    _secureDelete.SecureDelete(doc.FilePath);
                if (_store.Delete(doc.Id))
                    deleted++;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete document {doc.Id}: {ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        if (deleted > 0)
        {
            RefreshData();
            MessageBox.Show(deleted == count ? $"{deleted} document(s) deleted." : $"{deleted} of {count} document(s) deleted.", "Delete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static bool TryUpdateWithRetries(Func<bool> attempt, int maxAttempts = 6)
    {
        // Exponential backoff: 50ms, 100ms, 200ms, 400ms, 800ms, 1600ms
        var delays = new[] { 50, 100, 200, 400, 800, 1600 };
        
        for (var i = 0; i < maxAttempts; i++)
        {
            if (attempt()) return true;
            
            if (i < maxAttempts - 1)
            {
                Thread.Sleep(delays[Math.Min(i, delays.Length - 1)]);
            }
        }
        return false;
    }

    private void SetTypeSectionBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetCheckedItemsInGridOrder().ToList();
        if (selected.Count == 0)
        {
            var config = ServiceContainer.GetService<IConfigStore>();
            MessageBox.Show(ReportLocalizationService.GetString("SelectDocumentsFirst", config), ReportLocalizationService.GetString("SetTypeSection", config), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var first = selected[0].Document!;
        var config2 = ServiceContainer.GetService<IConfigStore>();
        var title = selected.Count > 1
            ? $"{ReportLocalizationService.GetString("SetTypeSection", config2)} ({selected.Count} documents)"
            : ReportLocalizationService.GetString("SetTypeSection", config2);

        var result = DialogHelper.ShowSectionFilteredTypeDialog(
            title,
            ReportLocalizationService.GetString("TypeLabel", config2),
            ReportLocalizationService.GetString("SectionLabel", config2),
            Enums.SectionValues,
            string.IsNullOrEmpty(first.Section) ? Enums.SectionValues[0] : first.Section,
            selectedSection => DocumentTypeInfo.BuildPickerItems(_docTypeService.GetDocumentTypesForSection(selectedSection)),
            DocumentTypeInfo.PickerSelectedValue(first.DocumentType),
            Window.GetWindow(this));

        if (result == null || result.Value.documentType == null || result.Value.section == null)
            return;

        var newTypePicked = result.Value.documentType;
        var typeToStore = DocumentTypeInfo.NormalizePickerToStorage(newTypePicked);
        var newSection = result.Value.section;
        if (selected.Count > 1)
        {
            var confirmMsg = $"Apply type \"{newTypePicked}\" and section \"{newSection}\" to {selected.Count} documents?";
            if (MessageBox.Show(confirmMsg, ReportLocalizationService.GetString("SetTypeSection", config2), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
        }
        var baseDir = !string.IsNullOrEmpty(_appConfig.BaseDirectory) ? _appConfig.BaseDirectory : Defaults.GetDefaultBaseDir();
        var success = 0;
        var failedDocs = new List<int>();
        var failedReasons = new Dictionary<int, string>();
        var docsWithTypeUpdate = new List<Document>();
        foreach (var item in selected)
        {
            var doc = item.Document!;
            var typeOk = TryUpdateWithRetries(() => _store.UpdateDocumentType(doc.Id, typeToStore));
            var sectionOk = TryUpdateWithRetries(() => _store.UpdateDocumentSection(doc.Id, newSection));
            
            // CRITICAL FIX: Fail-fast if EITHER update fails (not just both)
            // This prevents partial updates from proceeding to file rename
            if (!typeOk || !sectionOk)
            {
                failedDocs.Add(doc.Id);
                failedReasons[doc.Id] = $"Type update: {typeOk}, section update: {sectionOk}.";
                _log.Warning("Document {DocId} classification failed: typeOk={TypeOk}, sectionOk={SectionOk}", doc.Id, typeOk, sectionOk);
                continue;
            }
            // Apply type/section on the in-memory document before rename/move so FileRenameService uses the new folder path.
            var oldType = doc.DocumentType ?? "";
            if (typeOk)
            {
                doc.DocumentType = string.IsNullOrEmpty(typeToStore) ? null : typeToStore;
                item.DocumentType = ReportLocalizationService.GetDocumentTypeListLabel(doc.DocumentType, config2);
                if (!string.IsNullOrWhiteSpace(typeToStore))
                    docsWithTypeUpdate.Add(doc);
            }
            if (sectionOk)
            {
                doc.Section = newSection;
                item.Section = newSection;
            }
            if (typeOk)
                try { _changeHistory.RecordFieldChange(doc.Uuid, doc.Id, "document_type", oldType, typeToStore); } catch (Exception ex) { _log.Warning(ex, "Failed to record change history for {DocId}", doc.Id); }
            try { _ = _auditTrail.LogDocumentActionAsync(AuditAction.DocumentClassified, doc, $"Set type={typeToStore}, section={newSection}"); } catch (Exception ex) { _log.Warning(ex, "Failed to log audit trail for {DocId}", doc.Id); }
            success++;
        }
        ClassificationMovesResult? moveResult = null;
        if (docsWithTypeUpdate.Count > 0)
        {
            // Drop active preview sources before renaming files to avoid in-process file locks.
            DocumentPreviewViewer?.Clear();
            SetPdfPreviewSource(null);

            moveResult = ClassificationPathHelper.ApplyMovesForDocumentsSharingFiles(
                _store,
                _fileRenameService,
                docsWithTypeUpdate,
                typeToStore,
                baseDir,
                ResolveExistingDocumentPath);

            if (moveResult is { } mr &&
                (mr.RenameFailedDocumentIds.Count > 0 || mr.UnresolvedPathDocumentIds.Count > 0))
            {
                var retryIds = mr.RenameFailedDocumentIds.Concat(mr.UnresolvedPathDocumentIds).Distinct().ToList();
                var retryDocs = new List<Document>();
                foreach (var id in retryIds)
                {
                    var r = _store.GetResult(id);
                    if (r.IsSuccess && r.Value != null) retryDocs.Add(r.Value);
                }

                if (retryDocs.Count > 0)
                {
                    DocumentPreviewViewer?.Clear();
                    SetPdfPreviewSource(null);
                    Thread.Sleep(250);
                    var secondPass = ClassificationPathHelper.ApplyMovesForDocumentsSharingFiles(
                        _store,
                        _fileRenameService,
                        retryDocs,
                        typeToStore,
                        baseDir,
                        ResolveExistingDocumentPath);
                    moveResult = ClassificationPathHelper.MergeMoveResults(mr, secondPass);
                }
            }
        }

        // Verify persisted DB state before refresh to prevent in-memory success masking stale rows.
        var verifyIds = selected
            .Select(x => x.Document?.Id ?? 0)
            .Where(id => id > 0 && !failedDocs.Contains(id))
            .ToList();
        if (verifyIds.Count > 0)
        {
            var verifyFailures = ClassificationPathHelper.VerifyClassificationInvariants(
                _store,
                verifyIds,
                typeToStore,
                newSection,
                ResolveExistingDocumentPath);

            foreach (var kv in verifyFailures)
            {
                if (!failedDocs.Contains(kv.Key))
                    failedDocs.Add(kv.Key);
                failedReasons[kv.Key] = kv.Value;
            }
        }

        RefreshData();
        var cfg = ServiceContainer.GetService<IConfigStore>();
        
        // Report results including any failures
        if (success > 0 || failedDocs.Count > 0)
        {
            var headline = success > 0 
                ? ReportLocalizationService.GetString("DocumentsUpdated", cfg, success)
                : "Classification operation completed with errors.";
                
            if (failedDocs.Count > 0)
            {
                var detail = failedDocs
                    .Distinct()
                    .Select(id => failedReasons.TryGetValue(id, out var reason) ? $"{id} ({reason})" : $"{id}")
                    .ToList();
                headline += $"\n\nFailed to update {failedDocs.Count} document(s). IDs: {string.Join("; ", detail)}";
            }
            
            var hasFailures = failedDocs.Count > 0;
            if (moveResult != null && docsWithTypeUpdate.Count > 0)
            {
                headline += Environment.NewLine + Environment.NewLine + ClassificationPathHelper.FormatMoveFootnote(moveResult);
                hasFailures = hasFailures || moveResult.RenameFailedDocumentIds.Count > 0 || moveResult.UnresolvedPathDocumentIds.Count > 0;
            }
            
            // Use Warning icon if any failures occurred, otherwise Information
            var icon = hasFailures ? MessageBoxImage.Warning : MessageBoxImage.Information;
            MessageBox.Show(headline, ReportLocalizationService.GetString("SetTypeSection", cfg), MessageBoxButton.OK, icon);
        }
        else
            MessageBox.Show(ReportLocalizationService.GetString("UpdateFailed", cfg), ReportLocalizationService.GetString("SetTypeSection", cfg), MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
