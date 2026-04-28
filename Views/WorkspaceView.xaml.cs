using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Serilog;
using WorkAudit.Config;
using WorkAudit.Controls;
using WorkAudit.Core;
using WorkAudit.Core.Assignment;
using WorkAudit.Core.Compliance;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Helpers;
using WorkAudit.Core.Import;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Dialogs;
using WorkAudit.Storage;
using WorkAudit.Core.TextExtraction;
using WorkAudit.ViewModels;
using System.Threading;
using System.Threading.Tasks;
using PDFtoImage;

namespace WorkAudit.Views;

/// <summary>Tag for tree nodes: Branch, Section, or DocumentType.</summary>
public enum SchemaNodeTag
{
    Branch,
    Section,
    DocumentType
}

public partial class WorkspaceView : UserControl
{
    private const string MyAssignmentsTag = "::MyAssignments::";
    private const string UserKeyInspectorWidth = "workspace.inspectorWidth";
    private const string UserKeyTreeStar = "workspace.inspectorTreeStar";
    private const string UserKeyListStar = "workspace.inspectorListStar";
    private readonly ILogger _log = LoggingService.ForContext<WorkspaceView>();
    private readonly IDocumentStore _store;
    private readonly string _baseDir;
    private readonly IDocumentTypeService _docTypeService;
    private readonly WorkspaceViewModel _workspaceViewModel;
    private Document? _notesDocument;
    private bool _isMyAssignmentsView;
    private Dictionary<int, DocumentAssignment> _docToAssignment = new();
    private static readonly string[] PreviewExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp" };
    private ObservableCollection<QuickLink> _quickLinks = new();
    private readonly PdfPreviewLoadCoordinator _pdfPreviewLoad = new();
    private readonly IMarkupStore _markupStore = ServiceContainer.GetService<IMarkupStore>();
    private int? _previewDocumentId;
    private ICollectionView? _documentListView;
    private double _inspectorPixelWidth = 400;
    private Task? _workspaceWebViewInitTask;
    private CancellationTokenSource? _previewTextOcrCts;
    private bool _previewShowsRasterImage;
    private bool _previewShowsPdf;
    private bool _pdfTextOnImageMode;
    private string? _pdfTextOnImageTempPath;
    private bool _syncingPreviewTextToggle;
    private DispatcherTimer? _workspaceOcrBackfillTimer;

    private bool IsManagerOrAdmin => ServiceContainer.GetService<IPermissionService>().HasMinimumRole(Roles.Manager);
    private bool CanPickAnyBranch => IsManagerOrAdmin || Branches.ScopesToAllBranches(ServiceContainer.GetService<ISessionService>().CurrentUser?.Branch);
    private string LockedUserBranch => Branches.ToConcreteBranchOrDefault(ServiceContainer.GetService<ISessionService>().CurrentUser?.Branch);

    public WorkspaceView(IDocumentStore store, string baseDir, IDocumentTypeService docTypeService)
    {
        InitializeComponent();
        _store = store;
        _baseDir = baseDir;
        _docTypeService = docTypeService;
        _workspaceViewModel = ServiceContainer.GetService<WorkspaceViewModel>();
        DataContext = _workspaceViewModel;

        var ocrService = ServiceContainer.GetService<IOcrService>();
        if (ocrService != null)
        {
            ocrService.OcrCompleted += OnOcrCompleted;
        }

        Loaded += OnLoaded;
        if (PdfPreviewViewer != null)
        {
            PdfPreviewViewer.SizeChanged += (_, _) => SyncPdfMarkupCanvasSize();
        }
        if (DocumentPreviewViewer != null)
            DocumentPreviewViewer.ImageRotationChanged += DocumentPreviewViewer_ImageRotationChanged;
    }

    private void DocumentPreviewViewer_ImageRotationChanged(object? sender, EventArgs e)
    {
        UpdatePreviewTextOnImageToggleAvailability();
        if (DocumentPreviewViewer != null && Math.Abs(DocumentPreviewViewer.ImageRotationDegrees) > 0.001)
            CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
    }

    private void CancelAndUncheckPreviewTextOnImageToggle(bool restorePdfViewerIfPdfOcrMode = false)
    {
        _previewTextOcrCts?.Cancel();

        var wasPdfOcr = _pdfTextOnImageMode;
        TryDeletePdfTextOnImageTemp();
        _pdfTextOnImageMode = false;

        DocumentPreviewViewer?.ClearOcrTextOverlay();

        if (restorePdfViewerIfPdfOcrMode && wasPdfOcr && _previewShowsPdf
            && PdfPreviewHost != null && DocumentPreviewViewer != null)
        {
            DocumentPreviewViewer.SetSource(null);
            DocumentPreviewViewer.Visibility = Visibility.Collapsed;
            PdfPreviewHost.Visibility = Visibility.Visible;
        }

        HidePdfTextOnImageInfoBar();

        if (PreviewTextOnImageToggle == null) return;
        _syncingPreviewTextToggle = true;
        try
        {
            PreviewTextOnImageToggle.IsChecked = false;
        }
        finally
        {
            _syncingPreviewTextToggle = false;
        }
    }

    private void HidePdfTextOnImageInfoBar()
    {
        if (PdfTextOnImageInfoBar != null)
            PdfTextOnImageInfoBar.Visibility = Visibility.Collapsed;
    }

    private void ShowPdfTextOnImageInfoBar(Document doc, int pageIndex0)
    {
        if (PdfTextOnImageInfoBar == null || PdfTextOnImageInfoText == null) return;
        var page1 = pageIndex0 + 1;
        var total = doc.PageCount ?? 1;
        PdfTextOnImageInfoText.Text =
            $"Text on image shows only page {page1} of {total} as a picture so OCR lines match the pixels. " +
            "The full PDF viewer is hidden during this mode. Turn off Text on image to browse every page again.";
        PdfTextOnImageInfoBar.Visibility = Visibility.Visible;
    }

    private void TryDeletePdfTextOnImageTemp()
    {
        var p = _pdfTextOnImageTempPath;
        _pdfTextOnImageTempPath = null;
        if (string.IsNullOrEmpty(p)) return;
        try
        {
            if (File.Exists(p))
                File.Delete(p);
        }
        catch
        {
            /* ignore */
        }
    }

    private void UpdatePreviewTextOnImageToggleAvailability()
    {
        if (PreviewTextOnImageToggle == null || DocumentPreviewViewer == null) return;
        var neutralRotation = Math.Abs(DocumentPreviewViewer.ImageRotationDegrees % 360) < 0.001;
        PreviewTextOnImageToggle.IsEnabled = (_previewShowsRasterImage && neutralRotation) || _previewShowsPdf;
        if (!PreviewTextOnImageToggle.IsEnabled && PreviewTextOnImageToggle.IsChecked == true)
            CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
    }

    private void SyncPreviewTextOnImageAfterDocumentChange()
    {
        CancelAndUncheckPreviewTextOnImageToggle();
        UpdatePreviewTextOnImageToggleAvailability();
    }

    private async void PreviewTextOnImageToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingPreviewTextToggle) return;
        var doc = DocumentList?.SelectedItem as Document;
        var path = doc?.FilePath;
        if (doc == null || string.IsNullOrEmpty(path) || !File.Exists(path) || DocumentPreviewViewer == null)
        {
            CancelAndUncheckPreviewTextOnImageToggle();
            return;
        }

        var svc = ServiceContainer.GetOptionalService<IWindowsPreviewOcrLayout>();
        if (svc == null)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Tesseract preview OCR is not available (tessdata missing or service not registered).",
                "Text on image",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            CancelAndUncheckPreviewTextOnImageToggle();
            return;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var isPdf = ext == ".pdf";
        if (isPdf && PdfPreviewHost == null)
        {
            CancelAndUncheckPreviewTextOnImageToggle();
            return;
        }

        _previewTextOcrCts?.Cancel();
        _previewTextOcrCts = new CancellationTokenSource();
        var ct = _previewTextOcrCts.Token;
        var docId = doc.Id;

        string? ocrInputPath = path;
        if (isPdf)
        {
            if (!TrySetupPdfTextOnImageRasterFromCurrentPage(doc, path))
            {
                CancelAndUncheckPreviewTextOnImageToggle();
                return;
            }

            ocrInputPath = _pdfTextOnImageTempPath;
        }

        try
        {
            await RunWorkspacePreviewOcrAsync(svc, ocrInputPath!, docId, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Tesseract preview OCR failed for {Path}", path);
            MessageBox.Show(
                Window.GetWindow(this),
                "Text recognition failed: " + ex.Message,
                "Text on image",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
        }
    }

    /// <summary>
    /// Deletes any prior temp PNG, renders <see cref="PdfPreviewViewer.Page"/> to a new temp file, and wires the image preview for OCR.
    /// </summary>
    /// <returns>false if prerequisites failed or rendering failed (message may have been shown).</returns>
    private bool TrySetupPdfTextOnImageRasterFromCurrentPage(Document doc, string path)
    {
        if (PdfPreviewHost == null || DocumentPreviewViewer == null) return false;

        // Apply jump-box text to the viewer first (user may type a page and turn on Text on image without pressing →).
        if (PdfPreviewViewer != null)
            TryParseAndApplyPdfPageFromJumpBox();

        var pageIndex0 = GetPdfPageIndex0();
        var configStore = ServiceContainer.GetService<IConfigStore>();
        var dpi = configStore.GetSettingInt("ocr_pdf_render_dpi", 300);
        TryDeletePdfTextOnImageTemp();
        var tempPath = Path.Combine(Path.GetTempPath(), $"WorkAudit_pdf_ocr_{Guid.NewGuid():N}.png");
        try
        {
            using (var stream = File.OpenRead(path))
            {
                Conversion.SavePng(tempPath, stream, options: new PDFtoImage.RenderOptions { Dpi = dpi }, page: pageIndex0);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to render PDF page to PNG for preview OCR");
            MessageBox.Show(
                Window.GetWindow(this),
                "Could not render the PDF page for text scanning: " + ex.Message,
                "Text on image",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        _pdfTextOnImageTempPath = tempPath;
        _pdfTextOnImageMode = true;
        PdfPreviewHost.Visibility = Visibility.Collapsed;
        DocumentPreviewViewer.SetSource(tempPath);
        DocumentPreviewViewer.Visibility = Visibility.Visible;
        ShowPdfTextOnImageInfoBar(doc, pageIndex0);
        return true;
    }

    /// <summary>After jumping to another PDF page while Text-on-image is on, re-render that page and run preview OCR again.</summary>
    private async Task RestartPdfTextOnImageForCurrentPageAsync()
    {
        if (!_pdfTextOnImageMode || PreviewTextOnImageToggle?.IsChecked != true) return;
        if (DocumentPreviewViewer == null) return;
        var doc = DocumentList?.SelectedItem as Document;
        var path = doc?.FilePath;
        if (doc == null || string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)) return;

        var svc = ServiceContainer.GetOptionalService<IWindowsPreviewOcrLayout>();
        if (svc == null)
        {
            CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
            return;
        }

        _previewTextOcrCts?.Cancel();
        _previewTextOcrCts = new CancellationTokenSource();
        var ct = _previewTextOcrCts.Token;
        var docId = doc.Id;

        if (!TrySetupPdfTextOnImageRasterFromCurrentPage(doc, path))
        {
            CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
            return;
        }

        try
        {
            await RunWorkspacePreviewOcrAsync(svc, _pdfTextOnImageTempPath!, docId, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Tesseract preview OCR failed after PDF page change for {Path}", path);
            MessageBox.Show(
                Window.GetWindow(this),
                "Text recognition failed: " + ex.Message,
                "Text on image",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
        }
    }

    private async Task RunWorkspacePreviewOcrAsync(IWindowsPreviewOcrLayout svc, string imagePathForOcr, int docId, CancellationToken ct)
    {
        if (DocumentPreviewViewer == null) return;

        DocumentPreviewViewer.SetOcrTextSelectMode(true);
        DocumentPreviewViewer.SetOcrTextOverlay(null);
        DocumentPreviewViewer.SetOcrSelectableLayout(null);

        if (WorkAudit.Controls.DocumentViewer.UseNewOcrSelectionLayer)
        {
            var layout = await svc.ExtractSelectableTextLayoutAsync(imagePathForOcr, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested)
            {
                CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
                return;
            }

            if (DocumentList?.SelectedItem is not Document stillDoc || stillDoc.Id != docId)
            {
                CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
                return;
            }

            if (DocumentPreviewViewer == null || !DocumentPreviewViewer.IsOcrTextSelectMode)
            {
                CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
                return;
            }

            DocumentPreviewViewer.SetOcrSelectableLayout(layout);
            if (layout == null || layout.Spans.Count == 0)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    "No text was recognized. In Control Panel set default OCR language (e.g. eng+ara), ensure Tesseract tessdata includes those traineddata files, or try a clearer image.",
                    "Text on image",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
            }
        }
        else
        {
            var regions = await svc.ExtractLineRegionsAsync(imagePathForOcr, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested)
            {
                CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
                return;
            }

            if (DocumentList?.SelectedItem is not Document stillDoc || stillDoc.Id != docId)
            {
                CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
                return;
            }

            if (DocumentPreviewViewer == null || !DocumentPreviewViewer.IsOcrTextSelectMode)
            {
                CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
                return;
            }

            DocumentPreviewViewer.SetOcrTextOverlay(regions);
            if (regions.Count == 0)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    "No text was recognized. In Control Panel set default OCR language (e.g. eng+ara), ensure Tesseract tessdata includes those traineddata files, or try a clearer image.",
                    "Text on image",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
            }
        }
    }

    private void PreviewTextOnImageToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_syncingPreviewTextToggle) return;
        CancelAndUncheckPreviewTextOnImageToggle(restorePdfViewerIfPdfOcrMode: true);
    }

    private void OnOcrCompleted(Document doc)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (DocumentList?.SelectedItem is Document selected && selected.Id == doc.Id)
            {
                selected.OcrText = doc.OcrText; // Update reference for display
                UpdateMetadataDisplay(selected);
                RefreshAccountFields(selected);
                RefreshOcrPreview(selected);
                _log.Information("UI updated for completed background OCR on document {Id}", doc.Id);
            }
        });
    }

    private void ViewOcrBtn_Click(object sender, RoutedEventArgs e)
    {
        var doc = DocumentList?.SelectedItem as Document;
        if (doc == null || string.IsNullOrWhiteSpace(doc.OcrText)) return;

        var resultDlg = new TextResultDialog("OCR Results", doc.OcrText, "Close");
        resultDlg.Owner = Window.GetWindow(this);
        resultDlg.ShowDialog();
    }

    private void RunOcrBtn_Click(object sender, RoutedEventArgs e)
    {
        var doc = DocumentList?.SelectedItem as Document;
        if (doc == null) return;

        var ocrService = ServiceContainer.GetService<IOcrService>();
        if (ocrService != null)
        {
            ocrService.EnqueueOcrTask(doc);
            OcrPreviewText.Text = "Enqueued for manual OCR extraction...";
            OcrPreviewText.FontStyle = FontStyles.Italic;
            RunOcrBtn.IsEnabled = false;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _workspaceWebViewInitTask = InitializeWorkspaceWebViewAsync();
        WorkspacePdfPreviewTools?.SetTargetViewer(PdfPreviewViewer);
        _documentListView = CollectionViewSource.GetDefaultView(_workspaceViewModel.CurrentDocuments);
        _documentListView.Filter = DocumentListFilterPredicate;
        if (DocumentList != null)
            DocumentList.ItemsSource = _documentListView;
        LoadInspectorWidthFromSettings();
        PopulateFilterOptions();
        LoadFilterPersistence();
        PopulateTypeFilter();
        PopulateDocumentIndex();
        ApplyAssignmentLocalization();
        // Load all documents for the current branch filters (same as managers). Do not default non-managers to
        // "My Assignments" — that list is often empty and hid the whole workspace from typical users.
        RunFilteredSearch();
        RefreshFollowUpReminderPanel();
        ApplyBrowserVisibility();
        ApplyPreviewVisibility();
        ApplyFiltersAndDetailsVisibility();
        UpdateAssignButtonVisibility();
        LoadQuickLinks();
        UpdateActiveFilterSummary();
        UpdateMetadataDisplay(null);
        Dispatcher.BeginInvoke(new Action(LoadInspectorRowHeightsFromSettings), DispatcherPriority.Loaded);
    }

    #region Panel toggles

    private bool _inspectorPanelVisible = true;

    private void InspectorToggle_Changed(object sender, RoutedEventArgs e)
    {
        _inspectorPanelVisible = InspectorToggle?.IsChecked == true;
        ApplyFiltersAndDetailsVisibility();
    }

    private void BrowserToggle_Changed(object sender, RoutedEventArgs e)
    {
        ApplyBrowserVisibility();
    }

    private void PreviewToggle_Changed(object sender, RoutedEventArgs e)
    {
        ApplyPreviewVisibility();
    }

    private bool _syncingDetailsOcrToggles;

    private void DetailsOcrToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingDetailsOcrToggles) return;
        var isOn = (sender as ToggleButton)?.IsChecked == true;
        _syncingDetailsOcrToggles = true;
        try
        {
            if (DetailsOcrToolbarToggle != null && DetailsOcrToolbarToggle.IsChecked != isOn)
                DetailsOcrToolbarToggle.IsChecked = isOn;
            if (DetailsOcrFlyoutToggle != null && DetailsOcrFlyoutToggle.IsChecked != isOn)
                DetailsOcrFlyoutToggle.IsChecked = isOn;
            if (DetailsOcrFlyoutPanel != null)
                DetailsOcrFlyoutPanel.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _syncingDetailsOcrToggles = false;
        }
    }

    private void ApplyFiltersAndDetailsVisibility()
    {
        var visible = _inspectorPanelVisible;

        if (InspectorPanel != null) InspectorPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (InspectorCol != null) InspectorCol.Width = visible ? new GridLength(_inspectorPixelWidth) : new GridLength(0);
        if (InspectorSplitterCol != null) InspectorSplitterCol.Width = visible ? new GridLength(4) : new GridLength(0);
        if (InspectorBrowserSplitter != null) InspectorBrowserSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyBrowserVisibility()
    {
        var visible = BrowserToggle?.IsChecked == true;
        if (BrowserPanel != null) BrowserPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (BrowserCol != null) BrowserCol.Width = visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        var bothPanelsVisible = visible && (PreviewToggle?.IsChecked == true);
        if (BrowserSplitter != null) BrowserSplitter.Visibility = bothPanelsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (PreviewSplitterCol != null) PreviewSplitterCol.Width = bothPanelsVisible ? new GridLength(4) : new GridLength(0);
    }

    private void ApplyPreviewVisibility()
    {
        var visible = PreviewToggle?.IsChecked == true;
        if (PreviewPanel != null) PreviewPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (PreviewCol != null) PreviewCol.Width = visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        var bothPanelsVisible = visible && (BrowserToggle?.IsChecked == true);
        if (PreviewSplitterCol != null) PreviewSplitterCol.Width = bothPanelsVisible ? new GridLength(4) : new GridLength(0);
        if (BrowserSplitter != null) BrowserSplitter.Visibility = bothPanelsVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region Filters

    private void PopulateFilterOptions()
    {
        FilterBranch?.Items.Clear();
        if (CanPickAnyBranch)
        {
            FilterBranch?.Items.Add("");
            foreach (var b in Branches.All) FilterBranch?.Items.Add(b);
            if (FilterBranch != null) FilterBranch.IsEnabled = true;
        }
        else
        {
            FilterBranch?.Items.Add(LockedUserBranch);
            if (FilterBranch != null) FilterBranch.IsEnabled = false;
        }

        FilterSection?.Items.Clear();
        FilterSection?.Items.Add("");
        foreach (var s in Enums.SectionValues) FilterSection?.Items.Add(s);

        FilterDocType?.Items.Clear();
        FilterDocType?.Items.Add("");
        FilterDocType?.Items.Add(DocumentTypeInfo.UnclassifiedType);
        foreach (var t in _docTypeService.GetAllDocumentTypes()) FilterDocType?.Items.Add(t);
    }

    private void LoadFilterPersistence()
    {
        var persistedBranch = FilterPersistence.Load(Constants.FilterPrefix.Workspace, Constants.FilterKey.Branch);
        if (FilterBranch != null)
        {
            if (CanPickAnyBranch)
                FilterBranch.Text = string.IsNullOrWhiteSpace(persistedBranch) || Branches.All.Contains(persistedBranch.Trim()) ? persistedBranch.Trim() : "";
            else
                FilterBranch.Text = LockedUserBranch;
        }
        if (FilterSection != null) FilterSection.Text = FilterPersistence.Load(Constants.FilterPrefix.Workspace, Constants.FilterKey.Section);
        if (FilterDocType != null) FilterDocType.Text = FilterPersistence.Load(Constants.FilterPrefix.Workspace, Constants.FilterKey.DocumentType);
        if (FilterDateFrom != null) FilterDateFrom.SelectedDate = FilterPersistence.LoadDate(Constants.FilterPrefix.Workspace, Constants.FilterKey.DateFrom);
        if (FilterDateTo != null) FilterDateTo.SelectedDate = FilterPersistence.LoadDate(Constants.FilterPrefix.Workspace, Constants.FilterKey.DateTo);
        UpdateActiveFilterSummary();
    }

    private void SaveFilterPersistence()
    {
        if (FilterBranch != null) FilterPersistence.Save(Constants.FilterPrefix.Workspace, Constants.FilterKey.Branch, FilterBranch.Text);
        if (FilterSection != null) FilterPersistence.Save(Constants.FilterPrefix.Workspace, Constants.FilterKey.Section, FilterSection.Text);
        if (FilterDocType != null) FilterPersistence.Save(Constants.FilterPrefix.Workspace, Constants.FilterKey.DocumentType, FilterDocType.Text);
        if (FilterDateFrom != null) FilterPersistence.SaveDate(Constants.FilterPrefix.Workspace, Constants.FilterKey.DateFrom, FilterDateFrom.SelectedDate);
        if (FilterDateTo != null) FilterPersistence.SaveDate(Constants.FilterPrefix.Workspace, Constants.FilterKey.DateTo, FilterDateTo.SelectedDate);
    }

    private void FilterBranch_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PopulateTypeFilter();
        UpdateActiveFilterSummary();
    }

    private void FilterSection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PopulateTypeFilter();
        UpdateActiveFilterSummary();
    }

    private void PopulateTypeFilter()
    {
        if (FilterBranch == null || FilterSection == null || FilterDocType == null) return;
        var branch = string.IsNullOrWhiteSpace(FilterBranch.Text) ? null : FilterBranch.Text.Trim();
        if (!CanPickAnyBranch) branch = LockedUserBranch;
        var section = string.IsNullOrWhiteSpace(FilterSection.Text) ? null : FilterSection.Text.Trim();

        var previous = FilterDocType.SelectedItem as string ?? FilterDocType.Text?.Trim();
        if (string.IsNullOrWhiteSpace(previous)) previous = null;

        FilterDocType.Items.Clear();
        FilterDocType.Items.Add("");
        FilterDocType.Items.Add(DocumentTypeInfo.UnclassifiedType);

        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dt in _docTypeService.GetDocumentTypesForSection(section, branch))
        {
            if (!string.IsNullOrWhiteSpace(dt))
                types.Add(dt);
        }

        foreach (var dt in _store.GetDistinctDocumentTypes(branch, section))
        {
            if (!string.IsNullOrWhiteSpace(dt))
                types.Add(dt);
        }

        foreach (var dt in types.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            FilterDocType.Items.Add(dt);
        RestoreDocTypeCombo(FilterDocType, previous);
    }

    private static void RestoreDocTypeCombo(System.Windows.Controls.ComboBox filterDocType, string? previous)
    {
        if (previous == null || filterDocType == null) return;
        if (string.Equals(previous, "Other", StringComparison.OrdinalIgnoreCase))
            previous = DocumentTypeInfo.UnclassifiedType;
        object? found = null;
        foreach (var o in filterDocType.Items)
        {
            if (o is string str && string.Equals(str, previous, StringComparison.OrdinalIgnoreCase))
            {
                found = o;
                break;
            }
        }
        if (found != null)
            filterDocType.SelectedItem = found;
        else
        {
            filterDocType.Items.Add(previous);
            filterDocType.SelectedItem = previous;
        }
    }

    private void FilterSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        SaveFilterPersistence();
        UpdateActiveFilterSummary();
        if (_isMyAssignmentsView)
        {
            LoadMyAssignments();
            return;
        }
        RunFilteredSearch();
    }

    private void FilterClearBtn_Click(object sender, RoutedEventArgs e)
    {
        if (FilterBranch != null) FilterBranch.Text = CanPickAnyBranch ? "" : LockedUserBranch;
        if (FilterSection != null) FilterSection.Text = "";
        if (FilterDocType != null) FilterDocType.Text = "";
        if (FilterDateFrom != null) FilterDateFrom.SelectedDate = null;
        if (FilterDateTo != null) FilterDateTo.SelectedDate = null;
        SaveFilterPersistence();
        UpdateActiveFilterSummary();
        if (_isMyAssignmentsView)
        {
            LoadMyAssignments();
            return;
        }
        RunFilteredSearch();
    }

    private void FilterDate_Changed(object sender, EventArgs e)
    {
        SaveFilterPersistence();
        UpdateActiveFilterSummary();
        if (_isMyAssignmentsView) { LoadMyAssignments(); return; }
        if (FileTree?.SelectedItem is TreeViewItem item && item.Tag is string tag && tag != MyAssignmentsTag && !string.IsNullOrEmpty(tag))
        {
            var folderDocs = _store.ListDocumentsByFolder(tag, limit: 1000, appBaseDirectory: _baseDir);
            _workspaceViewModel.SetDocuments(ApplyFiltersToDocumentsForFolderTree(folderDocs));
            _documentListView?.Refresh();
        }
        else
            RunFilteredSearch();
    }

    private string? GetFilterDocType()
    {
        if (FilterDocType == null) return null;
        if (FilterDocType.SelectedItem is string s) return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var t = FilterDocType.Text?.Trim();
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    private void RunFilteredSearch()
    {
        var branch = string.IsNullOrWhiteSpace(FilterBranch?.Text) ? null : FilterBranch!.Text.Trim();
        if (!CanPickAnyBranch) branch = LockedUserBranch;
        var section = string.IsNullOrWhiteSpace(FilterSection?.Text) ? null : FilterSection.Text.Trim();
        var docType = GetFilterDocType();
        var dateFrom = FilterDateFrom?.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var dateTo = FilterDateTo?.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        _workspaceViewModel.LoadDocuments(branch, section, docType, dateFrom, dateTo, limit: 5000);
        ScheduleWorkspaceOcrBackfill();
    }

    /// <summary>Debounced enqueue of OCR for Ready-for-Audit rows missing text (or PDF low-signal text).</summary>
    private void ScheduleWorkspaceOcrBackfill()
    {
        if (!ServiceContainer.IsInitialized) return;
        var cfg = ServiceContainer.GetOptionalService<IConfigStore>();
        if (cfg != null && !cfg.GetSettingBool(DocumentWorkspaceOcr.SettingWorkspaceAutoBackfill, true))
            return;

        if (_workspaceOcrBackfillTimer == null)
        {
            _workspaceOcrBackfillTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _workspaceOcrBackfillTimer.Tick += (_, _) =>
            {
                _workspaceOcrBackfillTimer!.Stop();
                EnqueueWorkspaceOcrBackfill();
            };
        }

        _workspaceOcrBackfillTimer.Stop();
        _workspaceOcrBackfillTimer.Start();
    }

    private void EnqueueWorkspaceOcrBackfill()
    {
        if (!ServiceContainer.IsInitialized) return;
        var cfg = ServiceContainer.GetOptionalService<IConfigStore>();
        var ocr = ServiceContainer.GetOptionalService<IOcrService>();
        if (cfg == null || ocr == null) return;
        if (!cfg.GetSettingBool(DocumentWorkspaceOcr.SettingWorkspaceAutoBackfill, true)) return;

        var batch = cfg.GetSettingInt(DocumentWorkspaceOcr.SettingWorkspaceBackfillBatchSize, 50);
        if (batch < 1) batch = 50;

        var enqueued = 0;
        foreach (var doc in _workspaceViewModel.CurrentDocuments)
        {
            if (enqueued >= batch) break;
            if (!string.Equals(doc.Status, Enums.Status.ReadyForAudit, StringComparison.Ordinal)) continue;
            if (!DocumentWorkspaceOcr.NeedsBackgroundOcr(doc, cfg)) continue;
            DocumentWorkspaceOcr.TryEnqueueOcrIfNeeded(doc, ocr, cfg);
            enqueued++;
        }

        if (enqueued > 0)
            _log.Debug("Workspace OCR backfill: enqueued {Count} document(s) (batch cap {Batch})", enqueued, batch);
    }

    private List<Document> ApplyFiltersToDocuments(List<Document> docs)
    {
        var branch = string.IsNullOrWhiteSpace(FilterBranch?.Text) ? null : FilterBranch!.Text.Trim();
        if (!CanPickAnyBranch) branch = LockedUserBranch;
        var section = string.IsNullOrWhiteSpace(FilterSection?.Text) ? null : FilterSection.Text.Trim();
        var docType = GetFilterDocType();
        var dateFrom = FilterDateFrom?.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var dateTo = FilterDateTo?.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        return docs.Where(d =>
        {
            if (!string.IsNullOrEmpty(branch) && !string.Equals(d.Branch, branch, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(section) && !string.Equals(d.Section, section, StringComparison.OrdinalIgnoreCase)) return false;
            return DocumentMatchesTypeAndDateFilters(d, docType, dateFrom, dateTo);
        }).ToList();
    }

    /// <summary>
    /// When browsing by file tree, scope is the folder path. Top-strip filters (branch, section, type, dates) apply to Search, not folder contents;
    /// re-applying them here often cleared the list. Use the list filter box to narrow within the folder.
    /// Also enforces <see cref="IPermissionService.CanAccessDocument"/> (branch + assignment rules).
    /// </summary>
    private List<Document> ApplyFiltersToDocumentsForFolderTree(List<Document> docs)
    {
        if (!ServiceContainer.IsInitialized || docs.Count == 0) return docs;
        var ps = ServiceContainer.GetService<IPermissionService>();
        return docs.Where(d => ps.CanAccessDocument(d)).ToList();
    }

    private static bool DocumentMatchesTypeAndDateFilters(Document d, string? docType, string? dateFrom, string? dateTo)
    {
        if (!string.IsNullOrEmpty(docType))
        {
            var want = docType.Trim();
            var have = string.IsNullOrWhiteSpace(d.DocumentType) ? null : d.DocumentType.Trim();
            if (DocumentTypeInfo.IsUnclassified(want))
            {
                if (have != null && !DocumentTypeInfo.IsUnclassified(have)) return false;
            }
            else if (!string.Equals(have ?? "", want, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (!string.IsNullOrEmpty(dateFrom) || !string.IsNullOrEmpty(dateTo))
        {
            var docDate = !string.IsNullOrWhiteSpace(d.ExtractedDate)
                ? d.ExtractedDate.Trim()
                : null;
            if (string.IsNullOrEmpty(docDate)) return false;
            if (!string.IsNullOrEmpty(dateFrom) && string.CompareOrdinal(docDate, dateFrom) < 0) return false;
            if (!string.IsNullOrEmpty(dateTo) && string.CompareOrdinal(docDate, dateTo) > 0) return false;
        }
        return true;
    }

    /// <summary>Apply drill-down filters from report/dashboard and run search.</summary>
    public void ApplyDrillDownFilter(string? branch, string? section, DateTime? dateFrom, DateTime? dateTo)
    {
        _isMyAssignmentsView = false;
        if (FilterBranch != null) FilterBranch.Text = branch ?? (CanPickAnyBranch ? "" : LockedUserBranch);
        if (FilterSection != null) FilterSection.Text = section ?? "";
        if (FilterDocType != null) FilterDocType.Text = "";
        if (FilterDateFrom != null) FilterDateFrom.SelectedDate = dateFrom;
        if (FilterDateTo != null) FilterDateTo.SelectedDate = dateTo;
        SaveFilterPersistence();
        UpdateActiveFilterSummary();
        RunFilteredSearch();
    }

    /// <summary>Select a document in the list after drill-down or deep link (shows preview).</summary>
    public void SelectDocumentById(int documentId)
    {
        // ListBox uses a filtered ICollectionView; SelectedIndex must not index the raw ObservableCollection.
        // The quick filter can hide the row — clear it so the document is visible.
        if (DocumentListFilterBox != null)
            DocumentListFilterBox.Text = "";

        var doc = _workspaceViewModel.CurrentDocuments.FirstOrDefault(d => d.Id == documentId);
        if (doc == null)
        {
            var get = _store.GetResult(documentId);
            if (get.IsSuccess && get.Value != null)
            {
                doc = get.Value;
                if (_workspaceViewModel.CurrentDocuments.All(d => d.Id != documentId))
                    _workspaceViewModel.CurrentDocuments.Insert(0, doc);
            }
        }

        _documentListView?.Refresh();

        if (doc != null && DocumentList != null)
        {
            DocumentList.SelectedItem = doc;
            try
            {
                DocumentList.ScrollIntoView(doc);
            }
            catch
            {
                // Rare: list not yet laid out after filter refresh
            }
        }
    }

    /// <summary>Refresh the document list (e.g. after documents are moved from Processing). Switches to filter view and re-runs search.</summary>
    public void RefreshDocumentList()
    {
        _isMyAssignmentsView = false;
        if (AssignmentFilterPanel != null) AssignmentFilterPanel.Visibility = Visibility.Collapsed;
        if (FileTree?.SelectedItem is TreeViewItem selected)
            selected.IsSelected = false; // clears folder/MyAssignments selection; SelectedItemChanged will call RunFilteredSearch
        else
            RunFilteredSearch();
        RefreshFollowUpReminderPanel();
    }

    private void FilterDocType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateActiveFilterSummary();
    }

    #endregion

    #region Inspector layout, filter summary, list filter

    private static double ReadDoubleUserSetting(string key, double defaultValue)
    {
        try
        {
            var v = UserSettings.Get<object>(key);
            if (v == null) return defaultValue;
            return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return defaultValue;
        }
    }

    private void LoadInspectorWidthFromSettings()
    {
        var w = ReadDoubleUserSetting(UserKeyInspectorWidth, 400);
        _inspectorPixelWidth = Math.Clamp(w, 280, 720);
    }

    private void LoadInspectorRowHeightsFromSettings()
    {
        if (FileTreeDocumentsGrid != null)
        {
            var tStar = ReadDoubleUserSetting(UserKeyTreeStar, 100);
            var lStar = ReadDoubleUserSetting(UserKeyListStar, 100);
            if (tStar < 1) tStar = 100;
            if (lStar < 1) lStar = 100;
            FileTreeDocumentsGrid.RowDefinitions[0].Height = new GridLength(tStar, GridUnitType.Star);
            FileTreeDocumentsGrid.RowDefinitions[2].Height = new GridLength(lStar, GridUnitType.Star);
        }
    }

    private void InspectorBrowserSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (e.Canceled || InspectorCol == null) return;
        var w = InspectorCol.ActualWidth;
        if (w < 280) w = 280;
        if (w > 720) w = 720;
        _inspectorPixelWidth = w;
        UserSettings.Set(UserKeyInspectorWidth, _inspectorPixelWidth);
    }

    private void FileTreeDocumentsSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (e.Canceled || FileTreeDocumentsGrid == null) return;
        var r0 = FileTreeDocumentsGrid.RowDefinitions[0].ActualHeight;
        var r2 = FileTreeDocumentsGrid.RowDefinitions[2].ActualHeight;
        var total = r0 + r2;
        if (total < 1) return;
        const double scale = 1000;
        UserSettings.Set(UserKeyTreeStar, r0 / total * scale);
        UserSettings.Set(UserKeyListStar, r2 / total * scale);
    }

    private void InspectorFilterArea_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (FilterSearchBtn != null)
            FilterSearchBtn_Click(FilterSearchBtn, new RoutedEventArgs());
    }

    private void UpdateActiveFilterSummary()
    {
        if (FilterSummaryText == null) return;
        var parts = new List<string>();
        var branch = string.IsNullOrWhiteSpace(FilterBranch?.Text) ? null : FilterBranch!.Text.Trim();
        if (!CanPickAnyBranch) branch = LockedUserBranch;
        if (CanPickAnyBranch && string.IsNullOrEmpty(branch))
            parts.Add("Branch: All");
        else if (!string.IsNullOrEmpty(branch))
            parts.Add($"Branch: {branch}");

        var section = string.IsNullOrWhiteSpace(FilterSection?.Text) ? null : FilterSection!.Text.Trim();
        parts.Add(string.IsNullOrEmpty(section) ? "Section: All" : $"Section: {section}");

        var docType = GetFilterDocType();
        parts.Add(string.IsNullOrEmpty(docType) ? "Type: All" : $"Type: {docType}");

        var from = FilterDateFrom?.SelectedDate;
        var to = FilterDateTo?.SelectedDate;
        if (from.HasValue || to.HasValue)
        {
            var fromS = from.HasValue ? from.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "…";
            var toS = to.HasValue ? to.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "…";
            parts.Add($"Dates: {fromS} – {toS}");
        }
        else
            parts.Add("Dates: Any");

        FilterSummaryText.Text = string.Join(" · ", parts);
    }

    private bool DocumentListFilterPredicate(object obj)
    {
        if (obj is not Document doc) return false;
        var q = DocumentListFilterBox?.Text?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        var display = doc.DocumentListDisplay ?? "";
        return display.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void DocumentListFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _documentListView?.Refresh();
    }

    private void FileTreeRefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshTree();

    private void FileTreeExpandAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (FileTree == null) return;
        foreach (TreeViewItem item in FileTree.Items.Cast<TreeViewItem>())
            SetTreeExpandedRecursive(item, true);
    }

    private void FileTreeCollapseAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (FileTree == null) return;
        foreach (TreeViewItem item in FileTree.Items.Cast<TreeViewItem>())
            SetTreeExpandedRecursive(item, false);
    }

    private static void SetTreeExpandedRecursive(TreeViewItem item, bool expand)
    {
        item.IsExpanded = expand;
        foreach (TreeViewItem child in item.Items.Cast<TreeViewItem>())
            SetTreeExpandedRecursive(child, expand);
    }

    #endregion

    #region File tree

    private void PopulateDocumentIndex()
    {
        if (FileTree == null) return;
        FileTree.Items.Clear();

        // "My Assignments" at top - shows documents assigned to current user
        var session = ServiceContainer.GetService<ISessionService>();
        var user = session?.CurrentUser;
        if (user != null)
        {
            var assignmentsItem = new TreeViewItem
            {
                Header = "My Assignments",
                Tag = MyAssignmentsTag,
                IsExpanded = false
            };
            FileTree.Items.Add(assignmentsItem);
        }

        // File tree: only Manager+ sees the full vault root. Everyone else is scoped to their home branch
        // folder only — never fall back to _baseDir when the branch folder is missing, or other branches appear.
        var startDir = _baseDir;
        if (!IsManagerOrAdmin)
        {
            var branchFolder = Path.Combine(_baseDir, Folders.SanitizeSegment(LockedUserBranch));
            startDir = branchFolder;
        }

        if (!Directory.Exists(startDir)) return;

        var root = new TreeViewItem
        {
            Header = Path.GetFileName(startDir) ?? startDir,
            Tag = startDir,
            IsExpanded = true
        };
        PopulateTreeItem(root, startDir);
        FileTree.Items.Add(root);
    }

    private static readonly HashSet<string> HiddenFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Models",
        "webcam_sessions",
        "captures"
    };

    private void PopulateTreeItem(TreeViewItem parent, string dirPath)
    {
        try
        {
            foreach (var subDir in Directory.GetDirectories(dirPath).OrderBy(Path.GetFileName))
            {
                var name = Path.GetFileName(subDir);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".", StringComparison.Ordinal)) continue;
                if (HiddenFolders.Contains(name)) continue;
                var item = new TreeViewItem { Header = name, Tag = subDir };
                parent.Items.Add(item);
                PopulateTreeItem(item, subDir);
            }
        }
        catch { /* ignore */ }
    }

    private void RefreshTree()
    {
        PopulateDocumentIndex();
    }

    /// <summary>Select the My Assignments node in the File Tree (used when navigating from View menu).</summary>
    public void SelectMyAssignments()
    {
        if (FileTree == null) return;
        var assignmentsItem = FileTree.Items.Cast<TreeViewItem>().FirstOrDefault(i => i.Tag as string == MyAssignmentsTag);
        if (assignmentsItem != null)
        {
            assignmentsItem.IsSelected = true;
        }
    }

    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object?> e)
    {
        if (FileTree?.SelectedItem is not TreeViewItem item || item.Tag == null)
        {
            _isMyAssignmentsView = false;
            if (AssignmentFilterPanel != null) AssignmentFilterPanel.Visibility = Visibility.Collapsed;
            RunFilteredSearch();
            return;
        }
        if (item.Tag as string == MyAssignmentsTag)
        {
            _isMyAssignmentsView = true;
            if (AssignmentFilterPanel != null) AssignmentFilterPanel.Visibility = Visibility.Visible;
            LoadMyAssignments();
            return;
        }
        _isMyAssignmentsView = false;
        if (AssignmentFilterPanel != null) AssignmentFilterPanel.Visibility = Visibility.Collapsed;
        var folderPath = item.Tag as string;
        if (string.IsNullOrEmpty(folderPath))
        {
            _isMyAssignmentsView = false;
            if (AssignmentFilterPanel != null) AssignmentFilterPanel.Visibility = Visibility.Collapsed;
            RunFilteredSearch();
            return;
        }
        _isMyAssignmentsView = false;
        if (AssignmentFilterPanel != null) AssignmentFilterPanel.Visibility = Visibility.Collapsed;
        var folderDocs = _store.ListDocumentsByFolder(folderPath, limit: 1000, appBaseDirectory: _baseDir);
        _workspaceViewModel.SetDocuments(ApplyFiltersToDocumentsForFolderTree(folderDocs));
        _documentListView?.Refresh();
        ScheduleWorkspaceOcrBackfill();
    }

    private void ApplyAssignmentLocalization()
    {
        var config = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
        if (config == null) return;
        if (AssignmentFilterLabel != null) AssignmentFilterLabel.Text = ReportLocalizationService.GetString("Filter", config);
        if (RbAssignmentPending != null) RbAssignmentPending.Content = ReportLocalizationService.GetString("Pending", config);
        if (RbAssignmentInProgress != null) RbAssignmentInProgress.Content = ReportLocalizationService.GetString("InProgress", config);
        if (RbAssignmentCompleted != null) RbAssignmentCompleted.Content = ReportLocalizationService.GetString("Completed", config);
        if (AssignmentOverdueOnlyCheck != null) AssignmentOverdueOnlyCheck.Content = ReportLocalizationService.GetString("ShowOverdueOnly", config);
        if (MarkCompleteBtn != null) MarkCompleteBtn.ToolTip = ReportLocalizationService.GetString("MarkComplete", config);
        if (ViewDocumentBtn != null) ViewDocumentBtn.ToolTip = ReportLocalizationService.GetString("ViewDocument", config);
        if (CancelAssignmentBtn != null) CancelAssignmentBtn.ToolTip = ReportLocalizationService.GetString("CancelAssignment", config);
        if (MoveToArchiveBtn != null) MoveToArchiveBtn.ToolTip = ReportLocalizationService.GetString("MoveToArchiveTooltip", config);
    }

    private void AssignmentFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isMyAssignmentsView) return;
        LoadMyAssignments();
    }

    private string? GetAssignmentStatusFilter()
    {
        if (RbAssignmentPending?.IsChecked == true) return AssignmentStatus.Pending;
        if (RbAssignmentInProgress?.IsChecked == true) return AssignmentStatus.InProgress;
        if (RbAssignmentCompleted?.IsChecked == true) return AssignmentStatus.Completed;
        return null;
    }

    private void LoadMyAssignments()
    {
        if (!ServiceContainer.IsInitialized || DocumentList == null) return;
        var assignmentService = ServiceContainer.GetService<IDocumentAssignmentService>();
        var user = ServiceContainer.GetService<ISessionService>()?.CurrentUser;
        if (user == null)
        {
            _workspaceViewModel.SetDocuments(new List<Document>());
            _docToAssignment.Clear();
            return;
        }
        var status = GetAssignmentStatusFilter();
        var overdueOnly = AssignmentOverdueOnlyCheck?.IsChecked == true;
        var assignments = assignmentService.GetMyAssignments(status, overdueOnly);
        _docToAssignment = assignments
            .GroupBy(a => a.DocumentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AssignedAt).FirstOrDefault()!);
        var docIds = assignments.Select(a => a.DocumentId).Distinct().ToList();
        _workspaceViewModel.SetDocuments(_store.GetByIds(docIds));
        ScheduleWorkspaceOcrBackfill();
    }

    #endregion

    #region Document list

    private void DocumentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            var selected = DocumentList?.SelectedItems?.Cast<Document>().ToList() ?? new List<Document>();
            var first = selected.FirstOrDefault();
            UpdateMetadataDisplay(first);
            RefreshAccountFields(first);
            UpdateAssignButtonVisibility();
            UpdateAssignmentActionButtons(first);
            UpdateFlagFollowUpButtonState(selected);
            if (first != null)
            {
                _notesDocument = first;
                RefreshNotesPreview(first.Id);
                if (ViewNotesBtn != null) ViewNotesBtn.IsEnabled = true;
                if (AddNoteBtn != null) AddNoteBtn.IsEnabled = true;
                RefreshOcrPreview(first);
            }
            else
            {
                _notesDocument = null;
                if (NotesPreviewText != null)
                {
                    NotesPreviewText.Visibility = Visibility.Collapsed;
                    NotesPreviewText.ToolTip = null;
                }
                if (ViewNotesBtn != null) ViewNotesBtn.IsEnabled = false;
                if (AddNoteBtn != null) AddNoteBtn.IsEnabled = false;
                if (OcrPreviewText != null) OcrPreviewText.Text = "No document selected.";
                if (ViewOcrBtn != null) ViewOcrBtn.IsEnabled = false;
                if (RunOcrBtn != null) RunOcrBtn.IsEnabled = false;
                if (OcrSelectionToNoteBtn != null) OcrSelectionToNoteBtn.IsEnabled = false;
            }
            ShowDocumentPreview(first);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "DocumentList_SelectionChanged failed (often rapid navigation); UI left in partial state");
        }
    }

    /// <summary>Returns true if the given document is still the primary (first) selected row — used to drop stale async preview work.</summary>
    private bool IsPrimaryDocumentStillSelected(Document doc)
    {
        if (DocumentList?.SelectedItems == null || DocumentList.SelectedItems.Count == 0) return false;
        return DocumentList.SelectedItems.Cast<Document>().FirstOrDefault() is { } cur && cur.Id == doc.Id;
    }

    private void RefreshOcrPreview(Document doc)
    {
        if (OcrPreviewText == null) return;

        if (ServiceContainer.IsInitialized)
        {
            var cfg = ServiceContainer.GetOptionalService<IConfigStore>();
            var ocr = ServiceContainer.GetOptionalService<IOcrService>();
            if (cfg != null && ocr != null &&
                cfg.GetSettingBool(DocumentWorkspaceOcr.SettingWorkspaceAutoOnSelect, true))
            {
                DocumentWorkspaceOcr.TryEnqueueOcrIfNeeded(doc, ocr, cfg);
            }
        }

        if (string.IsNullOrWhiteSpace(doc.OcrText))
        {
            OcrPreviewText.Text = "No text extracted yet. (Background OCR may be running)";
            OcrPreviewText.FontStyle = FontStyles.Italic;
            if (ViewOcrBtn != null) ViewOcrBtn.IsEnabled = false;
            if (RunOcrBtn != null) RunOcrBtn.IsEnabled = true;
            if (OcrSelectionToNoteBtn != null) OcrSelectionToNoteBtn.IsEnabled = false;
        }
        else
        {
            OcrPreviewText.Text = doc.OcrText.Length > 200 ? doc.OcrText.Substring(0, 197) + "..." : doc.OcrText;
            OcrPreviewText.FontStyle = FontStyles.Normal;
            if (ViewOcrBtn != null) ViewOcrBtn.IsEnabled = true;
            if (RunOcrBtn != null) RunOcrBtn.IsEnabled = true; // Still allow rerunning if desired
            if (OcrSelectionToNoteBtn != null) OcrSelectionToNoteBtn.IsEnabled = true;
        }
    }

    private void DocumentList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var selected = DocumentList?.SelectedItems?.Cast<Document>().ToList() ?? new List<Document>();
        var hasSelection = selected.Count > 0;
        var permissionService = ServiceContainer.GetService<IPermissionService>();
        var canAssign = permissionService.HasMinimumRole(Roles.Manager);
        var canArchive = permissionService.HasPermission(Permissions.ArchiveCreate);
        if (ContextAssignItem != null) ContextAssignItem.Visibility = canAssign ? Visibility.Visible : Visibility.Collapsed;
        if (ContextFlagFollowUpItem != null) ContextFlagFollowUpItem.IsEnabled = hasSelection;
        if (ContextMoveToArchiveItem != null)
        {
            var cfg = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
            if (cfg != null) ContextMoveToArchiveItem.Header = ReportLocalizationService.GetString("MoveToArchive", cfg);
            ContextMoveToArchiveItem.Visibility = canArchive ? Visibility.Visible : Visibility.Collapsed;
            ContextMoveToArchiveItem.IsEnabled = hasSelection && selected.Any(d => d.Status != Enums.Status.Archived);
        }
    }

    private void DocumentList_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox lb && e.OriginalSource is FrameworkElement fe)
        {
            var item = FindVisualParent<System.Windows.Controls.ListBoxItem>(fe);
            if (item != null && item.DataContext is Document)
            {
                lb.SelectedItem = item.DataContext;
            }
        }
    }

    #endregion

    #region Context menu actions

    private void ContextOpen_Click(object sender, RoutedEventArgs e)
    {
        var doc = (DocumentList?.SelectedItem as Document) ?? (DocumentList?.SelectedItems?.Cast<Document>().FirstOrDefault());
        if (doc == null || string.IsNullOrEmpty(doc.FilePath)) return;
        if (!File.Exists(doc.FilePath))
        {
            MessageBox.Show("File not found.", "Open", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = doc.FilePath, UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ContextOpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        var doc = (DocumentList?.SelectedItem as Document) ?? (DocumentList?.SelectedItems?.Cast<Document>().FirstOrDefault());
        if (doc == null || string.IsNullOrEmpty(doc.FilePath)) return;
        var dir = Path.GetDirectoryName(doc.FilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            MessageBox.Show("Folder not found.", "Open in Explorer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{doc.FilePath}\""); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open in Explorer", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ContextCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var doc = (DocumentList?.SelectedItem as Document) ?? (DocumentList?.SelectedItems?.Cast<Document>().FirstOrDefault());
        if (doc == null || string.IsNullOrEmpty(doc.FilePath)) return;
        try { System.Windows.Clipboard.SetText(doc.FilePath); MessageBox.Show("Path copied to clipboard.", "Copy Path", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch { MessageBox.Show("Failed to copy path.", "Copy Path", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ContextAssign_Click(object sender, RoutedEventArgs e) => AssignForSelection();
    private void ContextFlagFollowUp_Click(object sender, RoutedEventArgs e) => FlagFollowUpForSelection();
    private void ContextCorrectClassification_Click(object sender, RoutedEventArgs e) => CorrectClassificationForSelection();
    private void ContextViewDetails_Click(object sender, RoutedEventArgs e)
    {
        var doc = (DocumentList?.SelectedItem as Document) ?? (DocumentList?.SelectedItems?.Cast<Document>().FirstOrDefault());
        if (doc != null) DocumentPreviewWindow.ShowOrActivateWithDocument(doc.FilePath ?? "");
    }

    #endregion

    #region Action buttons

    private void AssignBtn_Click(object sender, RoutedEventArgs e) => AssignForSelection();
    private void SetStatusBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = DocumentList?.SelectedItems?.Cast<Document>().ToList() ?? new List<Document>();
        if (selected.Count == 0) { MessageBox.Show("Select documents first.", "Set Status", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        SetStatusContextMenu.PlacementTarget = SetStatusBtn;
        SetStatusContextMenu.IsOpen = true;
    }
    private void SetStatusOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string status)
            ApplyStatusForSelection(status);
    }
    private void FlagFollowUpBtn_Click(object sender, RoutedEventArgs e) => FlagFollowUpForSelection();
    private void CorrectClassificationBtn_Click(object sender, RoutedEventArgs e) => CorrectClassificationForSelection();

    private async void MoveToArchive_Click(object sender, RoutedEventArgs e)
    {
        var config = ServiceContainer.GetService<IConfigStore>();
        var permissionService = ServiceContainer.GetService<IPermissionService>();
        if (!permissionService.HasPermission(Permissions.ArchiveCreate))
        {
            var deny = config != null ? ReportLocalizationService.GetString("MoveToArchiveNoPermission", config) : "You do not have permission to archive documents.";
            MessageBox.Show(deny, config != null ? ReportLocalizationService.GetString("MoveToArchive", config) : "Move to Archive", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selected = DocumentList?.SelectedItems?.Cast<Document>().ToList() ?? new List<Document>();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select documents first.", config != null ? ReportLocalizationService.GetString("MoveToArchive", config) : "Move to Archive", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var toArchive = selected.Where(d => d.Status != Enums.Status.Archived).ToList();
        if (toArchive.Count == 0)
        {
            MessageBox.Show("Selected documents are already archived.", config != null ? ReportLocalizationService.GetString("MoveToArchive", config) : "Move to Archive", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmMsg = config != null
            ? ReportLocalizationService.GetString("MoveToArchiveConfirm", config, toArchive.Count)
            : $"Move {toArchive.Count} document(s) to Archive? They will leave the Workspace list.";
        var title = config != null ? ReportLocalizationService.GetString("MoveToArchive", config) : "Move to Archive";
        if (MessageBox.Show(confirmMsg, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            var archiveService = ServiceContainer.GetService<IArchiveService>();
            var n = await archiveService.ArchiveDocumentsAsync(toArchive);
            if (n > 0)
            {
                DocumentsArchivedRequested.Raise(n);
                var doneMsg = config != null ? ReportLocalizationService.GetString("DocumentsArchived", config, n) : $"{n} document(s) archived.";
                MessageBox.Show(doneMsg, title, MessageBoxButton.OK, MessageBoxImage.Information);
                if (_isMyAssignmentsView)
                    LoadMyAssignments();
                else
                    RunFilteredSearch();
            }
            else
            {
                MessageBox.Show("No documents were archived.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MarkCompleteBtn_Click(object sender, RoutedEventArgs e)
    {
        var doc = DocumentList?.SelectedItem as Document;
        if (doc == null || !_docToAssignment.TryGetValue(doc.Id, out var assignment)) return;
        var assignmentService = ServiceContainer.GetService<IDocumentAssignmentService>();
        var user = ServiceContainer.GetService<ISessionService>()?.CurrentUser;
        if (user == null) return;

        var dlg = new MarkCompleteAssignmentDialog(doc);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() != true) return;

        var notesStore = ServiceContainer.GetService<INotesStore>();
        var config = ServiceContainer.GetService<AppConfiguration>();

        var note = new Note
        {
            DocumentId = doc.Id,
            DocumentUuid = doc.Uuid,
            Content = dlg.CompletionContent,
            Type = dlg.SelectedType,
            Severity = dlg.SelectedSeverity,
            CreatedBy = config?.CurrentUserName ?? user.Username ?? "Unknown",
            CreatedByUserId = user.Id,
            Status = NoteStatus.Open
        };
        notesStore.Add(note);

        if (dlg.SelectedType == NoteType.Issue)
        {
            var docStore = ServiceContainer.GetService<IDocumentStore>();
            docStore.UpdateStatus(doc.Id, Enums.Status.Issue);
        }

        if (assignmentService.CompleteAssignment(assignment.Id, user, note.Content))
        {
            var cfg = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
            var msg = cfg != null ? ReportLocalizationService.GetString("AssignmentCompleted", cfg) : "Assignment completed.";
            MessageBox.Show(msg, "Mark Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadMyAssignments();
        }
        else
        {
            MessageBox.Show("Could not complete assignment.", "Mark Complete", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ViewDocumentBtn_Click(object sender, RoutedEventArgs e)
    {
        var doc = DocumentList?.SelectedItem as Document;
        if (doc == null || string.IsNullOrEmpty(doc.FilePath) || !File.Exists(doc.FilePath))
        {
            MessageBox.Show("Document file not found.", "View Document", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(doc.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open file: {ex.Message}", "View Document", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelAssignmentBtn_Click(object sender, RoutedEventArgs e)
    {
        var doc = DocumentList?.SelectedItem as Document;
        if (doc == null || !_docToAssignment.TryGetValue(doc.Id, out var assignment)) return;
        var assignmentService = ServiceContainer.GetService<IDocumentAssignmentService>();
        var user = ServiceContainer.GetService<ISessionService>()?.CurrentUser;
        if (user == null) return;
        if (assignment.Status != AssignmentStatus.Pending && assignment.Status != AssignmentStatus.InProgress)
        {
            MessageBox.Show("Can only cancel Pending or In Progress assignments.", "Cancel Assignment", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var result = MessageBox.Show("Cancel this assignment?", "Cancel Assignment", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;
        if (assignmentService.CancelAssignment(assignment.Id, user))
        {
            MessageBox.Show("Assignment cancelled.", "Cancel Assignment", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadMyAssignments();
        }
        else
        {
            MessageBox.Show("Could not cancel assignment.", "Cancel Assignment", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AssignForSelection()
    {
        var selected = DocumentList?.SelectedItems?.Cast<Document>().ToList() ?? new List<Document>();
        if (selected.Count == 0) { MessageBox.Show("Select documents first.", "Assign", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var session = ServiceContainer.GetService<ISessionService>();
        var user = session?.CurrentUser;
        if (user == null) { MessageBox.Show("You must be logged in to assign documents.", "Assign", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var dlg = new AssignDocumentDialog(selected, user);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() == true) { RefreshFollowUpReminderPanel(); RunFilteredSearch(); }
    }

    private void ApplyStatusForSelection(string status)
    {
        var selected = DocumentList?.SelectedItems?.Cast<Document>().ToList() ?? new List<Document>();
        if (selected.Count == 0) return;
        var auditTrail = ServiceContainer.GetService<IAuditTrailService>();
        var changeHistory = ServiceContainer.GetService<IChangeHistoryService>();
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
                changeHistory.RecordFieldChange(doc.Uuid, doc.Id, "status", oldStatus, status);
                _ = auditTrail.LogDocumentActionAsync(AuditAction.DocumentStatusChanged, doc, $"Status set to {status}");
                if (string.Equals(status, Enums.Status.ReadyForAudit, StringComparison.Ordinal))
                    DocumentWorkspaceOcr.TryEnqueueOcrIfNeeded(doc, ocrService, configStore);
            }
        }
        MessageBox.Show($"Updated {updated} of {selected.Count} documents to {status}.", "Set Status", MessageBoxButton.OK, MessageBoxImage.Information);
        if (_isMyAssignmentsView)
            LoadMyAssignments();
        else
            RunFilteredSearch();
    }

    private void FlagFollowUpForSelection()
    {
        var selected = DocumentList?.SelectedItems?.Cast<Document>().ToList() ?? new List<Document>();
        if (selected.Count == 0) { MessageBox.Show("Select documents first.", "Flag for Follow-up", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var due = DialogHelper.ShowFollowUpReminderDialog(Window.GetWindow(this));
        if (!due.HasValue) return;
        var updated = 0;
        foreach (var doc in selected)
        {
            doc.AddTag("follow-up");
            doc.SetFollowUpDue(due);
            if (_store.Update(doc)) updated++;
        }
        MessageBox.Show($"{updated} document(s) flagged for follow-up.", "Flag for Follow-up", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshFollowUpReminderPanel();
        RunFilteredSearch();
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

    private void CorrectClassificationForSelection()
    {
        var selected = DocumentList?.SelectedItems?.Cast<Document>().ToList() ?? new List<Document>();
        if (selected.Count == 0) { MessageBox.Show("Select documents first.", "Correct Classification", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var first = selected[0];
        var result = DialogHelper.ShowSectionFilteredTypeDialog(
            "Correct Classification",
            "Document type:",
            "Section:",
            Enums.SectionValues,
            string.IsNullOrEmpty(first.Section) ? Enums.SectionValues[0] : first.Section,
            selectedSection => DocumentTypeInfo.BuildPickerItems(_docTypeService.GetDocumentTypesForSection(selectedSection)),
            DocumentTypeInfo.PickerSelectedValue(first.DocumentType),
            Window.GetWindow(this));
        if (result == null || result.Value.documentType == null || result.Value.section == null) return;
        var typeToStore = DocumentTypeInfo.NormalizePickerToStorage(result.Value.documentType);
        var newSection = result.Value.section;
        var auditTrail = ServiceContainer.GetService<IAuditTrailService>();
        var changeHistory = ServiceContainer.GetService<IChangeHistoryService>();
        var fileRenameService = ServiceContainer.GetService<IFileRenameService>();
        var success = 0;
        var failedDocs = new List<int>();
        var failedReasons = new Dictionary<int, string>();
        var docsWithTypeUpdate = new List<Document>();
        foreach (var doc in selected)
        {
            var typeOk = TryUpdateWithRetries(() => _store.UpdateDocumentType(doc.Id, typeToStore));
            var sectionOk = TryUpdateWithRetries(() => _store.UpdateDocumentSection(doc.Id, newSection));
            
            // CRITICAL FIX: Fail-fast if EITHER update fails (not just both)
            if (!typeOk || !sectionOk)
            {
                failedDocs.Add(doc.Id);
                failedReasons[doc.Id] = $"Type update: {typeOk}, section update: {sectionOk}.";
                _log.Warning("Document {DocId} classification failed in workspace: typeOk={TypeOk}, sectionOk={SectionOk}", doc.Id, typeOk, sectionOk);
                continue;
            }
            
            var oldType = doc.DocumentType ?? "";
            if (typeOk) doc.DocumentType = string.IsNullOrEmpty(typeToStore) ? null : typeToStore;
            if (sectionOk) doc.Section = newSection;
            if (typeOk)
            {
                changeHistory.RecordFieldChange(doc.Uuid, doc.Id, "document_type", oldType, typeToStore);
                if (!string.IsNullOrWhiteSpace(typeToStore))
                    docsWithTypeUpdate.Add(doc);
            }
            _ = auditTrail.LogDocumentActionAsync(AuditAction.DocumentClassified, doc, $"Corrected: type={typeToStore}, section={newSection}");
            success++;
        }
        ClassificationMovesResult? moveResult = null;
        if (docsWithTypeUpdate.Count > 0)
        {
            moveResult = ClassificationPathHelper.ApplyMovesForDocumentsSharingFiles(
                _store,
                fileRenameService,
                docsWithTypeUpdate,
                typeToStore,
                _baseDir,
                d => DocumentFilePathResolver.ResolveExistingPath(d, _baseDir));

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
                    DocumentPreviewViewer?.SetSource(null);
                    SetPdfPreviewSource(null);
                    Thread.Sleep(250);
                    var secondPass = ClassificationPathHelper.ApplyMovesForDocumentsSharingFiles(
                        _store,
                        fileRenameService,
                        retryDocs,
                        typeToStore,
                        _baseDir,
                        d => DocumentFilePathResolver.ResolveExistingPath(d, _baseDir));
                    moveResult = ClassificationPathHelper.MergeMoveResults(mr, secondPass);
                }
            }
        }

        var verifyIds = selected
            .Select(d => d.Id)
            .Where(id => !failedDocs.Contains(id))
            .ToList();
        if (verifyIds.Count > 0)
        {
            var verifyFailures = ClassificationPathHelper.VerifyClassificationInvariants(
                _store,
                verifyIds,
                typeToStore,
                newSection,
                d => DocumentFilePathResolver.ResolveExistingPath(d, _baseDir));

            foreach (var kv in verifyFailures)
            {
                if (!failedDocs.Contains(kv.Key))
                    failedDocs.Add(kv.Key);
                failedReasons[kv.Key] = kv.Value;
            }
        }

        // Report results including any failures
        var msg = success > 0 ? $"{success} document(s) corrected." : "Classification operation completed with errors.";
        
        if (failedDocs.Count > 0)
        {
            var detail = failedDocs
                .Distinct()
                .Select(id => failedReasons.TryGetValue(id, out var reason) ? $"{id} ({reason})" : $"{id}")
                .ToList();
            msg += $"\n\nFailed to update {failedDocs.Count} document(s). IDs: {string.Join("; ", detail)}";
        }
        
        var hasFailures = failedDocs.Count > 0;
        if (moveResult != null && docsWithTypeUpdate.Count > 0)
        {
            msg += Environment.NewLine + Environment.NewLine + ClassificationPathHelper.FormatMoveFootnote(moveResult);
            hasFailures = hasFailures || moveResult.RenameFailedDocumentIds.Count > 0 || moveResult.UnresolvedPathDocumentIds.Count > 0;
        }
        
        MessageBox.Show(msg, "Correct Classification", MessageBoxButton.OK, 
            hasFailures ? MessageBoxImage.Warning : MessageBoxImage.Information);
        RunFilteredSearch();
    }

    private void AddNoteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_notesDocument == null) return;

        var dialog = new Dialogs.NotesDialog(
            _notesDocument.Id,
            _notesDocument.Uuid,
            System.IO.Path.GetFileName(_notesDocument.FilePath)
        );
        dialog.Owner = Window.GetWindow(this);
        var result = dialog.ShowDialog();

        // Refresh preview and document list after dialog closes (like status actions)
        if (result == true)
        {
            RefreshNotesPreview(_notesDocument.Id);
            if (_isMyAssignmentsView)
                LoadMyAssignments();
            else
                RunFilteredSearch();
        }
    }

    private void ViewNotesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_notesDocument == null) return;

        var dialog = new Dialogs.NotesDialog(
            _notesDocument.Id,
            _notesDocument.Uuid,
            System.IO.Path.GetFileName(_notesDocument.FilePath)
        );
        dialog.Owner = Window.GetWindow(this);
        var result = dialog.ShowDialog();

        // Refresh preview and document list after dialog closes (like status actions)
        if (result == true)
        {
            RefreshNotesPreview(_notesDocument.Id);
            if (_isMyAssignmentsView)
                LoadMyAssignments();
            else
                RunFilteredSearch();
        }
    }

    private void RefreshNotesPreview(int documentId)
    {
        if (NotesPreviewText == null) return;

        var notesStore = ServiceContainer.GetOptionalService<INotesStore>();
        if (notesStore == null)
        {
            NotesPreviewText.Text = "⚠";
            NotesPreviewText.Visibility = Visibility.Visible;
            NotesPreviewText.ToolTip = "Notes system unavailable";
            return;
        }

        var notes = notesStore.GetByDocumentId(documentId);
        if (notes.Count == 0)
        {
            NotesPreviewText.Visibility = Visibility.Collapsed;
            NotesPreviewText.ToolTip = null;
            return;
        }

        // Count by severity and status (shown in tooltip only; strip shows a single indicator)
        var criticalCount = notes.Count(n => n.Severity == NoteSeverity.Critical);
        var highCount = notes.Count(n => n.Severity == NoteSeverity.High);
        var openCount = notes.Count(n => n.Status == NoteStatus.Open || n.Status == NoteStatus.InProgress);

        var statusParts = new List<string>();
        if (criticalCount > 0) statusParts.Add($"{criticalCount} critical ⚠️");
        else if (highCount > 0) statusParts.Add($"{highCount} high");
        if (openCount > 0) statusParts.Add($"{openCount} open");

        var statusSummary = statusParts.Count > 0 ? $" ({string.Join(", ", statusParts)})" : "";

        var latestNote = notes.OrderByDescending(n => n.CreatedAt).FirstOrDefault();
        string tip;
        if (latestNote != null)
        {
            var content = latestNote.Content ?? "";
            var preview = content.Length > 120 ? content.Substring(0, 120) + "…" : content;
            tip = $"{notes.Count} note(s){statusSummary}\nLatest: \"{preview}\"";
        }
        else
            tip = $"{notes.Count} note(s){statusSummary}";

        NotesPreviewText.Text = "💡";
        NotesPreviewText.Visibility = Visibility.Visible;
        NotesPreviewText.ToolTip = tip;
    }

    #endregion

    #region Follow-up reminder

    private void RefreshFollowUpReminderPanel()
    {
        if (FollowUpReminderPanel == null || FollowUpReminderList == null) return;
        var session = ServiceContainer.GetService<ISessionService>();
        var user = session?.CurrentUser;
        var assignmentService = ServiceContainer.GetService<IDocumentAssignmentService>();
        var permissionService = ServiceContainer.GetService<IPermissionService>();
        var assignedIds = new HashSet<int>();
        if (user != null)
        {
            foreach (var a in assignmentService.GetMyAssignments())
                assignedIds.Add(a.DocumentId);
        }
        var username = user?.Username ?? "";
        var branchFilter = permissionService.GetEffectiveDocumentListBranchFilter();
        var allDocs = _store.ListDocuments(branch: branchFilter, limit: 10000);
        if (!permissionService.HasMinimumRole(Roles.Manager) && user != null)
        {
            var existing = allDocs.Select(d => d.Id).ToHashSet();
            var extraIds = assignmentService.GetMyAssignments()
                .Select(a => a.DocumentId).Where(id => !existing.Contains(id)).Distinct().ToList();
            if (extraIds.Count > 0)
                allDocs = allDocs.Concat(_store.GetByIds(extraIds)).GroupBy(d => d.Id).Select(g => g.First()).ToList();
        }

        var followUpDocs = allDocs
            .Where(d => permissionService.CanAccessDocument(d))
            .Where(d => d.GetTags().Contains("follow-up", StringComparer.OrdinalIgnoreCase) && d.GetFollowUpDue() is { } due && due.Date <= DateTime.UtcNow.Date)
            .Where(d => string.IsNullOrEmpty(username) || assignedIds.Contains(d.Id) || (d.CreatedBy != null && d.CreatedBy.Equals(username, StringComparison.OrdinalIgnoreCase)) || (d.ReviewedBy != null && d.ReviewedBy.Equals(username, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(d => d.GetFollowUpDue())
            .Take(10)
            .ToList();
        FollowUpReminderList.ItemsSource = followUpDocs;
        FollowUpReminderPanel.Visibility = followUpDocs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FollowUpReminderItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.DataContext is Document doc)
        {
            var list = _workspaceViewModel.CurrentDocuments.ToList();
            var idx = list.FindIndex(d => d.Id == doc.Id);
            if (idx >= 0 && DocumentList != null) DocumentList.SelectedIndex = idx;
            else
            {
                RunFilteredSearch();
                list = _workspaceViewModel.CurrentDocuments.ToList();
                var i = list.FindIndex(d => d.Id == doc.Id);
                if (i >= 0 && DocumentList != null) DocumentList.SelectedIndex = i;
            }
        }
    }

    private void FollowUpReminderDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (FollowUpReminderPanel != null) FollowUpReminderPanel.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Browser

    private void LoadQuickLinks()
    {
        if (QuickLinksChips == null) return;
        var list = UserSettings.Get<List<QuickLink>>(Constants.WorkspaceBrowserQuickLinksKey, null) ?? new List<QuickLink>();
        _quickLinks = new ObservableCollection<QuickLink>(list);
        QuickLinksChips.ItemsSource = _quickLinks;
    }

    private void SaveQuickLinks()
    {
        try
        {
            UserSettings.Set(Constants.WorkspaceBrowserQuickLinksKey, _quickLinks.ToList());
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to save quick links");
        }
    }

    private void QuickLinkChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.DataContext is not QuickLink link || string.IsNullOrWhiteSpace(link.Url)) return;
        NavigateToUrl(NormalizeUrl(link.Url));
        if (UrlBox != null) UrlBox.Text = link.Url;
    }

    private void QuickLinkRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var ctx = menuItem.Parent as ContextMenu;
        var target = ctx?.PlacementTarget as FrameworkElement;
        if (target?.DataContext is QuickLink link)
        {
            _quickLinks.Remove(link);
            SaveQuickLinks();
        }
    }

    private void AddQuickLink_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var result = DialogHelper.ShowTwoTextBoxDialog("Add quick link", "Title", "URL", "", "", owner);
        if (result is not { } tuple || string.IsNullOrWhiteSpace(tuple.value1) || string.IsNullOrWhiteSpace(tuple.value2)) return;
        var title = tuple.value1.Trim();
        var url = NormalizeUrl(tuple.value2.Trim());
        _quickLinks.Add(new QuickLink { Title = title, Url = url });
        SaveQuickLinks();
        NavigateToUrl(url);
        if (UrlBox != null) UrlBox.Text = url;
    }

    private static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "https://" + url;
        return url;
    }

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) NavigateToUrl();
    }

    private void Go_Click(object sender, RoutedEventArgs e)
    {
        NavigateToUrl();
    }

    private void NavigateToUrl()
    {
        var url = UrlBox?.Text?.Trim();
        if (string.IsNullOrEmpty(url)) return;
        NavigateToUrl(NormalizeUrl(url));
    }

    private async void NavigateToUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            await EnsureWorkspaceWebViewReadyAsync();
            if (WebBrowser != null)
                WebBrowser.Source = new Uri(url);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to set WebBrowser source: {Url}", url);
        }
    }

    private async Task InitializeWorkspaceWebViewAsync()
    {
        try
        {
            if (WebBrowser == null) return;
            var env = await WebView2EnvironmentHelper.CreateForAppAsync("Workspace");
            await WebBrowser.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Workspace WebView2 initialization failed");
        }
    }

    private async Task EnsureWorkspaceWebViewReadyAsync()
    {
        _workspaceWebViewInitTask ??= InitializeWorkspaceWebViewAsync();
        await _workspaceWebViewInitTask;
    }

    #endregion

    #region Helpers

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void UpdateMetadataDisplay(Document? doc)
    {
        if (MetadataText == null) return;
        if (doc == null)
        {
            MetadataText.Text = "No document selected" + Environment.NewLine + "Choose a row in Documents or a folder in the file tree.";
            if (AccountFieldsCard != null) AccountFieldsCard.Opacity = 0.55;
            return;
        }

        if (AccountFieldsCard != null) AccountFieldsCard.Opacity = 1.0;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Date: {doc.DateDisplay}");
        sb.AppendLine($"Type: {doc.DocumentType ?? "—"}");
        sb.AppendLine($"Section: {doc.Section ?? "—"}");
        sb.AppendLine($"Branch: {doc.Branch ?? "—"}");
        sb.AppendLine($"Status: {doc.StatusDisplay}");
        sb.AppendLine($"File: {Path.GetFileName(doc.FilePath) ?? "—"}");
        sb.AppendLine($"Size: {doc.FileSizeDisplay}");
        var tags = doc.GetTags();
        if (tags.Length > 0) sb.AppendLine($"Tags: {string.Join(", ", tags)}");
        if (!string.IsNullOrWhiteSpace(doc.AccountName)) sb.AppendLine($"Account / party: {doc.AccountName.Trim()}");
        if (!string.IsNullOrWhiteSpace(doc.AccountNumber)) sb.AppendLine($"Account no.: {doc.AccountNumber.Trim()}");
        if (!string.IsNullOrWhiteSpace(doc.TransactionReference)) sb.AppendLine($"Transaction ref.: {doc.TransactionReference.Trim()}");
        var due = doc.GetFollowUpDue();
        if (due.HasValue) sb.AppendLine($"Follow-up due: {due.Value:yyyy-MM-dd}");
        if (_isMyAssignmentsView && _docToAssignment.TryGetValue(doc.Id, out var assignment))
        {
            var config = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
            var priorityStr = config != null ? ReportLocalizationService.GetString(assignment.Priority, config) : assignment.Priority;
            var statusStr = config != null ? ReportLocalizationService.GetString(assignment.Status, config) : assignment.Status;
            var assignmentService = ServiceContainer.GetService<IDocumentAssignmentService>();
            var isOverdue = assignmentService.IsOverdue(assignment);
            sb.AppendLine("");
            sb.AppendLine($"Assignment: {statusStr}{(isOverdue ? " (Overdue)" : "")}");
            sb.AppendLine($"Priority: {priorityStr}");
            sb.AppendLine($"Due: {FormatDueDate(assignment.DueDate, assignment.Status, isOverdue, config)}");
            sb.AppendLine($"Assigned by: {assignment.AssignedByUsername}");
        }
        MetadataText.Text = sb.ToString().TrimEnd();
    }

    private void RefreshAccountFields(Document? doc)
    {
        if (AccountNameBox == null || AccountNumberBox == null || TransactionReferenceBox == null || SaveAccountFieldsBtn == null)
            return;
        if (doc == null)
        {
            AccountNameBox.Text = "";
            AccountNumberBox.Text = "";
            TransactionReferenceBox.Text = "";
            AccountNameBox.IsEnabled = false;
            AccountNumberBox.IsEnabled = false;
            TransactionReferenceBox.IsEnabled = false;
            SaveAccountFieldsBtn.IsEnabled = false;
            return;
        }

        AccountNameBox.Text = doc.AccountName ?? "";
        AccountNumberBox.Text = doc.AccountNumber ?? "";
        TransactionReferenceBox.Text = doc.TransactionReference ?? "";
        var canEdit = ServiceContainer.GetService<IPermissionService>().CanEditDocument(doc);
        AccountNameBox.IsEnabled = canEdit;
        AccountNumberBox.IsEnabled = canEdit;
        TransactionReferenceBox.IsEnabled = canEdit;
        SaveAccountFieldsBtn.IsEnabled = canEdit;
    }

    private void SaveAccountFieldsBtn_Click(object sender, RoutedEventArgs e)
    {
        var doc = DocumentList?.SelectedItem as Document;
        if (doc == null) return;
        if (!ServiceContainer.GetService<IPermissionService>().CanEditDocument(doc)) return;

        doc.AccountName = string.IsNullOrWhiteSpace(AccountNameBox?.Text) ? null : AccountNameBox.Text.Trim();
        doc.AccountNumber = string.IsNullOrWhiteSpace(AccountNumberBox?.Text) ? null : AccountNumberBox.Text.Trim();
        doc.TransactionReference = string.IsNullOrWhiteSpace(TransactionReferenceBox?.Text) ? null : TransactionReferenceBox.Text.Trim();

        if (!_store.Update(doc))
        {
            MessageBox.Show(Window.GetWindow(this), "Could not save account fields.", "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UpdateMetadataDisplay(doc);
        _log.Information("Updated account fields for document {Id}", doc.Id);
    }

    private static string FormatDueDate(string? dueDate, string status, bool isOverdue, IConfigStore? config)
    {
        if (string.IsNullOrEmpty(dueDate) || status == AssignmentStatus.Completed || status == AssignmentStatus.Cancelled)
            return "—";
        if (!DateTime.TryParse(dueDate, out var due)) return dueDate;
        var today = DateTime.Today;
        var diff = (due.Date - today).Days;
        if (diff < 0) return config != null ? ReportLocalizationService.GetString("OverdueByDays", config, -diff) : $"Overdue by {-diff} day{(-diff == 1 ? "" : "s")}";
        if (diff == 0) return config != null ? ReportLocalizationService.GetString("DueToday", config) : "Due today";
        if (diff == 1) return config != null ? ReportLocalizationService.GetString("DueTomorrow", config) : "Due tomorrow";
        return config != null ? ReportLocalizationService.GetString("DueInDays", config, diff) : $"Due in {diff} days";
    }

    private void UpdateAssignmentActionButtons(Document? doc)
    {
        DocumentAssignment? assignment = null;
        if (_isMyAssignmentsView && doc != null && _docToAssignment.TryGetValue(doc.Id, out var a))
            assignment = a;
        var hasAssignment = assignment != null;
        if (AssignmentActionsPanel != null)
            AssignmentActionsPanel.Visibility = hasAssignment ? Visibility.Visible : Visibility.Collapsed;
        if (MarkCompleteBtn != null)
        {
            MarkCompleteBtn.Visibility = hasAssignment ? Visibility.Visible : Visibility.Collapsed;
            MarkCompleteBtn.IsEnabled = hasAssignment && (assignment!.Status == AssignmentStatus.Pending || assignment.Status == AssignmentStatus.InProgress);
        }
        if (ViewDocumentBtn != null)
        {
            ViewDocumentBtn.Visibility = hasAssignment ? Visibility.Visible : Visibility.Collapsed;
            ViewDocumentBtn.IsEnabled = hasAssignment;
        }
        if (CancelAssignmentBtn != null)
        {
            CancelAssignmentBtn.Visibility = hasAssignment ? Visibility.Visible : Visibility.Collapsed;
            var canCancel = hasAssignment && (assignment!.Status == AssignmentStatus.Pending || assignment.Status == AssignmentStatus.InProgress);
            CancelAssignmentBtn.IsEnabled = canCancel;
        }
    }

    private void UpdateAssignButtonVisibility()
    {
        var permissionService = ServiceContainer.GetService<IPermissionService>();
        var canAssign = permissionService.HasMinimumRole(Roles.Manager);
        if (AssignmentGroupBox != null) AssignmentGroupBox.Visibility = canAssign ? Visibility.Visible : Visibility.Collapsed;
        if (AssignBtn != null) AssignBtn.Visibility = canAssign ? Visibility.Visible : Visibility.Collapsed;
        var selected = DocumentList?.SelectedItems?.Cast<Document>().ToList() ?? new List<Document>();
        if (AssignBtn != null) AssignBtn.IsEnabled = selected.Count > 0 && canAssign;
        if (SetStatusBtn != null) SetStatusBtn.IsEnabled = selected.Count > 0;
        if (FlagFollowUpBtn != null) FlagFollowUpBtn.IsEnabled = selected.Count > 0;
        if (CorrectClassificationBtn != null) CorrectClassificationBtn.IsEnabled = selected.Count > 0;
        var canArchive = permissionService.HasPermission(Permissions.ArchiveCreate);
        if (MoveToArchiveBtn != null)
        {
            MoveToArchiveBtn.Visibility = canArchive ? Visibility.Visible : Visibility.Collapsed;
            MoveToArchiveBtn.IsEnabled = selected.Count > 0 && canArchive && selected.Any(d => d.Status != Enums.Status.Archived);
        }
    }

    private void UpdateFlagFollowUpButtonState(IEnumerable<Document> selected)
    {
        if (FlagFollowUpBtn != null) FlagFollowUpBtn.IsEnabled = selected.Any();
    }

    private void SetPdfPreviewSource(string? filePath, Action? onSourceApplied = null)
    {
        _pdfPreviewLoad.SetSource(PdfPreviewViewer, filePath, onSourceApplied);
    }

    private void ShowDocumentPreview(Document? doc)
    {
        if (DocumentPreviewViewer == null || PdfPreviewViewer == null || PdfPreviewHost == null) return;

        _previewShowsRasterImage = false;
        _previewShowsPdf = false;

        var oldId = _previewDocumentId;
        var hadImage = DocumentPreviewViewer.Visibility == Visibility.Visible;
        var hadPdf = PdfPreviewHost.Visibility == Visibility.Visible;
        if (oldId.HasValue && (doc == null || doc.Id != oldId.Value))
            ResolveDirtyMarkupForPreviousDocument(oldId.Value, hadImage, hadPdf);

        DocumentPreviewViewer.Visibility = Visibility.Collapsed;
        PdfPreviewHost.Visibility = Visibility.Collapsed;
        if (WorkspaceMarkupToolBar != null) WorkspaceMarkupToolBar.Visibility = Visibility.Collapsed;
        if (PdfPageJumpGroup != null) PdfPageJumpGroup.Visibility = Visibility.Collapsed;

        if (doc == null || string.IsNullOrEmpty(doc.FilePath) || !File.Exists(doc.FilePath))
        {
            SetPdfPreviewSource(null);
            DocumentPreviewViewer.SetSource(null);
            ClearWorkspacePdfMarkup();
            if (WorkspacePdfPreviewTools != null) WorkspacePdfPreviewTools.Visibility = Visibility.Collapsed;
            _previewDocumentId = null;
            SyncPreviewTextOnImageAfterDocumentChange();
            return;
        }

        var path = doc.FilePath;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var canMarkup = CanUseAuditorMarkup(doc);

        if (ext == ".pdf")
        {
            _previewShowsPdf = true;
            DocumentPreviewViewer.SetSource(null);
            if (WorkspacePdfPreviewTools != null)
            {
                WorkspacePdfPreviewTools.Visibility = Visibility.Visible;
                WorkspacePdfPreviewTools.SetTargetViewer(PdfPreviewViewer);
            }
            SetPdfPreviewSource(path, () =>
            {
                if (!IsPrimaryDocumentStillSelected(doc)) return;
                PdfPreviewHost.Visibility = Visibility.Visible;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!IsPrimaryDocumentStillSelected(doc) || PdfPreviewViewer == null) return;
                    PdfiumViewerTools.ApplyDefaultDocumentZoom(PdfPreviewViewer);
                    WorkspacePdfPreviewTools?.SetTargetViewer(PdfPreviewViewer);
                    SyncPdfMarkupCanvasSize();
                    LoadPdfMarkupForDocument(doc.Id);
                    SetPdfMarkupReadOnly(!canMarkup);
                    if (!_pdfMouseHooked)
                    {
                        PdfPreviewViewer.PreviewMouseLeftButtonUp += PdfPreviewViewer_PageInteraction;
                        _pdfMouseHooked = true;
                    }
                    if (canMarkup)
                    {
                        if (WorkspaceMarkupToolBar != null) WorkspaceMarkupToolBar.Visibility = Visibility.Visible;
                        if (PdfPageJumpGroup != null) PdfPageJumpGroup.Visibility = Visibility.Visible;
                        ApplyWorkspaceMarkupMode(DocumentMarkupTool.Browse);
                        SyncWorkspaceMarkupRadios(DocumentMarkupTool.Browse);
                    }
                }), DispatcherPriority.Loaded);
            });
        }
        else if (PreviewExtensions.Contains(ext))
        {
            _previewShowsPdf = false;
            _previewShowsRasterImage = true;
            if (WorkspacePdfPreviewTools != null) WorkspacePdfPreviewTools.Visibility = Visibility.Collapsed;
            SetPdfPreviewSource(null);
            ClearWorkspacePdfMarkup();
            DocumentPreviewViewer.SetSource(path);
            DocumentPreviewViewer.Visibility = Visibility.Visible;
            if (IsPrimaryDocumentStillSelected(doc))
                LoadImageMarkupForDocument(doc.Id);
            DocumentPreviewViewer.MarkupReadOnly = !canMarkup;
            if (canMarkup)
            {
                if (WorkspaceMarkupToolBar != null) WorkspaceMarkupToolBar.Visibility = Visibility.Visible;
                ApplyWorkspaceMarkupMode(DocumentMarkupTool.Browse);
                SyncWorkspaceMarkupRadios(DocumentMarkupTool.Browse);
            }
        }
        else
        {
            _previewShowsPdf = false;
            if (WorkspacePdfPreviewTools != null) WorkspacePdfPreviewTools.Visibility = Visibility.Collapsed;
            SetPdfPreviewSource(null);
            DocumentPreviewViewer.SetSource(null);
            ClearWorkspacePdfMarkup();
        }

        _previewDocumentId = doc?.Id;
        SyncPreviewTextOnImageAfterDocumentChange();
    }

    #endregion
}
