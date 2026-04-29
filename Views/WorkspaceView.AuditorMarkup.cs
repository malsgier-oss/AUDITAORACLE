using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic;
using Serilog;
using WorkAudit.Config;
using WorkAudit.Controls;
using WorkAudit.Core.Helpers;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Dialogs;
using WorkAudit.Domain;
using WRectangle = System.Windows.Shapes.Rectangle;
using WBrushes = System.Windows.Media.Brushes;
using WColor = System.Windows.Media.Color;
using WPoint = System.Windows.Point;

namespace WorkAudit.Views;

public partial class WorkspaceView
{
    private const string HighlightFill = "#80FFFF00";
    private const string RectangleStroke = "#CCFF0000";

    private readonly List<MarkupRegion> _pdfMarkupModel = new();
    private bool _pdfMarkupDirty;
    private DocumentMarkupTool _pdfMarkupTool = DocumentMarkupTool.Browse;
    private bool _pdfMarkupReadOnly = true;
    private WPoint? _pdfDrawStart;
    private WRectangle? _pdfRubberBand;
    private bool _pdfMouseHooked;
    private bool _pdfPanDragging;
    private WPoint _pdfPanLast;
    private List<WPoint>? _pdfInkPoints;
    private System.Windows.Shapes.Polyline? _pdfInkPolyline;

    private bool CanUseAuditorMarkup(Document doc)
    {
        var perm = ServiceContainer.GetService<IPermissionService>();
        return perm.HasMinimumRole(Roles.Auditor) && perm.CanEditDocument(doc);
    }

    private string GetMarkupActorLabel()
    {
        var user = ServiceContainer.GetService<ISessionService>().CurrentUser;
        var cfg = ServiceContainer.GetService<AppConfiguration>();
        return cfg?.CurrentUserName ?? user?.Username ?? "Unknown";
    }

    private bool HasDirtyMarkup() =>
        (DocumentPreviewViewer?.IsMarkupDirty == true) || _pdfMarkupDirty;

    private void ResolveDirtyMarkupForPreviousDocument(int documentId, bool hadImage, bool hadPdf)
    {
        if (!HasDirtyMarkup()) return;
        var gr = _store.GetResult(documentId);
        if (!gr.IsSuccess)
            _log.Warning("Could not load document {Id} for markup prompt: {Error}", documentId, gr.Error);
        var name = gr.IsSuccess ? System.IO.Path.GetFileName(gr.Value!.FilePath) : $"#{documentId}";
        var res = MessageBox.Show(
            $"Save markup changes for \"{name}\" before leaving?",
            "Markup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
            PersistMarkupForDocument(documentId, hadImage, hadPdf, gr.IsSuccess ? gr.Value : null);
        else
            DiscardMarkupUiState();
    }

    private void DiscardMarkupUiState()
    {
        DocumentPreviewViewer?.ClearMarkup();
        ClearWorkspacePdfMarkup();
    }

    private void PersistMarkupForDocument(int documentId, bool hadImage, bool hadPdf, Document? doc)
    {
        if (hadImage && DocumentPreviewViewer != null)
        {
            var imageRegions = DocumentPreviewViewer.GetMarkupRegionsForSave().ToList();
            foreach (var m in imageRegions)
            {
                m.DocumentId = documentId;
                m.CreatedBy = GetMarkupActorLabel();
                m.CreatedAt = string.IsNullOrEmpty(m.CreatedAt) ? DateTime.UtcNow.ToString("O") : m.CreatedAt;
            }
            var keepPdf = _markupStore.GetByDocumentId(documentId)
                .Where(m => m.PreviewSurface == MarkupPreviewSurface.Pdf)
                .Select(CloneMarkupRegion)
                .ToList();
            _markupStore.ReplaceForDocument(documentId, keepPdf.Concat(imageRegions).ToList(), GetMarkupActorLabel());
            DocumentPreviewViewer.MarkMarkupSaved();
        }
        else if (hadPdf)
        {
            var pdfRegions = _pdfMarkupModel
                .Where(m => m.PreviewSurface == MarkupPreviewSurface.Pdf)
                .Select(CloneMarkupRegion)
                .ToList();
            foreach (var m in pdfRegions)
            {
                m.DocumentId = documentId;
                m.CreatedBy = GetMarkupActorLabel();
                if (string.IsNullOrEmpty(m.CreatedAt)) m.CreatedAt = DateTime.UtcNow.ToString("O");
            }
            var keepImage = _markupStore.GetByDocumentId(documentId)
                .Where(m => m.PreviewSurface == MarkupPreviewSurface.Image)
                .Select(CloneMarkupRegion)
                .ToList();
            _markupStore.ReplaceForDocument(documentId, keepImage.Concat(pdfRegions).ToList(), GetMarkupActorLabel());
            _pdfMarkupDirty = false;
        }

        if (doc == null)
        {
            var load = _store.GetResult(documentId);
            if (load.IsSuccess)
                doc = load.Value;
            else
                _log.Warning("Could not load document {Id} for markup audit log: {Error}", documentId, load.Error);
        }
        if (doc != null)
            _ = ServiceContainer.GetService<IAuditTrailService>().LogDocumentActionAsync(AuditAction.DocumentMarkupSaved, doc, "Preview markup saved");
    }

    private static MarkupRegion CloneMarkupRegion(MarkupRegion m) => new()
    {
        Id = m.Id,
        DocumentId = m.DocumentId,
        Kind = m.Kind,
        Color = m.Color,
        NormX = m.NormX,
        NormY = m.NormY,
        NormW = m.NormW,
        NormH = m.NormH,
        PageIndex = m.PageIndex,
        PreviewSurface = m.PreviewSurface,
        NoteId = m.NoteId,
        Label = m.Label,
        CreatedAt = m.CreatedAt,
        CreatedBy = m.CreatedBy
    };

    private void LoadImageMarkupForDocument(int documentId)
    {
        var all = _markupStore.GetByDocumentId(documentId);
        DocumentPreviewViewer?.SetMarkupRegions(all.Where(m => m.PreviewSurface == MarkupPreviewSurface.Image).ToList());
    }

    private void LoadPdfMarkupForDocument(int documentId)
    {
        _pdfMarkupModel.Clear();
        foreach (var m in _markupStore.GetByDocumentId(documentId).Where(x => x.PreviewSurface == MarkupPreviewSurface.Pdf))
            _pdfMarkupModel.Add(CloneMarkupRegion(m));
        _pdfMarkupDirty = false;
        RebuildPdfMarkupShapes();
    }

    private void ClearWorkspacePdfMarkup()
    {
        if (_pdfMouseHooked && PdfPreviewViewer != null)
        {
            PdfPreviewViewer.PreviewMouseLeftButtonUp -= PdfPreviewViewer_PageInteraction;
            _pdfMouseHooked = false;
        }
        _pdfMarkupModel.Clear();
        _pdfMarkupDirty = false;
        _pdfDrawStart = null;
        _pdfRubberBand = null;
        WorkspacePdfMarkupCanvas?.Children.Clear();
    }

    private void PdfPreviewViewer_PageInteraction(object sender, MouseButtonEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try { RebuildPdfMarkupShapes(); }
            catch (Exception ex) { _log.Warning(ex, "RebuildPdfMarkupShapes after PDF interaction (often rapid navigation)"); }
        }), DispatcherPriority.Background);
    }

    private int GetPdfPageIndex0()
    {
        try
        {
            var p = PdfPreviewViewer?.Page;
            if (p is int ip)
                return Math.Max(0, ip - 1);
        }
        catch { /* Page may be 0-based on some builds */ }
        return 0;
    }

    private void SyncPdfMarkupCanvasSize()
    {
        if (WorkspacePdfMarkupCanvas == null || PdfPreviewViewer == null) return;
        WorkspacePdfMarkupCanvas.Width = PdfPreviewViewer.ActualWidth;
        WorkspacePdfMarkupCanvas.Height = PdfPreviewViewer.ActualHeight;
        RebuildPdfMarkupShapes();
    }

    private void SetPdfMarkupReadOnly(bool readOnly)
    {
        _pdfMarkupReadOnly = readOnly;
        UpdatePdfMarkupCanvasHitTest();
    }

    private void UpdatePdfMarkupCanvasHitTest()
    {
        if (WorkspacePdfMarkupCanvas == null) return;
        // Browse lets the PDF control receive clicks/wheel (scrollbars). Pan and drawing tools need the overlay.
        WorkspacePdfMarkupCanvas.IsHitTestVisible = !_pdfMarkupReadOnly && _pdfMarkupTool != DocumentMarkupTool.Browse;
    }

    /// <summary>
    /// When the markup overlay is hit-testable it sits above <see cref="PdfPreviewViewer"/> and would
    /// otherwise swallow the wheel; forward scroll to the viewer's internal ScrollViewer.
    /// </summary>
    private void WorkspacePdfMarkupCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (PdfPreviewViewer == null) return;
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Delta > 0) PdfiumViewerTools.ZoomIn(PdfPreviewViewer);
            else PdfiumViewerTools.ZoomOut(PdfPreviewViewer);
            e.Handled = true;
            return;
        }

        var lines = Math.Max(1.0, SystemParameters.WheelScrollLines);
        var pixelsPerNotch = lines * 16.0;
        var delta = -(e.Delta / 120.0) * pixelsPerNotch;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            PdfiumViewerTools.Pan(PdfPreviewViewer, delta, 0);
        else
            PdfiumViewerTools.Pan(PdfPreviewViewer, 0, delta);
        e.Handled = true;
    }

    /// <summary>Ctrl+wheel zoom on PDF when the markup overlay is not capturing (Browse mode).</summary>
    private void PdfPreviewHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (PdfPreviewViewer == null) return;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
        if (e.Delta > 0) PdfiumViewerTools.ZoomIn(PdfPreviewViewer);
        else PdfiumViewerTools.ZoomOut(PdfPreviewViewer);
        e.Handled = true;
    }

    private void RebuildPdfMarkupShapes()
    {
        if (WorkspacePdfMarkupCanvas == null) return;
        WorkspacePdfMarkupCanvas.Children.Clear();
        var cw = WorkspacePdfMarkupCanvas.ActualWidth;
        var ch = WorkspacePdfMarkupCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return;
        var page = GetPdfPageIndex0();
        foreach (var m in _pdfMarkupModel.Where(x => x.PreviewSurface == MarkupPreviewSurface.Pdf && x.PageIndex == page))
            WorkspacePdfMarkupCanvas.Children.Add(MarkupPreviewShapeFactory.Create(m, cw, ch));
    }

    private void ApplyWorkspaceMarkupMode(DocumentMarkupTool mode)
    {
        if (DocumentPreviewViewer != null)
            DocumentPreviewViewer.MarkupTool = mode;
        _pdfMarkupTool = mode;
        UpdatePdfMarkupCanvasHitTest();
    }

    private void SyncWorkspaceMarkupRadios(DocumentMarkupTool mode)
    {
        if (MarkupPanBtn == null) return;
        if (MarkupBrowseBtn != null) MarkupBrowseBtn.IsChecked = mode == DocumentMarkupTool.Browse;
        MarkupPanBtn.IsChecked = mode == DocumentMarkupTool.Pan;
        if (MarkupHighlightBtn != null) MarkupHighlightBtn.IsChecked = mode == DocumentMarkupTool.Highlight;
        if (MarkupRectBtn != null) MarkupRectBtn.IsChecked = mode == DocumentMarkupTool.Rectangle;
        if (MarkupEraseBtn != null) MarkupEraseBtn.IsChecked = mode == DocumentMarkupTool.Erase;
        if (MarkupMarkupBtn != null) MarkupMarkupBtn.IsChecked = mode == DocumentMarkupTool.Markup;
        if (MarkupTextBtn != null) MarkupTextBtn.IsChecked = mode == DocumentMarkupTool.Text;
    }

    private void MarkupBrowse_Checked(object sender, RoutedEventArgs e) => ApplyWorkspaceMarkupMode(DocumentMarkupTool.Browse);
    private void MarkupPan_Checked(object sender, RoutedEventArgs e) => ApplyWorkspaceMarkupMode(DocumentMarkupTool.Pan);
    private void MarkupHighlight_Checked(object sender, RoutedEventArgs e) => ApplyWorkspaceMarkupMode(DocumentMarkupTool.Highlight);
    private void MarkupRect_Checked(object sender, RoutedEventArgs e) => ApplyWorkspaceMarkupMode(DocumentMarkupTool.Rectangle);
    private void MarkupErase_Checked(object sender, RoutedEventArgs e) => ApplyWorkspaceMarkupMode(DocumentMarkupTool.Erase);
    private void MarkupMarkup_Checked(object sender, RoutedEventArgs e) => ApplyWorkspaceMarkupMode(DocumentMarkupTool.Markup);
    private void MarkupText_Checked(object sender, RoutedEventArgs e) => ApplyWorkspaceMarkupMode(DocumentMarkupTool.Text);

    private void MarkupSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_previewDocumentId.HasValue) return;
        var getResult = _store.GetResult(_previewDocumentId.Value);
        if (!getResult.IsSuccess)
        {
            _log.Warning("Could not load document {Id} for markup save: {Error}", _previewDocumentId.Value, getResult.Error);
            MessageBox.Show($"Failed to load document: {getResult.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        var doc = getResult.Value!;
        var hadImage = DocumentPreviewViewer?.Visibility == Visibility.Visible;
        var hadPdf = PdfPreviewHost?.Visibility == Visibility.Visible;
        PersistMarkupForDocument(_previewDocumentId.Value, hadImage, hadPdf, doc);
        MessageBox.Show("Markup saved.", "Markup", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MarkupNoteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_notesDocument == null) return;
        var dlg = new NotesDialog(_notesDocument.Id, _notesDocument.Uuid, System.IO.Path.GetFileName(_notesDocument.FilePath ?? ""));
        dlg.Owner = Window.GetWindow(this);
        dlg.ShowDialog();
    }

    /// <summary>
    /// If the page jump box contains a number, applies it to <see cref="PdfPreviewViewer"/> (clamped) and rebuilds markup.
    /// Call this before rasterizing for Text-on-image so the user does not have to press Go after typing a page.
    /// </summary>
    /// <returns>false when the box is empty or not a valid integer (viewer unchanged).</returns>
    private bool TryParseAndApplyPdfPageFromJumpBox()
    {
        if (PdfPreviewViewer == null) return false;
        if (!int.TryParse(PdfPageJumpBox?.Text?.Trim(), out var page1)) return false;
        if (DocumentList?.SelectedItem is Document d && d.PageCount is int pc && pc > 0)
            page1 = Math.Clamp(page1, 1, pc);
        else
            page1 = Math.Max(1, page1);
        PdfPreviewViewer.Page = page1;
        if (PdfPageJumpBox != null)
            PdfPageJumpBox.Text = page1.ToString(CultureInfo.InvariantCulture);
        RebuildPdfMarkupShapes();
        return true;
    }

    private void PdfPageGoBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseAndApplyPdfPageFromJumpBox()) return;
        if (_pdfTextOnImageMode && PreviewTextOnImageToggle?.IsChecked == true)
            _ = RestartPdfTextOnImageForCurrentPageAsync();
    }

    private void WorkspacePdfMarkupCanvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_pdfMarkupReadOnly || WorkspacePdfMarkupCanvas == null || PdfPreviewHost == null || PdfPreviewHost.Visibility != Visibility.Visible) return;
        var pos = e.GetPosition(WorkspacePdfMarkupCanvas);
        var cw = WorkspacePdfMarkupCanvas.ActualWidth;
        var ch = WorkspacePdfMarkupCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return;

        if (_pdfMarkupTool == DocumentMarkupTool.Pan)
        {
            e.Handled = true;
            _pdfPanDragging = true;
            _pdfPanLast = pos;
            Mouse.Capture(WorkspacePdfMarkupCanvas, CaptureMode.SubTree);
            WorkspacePdfMarkupCanvas.Cursor = System.Windows.Input.Cursors.Hand;
            return;
        }

        if (_pdfMarkupTool == DocumentMarkupTool.Text)
        {
            e.Handled = true;
            var text = Interaction.InputBox("Enter annotation text:", "Text", "", -1, -1);
            if (string.IsNullOrWhiteSpace(text)) return;
            const double tw = 0.28;
            const double th = 0.06;
            var nx = Math.Clamp(pos.X / cw - 0.02, 0, 1 - tw);
            var ny = Math.Clamp(pos.Y / ch - 0.02, 0, 1 - th);
            _pdfMarkupModel.Add(new MarkupRegion
            {
                Kind = MarkupKind.Text,
                Color = MarkupPreviewShapeFactory.TextForegroundColor,
                NormX = nx,
                NormY = ny,
                NormW = tw,
                NormH = th,
                Label = text.Trim(),
                PageIndex = GetPdfPageIndex0(),
                PreviewSurface = MarkupPreviewSurface.Pdf,
                CreatedBy = GetMarkupActorLabel(),
                CreatedAt = DateTime.UtcNow.ToString("O")
            });
            _pdfMarkupDirty = true;
            RebuildPdfMarkupShapes();
            return;
        }

        if (_pdfMarkupTool == DocumentMarkupTool.Markup)
        {
            e.Handled = true;
            _pdfInkPoints = new List<WPoint> { pos };
            var stroke = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(MarkupPreviewShapeFactory.InkStrokeColor)!;
            _pdfInkPolyline = new System.Windows.Shapes.Polyline
            {
                Stroke = stroke,
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            _pdfInkPolyline.Points.Add(pos);
            WorkspacePdfMarkupCanvas.Children.Add(_pdfInkPolyline);
            Mouse.Capture(WorkspacePdfMarkupCanvas, CaptureMode.SubTree);
            return;
        }

        if (_pdfMarkupTool == DocumentMarkupTool.Erase)
        {
            e.Handled = true;
            if (TryErasePdfAt(pos, cw, ch))
                _pdfMarkupDirty = true;
            return;
        }

        if (_pdfMarkupTool is not (DocumentMarkupTool.Highlight or DocumentMarkupTool.Rectangle))
            return;

        e.Handled = true;
        _pdfDrawStart = pos;
        _pdfRubberBand = new WRectangle
        {
            Stroke = WBrushes.CornflowerBlue,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = WBrushes.Transparent,
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(_pdfRubberBand, pos.X);
        Canvas.SetTop(_pdfRubberBand, pos.Y);
        WorkspacePdfMarkupCanvas.Children.Add(_pdfRubberBand);
        Mouse.Capture(WorkspacePdfMarkupCanvas, CaptureMode.SubTree);
    }

    private bool TryErasePdfAt(WPoint pos, double cw, double ch)
    {
        var page = GetPdfPageIndex0();
        for (var i = _pdfMarkupModel.Count - 1; i >= 0; i--)
        {
            var m = _pdfMarkupModel[i];
            if (m.PreviewSurface != MarkupPreviewSurface.Pdf || m.PageIndex != page) continue;
            var x = m.NormX * cw;
            var y = m.NormY * ch;
            var w = m.NormW * cw;
            var h = m.NormH * ch;
            if (pos.X >= x && pos.X <= x + w && pos.Y >= y && pos.Y <= y + h)
            {
                _pdfMarkupModel.RemoveAt(i);
                RebuildPdfMarkupShapes();
                return true;
            }
        }
        return false;
    }

    private void WorkspacePdfMarkupCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pdfPanDragging && PdfPreviewViewer != null && WorkspacePdfMarkupCanvas != null)
        {
            e.Handled = true;
            var now = e.GetPosition(WorkspacePdfMarkupCanvas);
            var dx = _pdfPanLast.X - now.X;
            var dy = _pdfPanLast.Y - now.Y;
            _pdfPanLast = now;
            PdfiumViewerTools.Pan(PdfPreviewViewer, dx, dy);
            return;
        }

        if (_pdfInkPolyline != null && _pdfInkPoints != null && WorkspacePdfMarkupCanvas != null)
        {
            var p = e.GetPosition(WorkspacePdfMarkupCanvas);
            _pdfInkPoints.Add(p);
            _pdfInkPolyline.Points.Add(p);
            return;
        }

        if (_pdfRubberBand == null || _pdfDrawStart == null || WorkspacePdfMarkupCanvas == null) return;
        var pos = e.GetPosition(WorkspacePdfMarkupCanvas);
        var x = Math.Min(_pdfDrawStart.Value.X, pos.X);
        var y = Math.Min(_pdfDrawStart.Value.Y, pos.Y);
        var w = Math.Abs(pos.X - _pdfDrawStart.Value.X);
        var h = Math.Abs(pos.Y - _pdfDrawStart.Value.Y);
        Canvas.SetLeft(_pdfRubberBand, x);
        Canvas.SetTop(_pdfRubberBand, y);
        _pdfRubberBand.Width = Math.Max(1, w);
        _pdfRubberBand.Height = Math.Max(1, h);
    }

    private void WorkspacePdfMarkupCanvas_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pdfPanDragging)
        {
            e.Handled = true;
            _pdfPanDragging = false;
            Mouse.Capture(null);
            if (WorkspacePdfMarkupCanvas != null)
                WorkspacePdfMarkupCanvas.Cursor = null;
            return;
        }

        if (_pdfInkPolyline != null && _pdfInkPoints != null && WorkspacePdfMarkupCanvas != null)
        {
            e.Handled = true;
            Mouse.Capture(null);
            var inkCw = WorkspacePdfMarkupCanvas.ActualWidth;
            var inkCh = WorkspacePdfMarkupCanvas.ActualHeight;
            WorkspacePdfMarkupCanvas.Children.Remove(_pdfInkPolyline);
            _pdfInkPolyline = null;
            var pts = _pdfInkPoints;
            _pdfInkPoints = null;
            if (pts == null || inkCw <= 0 || inkCh <= 0 || pts.Count < 2)
                return;

            var minX = pts.Min(pt => pt.X);
            var minY = pts.Min(pt => pt.Y);
            var maxX = pts.Max(pt => pt.X);
            var maxY = pts.Max(pt => pt.Y);
            const double pad = 2;
            minX = Math.Max(0, minX - pad);
            minY = Math.Max(0, minY - pad);
            maxX = Math.Min(inkCw, maxX + pad);
            maxY = Math.Min(inkCh, maxY + pad);
            var inkNx = minX / inkCw;
            var inkNy = minY / inkCh;
            var inkNw = (maxX - minX) / inkCw;
            var inkNh = (maxY - minY) / inkCh;
            inkNx = Math.Clamp(inkNx, 0, 1);
            inkNy = Math.Clamp(inkNy, 0, 1);
            inkNw = Math.Clamp(inkNw, 0.001, 1 - inkNx);
            inkNh = Math.Clamp(inkNh, 0.001, 1 - inkNy);
            _pdfMarkupModel.Add(new MarkupRegion
            {
                Kind = MarkupKind.Markup,
                Color = MarkupPreviewShapeFactory.InkStrokeColor,
                NormX = inkNx,
                NormY = inkNy,
                NormW = inkNw,
                NormH = inkNh,
                Label = MarkupPreviewShapeFactory.SerializeInkPoints(pts, inkCw, inkCh),
                PageIndex = GetPdfPageIndex0(),
                PreviewSurface = MarkupPreviewSurface.Pdf,
                CreatedBy = GetMarkupActorLabel(),
                CreatedAt = DateTime.UtcNow.ToString("O")
            });
            _pdfMarkupDirty = true;
            RebuildPdfMarkupShapes();
            return;
        }

        if (_pdfRubberBand == null || _pdfDrawStart == null || WorkspacePdfMarkupCanvas == null)
        {
            Mouse.Capture(null);
            return;
        }
        e.Handled = true;
        Mouse.Capture(null);
        var cw = WorkspacePdfMarkupCanvas.ActualWidth;
        var ch = WorkspacePdfMarkupCanvas.ActualHeight;
        var left = Canvas.GetLeft(_pdfRubberBand);
        var top = Canvas.GetTop(_pdfRubberBand);
        var w = _pdfRubberBand.Width;
        var h = _pdfRubberBand.Height;
        WorkspacePdfMarkupCanvas.Children.Remove(_pdfRubberBand);
        _pdfRubberBand = null;
        _pdfDrawStart = null;
        if (cw <= 0 || ch <= 0 || w < 3 || h < 3) return;

        var nx = Math.Clamp(left / cw, 0, 1);
        var ny = Math.Clamp(top / ch, 0, 1);
        var nw = Math.Clamp(w / cw, 0, 1 - nx);
        var nh = Math.Clamp(h / ch, 0, 1 - ny);
        _pdfMarkupModel.Add(new MarkupRegion
        {
            Kind = _pdfMarkupTool == DocumentMarkupTool.Highlight ? MarkupKind.Highlight : MarkupKind.Rectangle,
            Color = _pdfMarkupTool == DocumentMarkupTool.Highlight ? HighlightFill : RectangleStroke,
            NormX = nx,
            NormY = ny,
            NormW = nw,
            NormH = nh,
            PageIndex = GetPdfPageIndex0(),
            PreviewSurface = MarkupPreviewSurface.Pdf,
            CreatedBy = GetMarkupActorLabel(),
            CreatedAt = DateTime.UtcNow.ToString("O")
        });
        _pdfMarkupDirty = true;
        RebuildPdfMarkupShapes();
    }

    private void OcrSelectionToNoteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_notesDocument == null || OcrPreviewText == null) return;
        var sel = OcrPreviewText.SelectedText?.Trim();
        if (string.IsNullOrEmpty(sel))
        {
            MessageBox.Show("Select text in the OCR preview first.", "Note from selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new NotesDialog(_notesDocument.Id, _notesDocument.Uuid, System.IO.Path.GetFileName(_notesDocument.FilePath ?? ""), sel);
        dlg.Owner = Window.GetWindow(this);
        dlg.ShowDialog();
    }
}
