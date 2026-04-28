using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using Microsoft.VisualBasic;
using WBrush = System.Windows.Media.Brush;
using WBrushes = System.Windows.Media.Brushes;
using WColor = System.Windows.Media.Color;
using WorkAudit.Core.TextExtraction;
using WorkAudit.Domain;
using WRectangle = System.Windows.Shapes.Rectangle;
using Point = System.Windows.Point;
using Cursors = System.Windows.Input.Cursors;

namespace WorkAudit.Controls;

public enum DocumentMarkupTool
{
    Pan,
    Highlight,
    Rectangle,
    Erase,
    /// <summary>Freehand pen markup.</summary>
    Markup,
    /// <summary>Click to place text.</summary>
    Text,
    /// <summary>View only: overlay does not capture the preview so scrollbars and wheel scrolling work.</summary>
    Browse
}

/// <summary>
/// Document viewer with zoom, pan, rotation for images, and optional markup overlay.
/// </summary>
public partial class DocumentViewer : UserControl
{
    private const double MinScale = 0.1;
    private const double MaxScale = 10.0;
    private const double ZoomStep = 1.25;
    /// <summary>Default zoom on open: fraction of native size (1.0 = 100% = actual pixels).</summary>
    private const double DefaultPreviewZoomPercent = 0.6;
    private const double PanStep = 64;
    private const string HighlightFill = "#80FFFF00";
    private const string RectangleStroke = "#CCFF0000";

    /// <summary>When true, use span-based <see cref="OcrSpanSelectionSurface"/>; when false, legacy per-line TextBox overlay.</summary>
    public static bool UseNewOcrSelectionLayer { get; set; } = true;

    private double _scale = 1.0;
    private double _rotationAngle;
    private bool _isPanning;
    private Point _panStart;
    private double _scrollStartH, _scrollStartV;

    private DocumentMarkupTool _markupTool = DocumentMarkupTool.Browse;
    private bool _markupReadOnly = true;
    private readonly List<MarkupRegion> _markupModel = new();
    private Point? _drawStart;
    private WRectangle? _rubberBand;
    private bool _markupDirty;
    private List<Point>? _inkPoints;
    private System.Windows.Shapes.Polyline? _inkPolyline;
    private bool _ocrTextSelectMode;
    private List<PreviewOcrRegion>? _ocrOverlayLineRegions;
    private OcrSelectableTextLayout? _ocrSelectableLayout;
    private OcrSpanSelectionSurface? _ocrSpanSurface;

    /// <summary>Fired after the image is rotated (workspace may disable on-image OCR).</summary>
    public event EventHandler? ImageRotationChanged;

    public DocumentViewer()
    {
        InitializeComponent();
    }

    /// <summary>When false, user can draw/erase markup (when tool is not Pan).</summary>
    public bool MarkupReadOnly
    {
        get => _markupReadOnly;
        set
        {
            _markupReadOnly = value;
            SyncMarkupAndOcrHitTest();
        }
    }

    public DocumentMarkupTool MarkupTool
    {
        get => _markupTool;
        set
        {
            _markupTool = value;
            SyncMarkupAndOcrHitTest();
        }
    }

    /// <summary>Whether Windows OCR text selection overlay is active (requires neutral rotation).</summary>
    public bool IsOcrTextSelectMode => _ocrTextSelectMode;

    /// <summary>Current clockwise rotation in degrees (0, 90, 180, 270).</summary>
    public double ImageRotationDegrees => _rotationAngle;

    /// <summary>True if markup changed since last save.</summary>
    public bool IsMarkupDirty => _markupDirty;

    public void MarkMarkupSaved() => _markupDirty = false;

    /// <summary>Replace image-surface markup and redraw.</summary>
    public void SetMarkupRegions(IReadOnlyList<MarkupRegion>? regions)
    {
        _markupModel.Clear();
        if (regions != null)
        {
            foreach (var r in regions.Where(x => x.PreviewSurface == MarkupPreviewSurface.Image))
                _markupModel.Add(CloneRegion(r));
        }
        _markupDirty = false;
        RebuildMarkupShapes();
    }

    /// <summary>Current markup for persistence (image surface).</summary>
    public IReadOnlyList<MarkupRegion> GetMarkupRegionsForSave()
    {
        return _markupModel
            .Where(m => m.PreviewSurface == MarkupPreviewSurface.Image)
            .Select(CloneRegion)
            .ToList();
    }

    public void ClearMarkup()
    {
        _markupModel.Clear();
        MarkupCanvas?.Children.Clear();
        _markupDirty = false;
    }

    public void SetSource(string? filePath)
    {
        if (DocumentImage == null) return;

        ClearMarkup();
        ClearOcrTextOverlay();

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            DocumentImage.Source = null;
            _scale = 1.0;
            _rotationAngle = 0;
            ApplyTransform();
            UpdateZoomText();
            SyncMarkupAndOcrHitTest();
            return;
        }

        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".pdf")
        {
            DocumentImage.Source = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            DocumentImage.Source = bitmap;
        }
        catch
        {
            DocumentImage.Source = null;
        }

        _rotationAngle = 0;
        void OnLayout(object? s, EventArgs _)
        {
            if (Scroll != null && Scroll.ViewportWidth > 0 && Scroll.ViewportHeight > 0)
            {
                LayoutUpdated -= OnLayout;
                FitToView();
            }
        }
        LayoutUpdated += OnLayout;
        SyncMarkupAndOcrHitTest();
    }

    public void FitToView()
    {
        if (DocumentImage?.Source == null || Scroll == null) return;

        var img = DocumentImage.Source as BitmapImage;
        if (img == null) return;

        var w = img.PixelWidth;
        var h = img.PixelHeight;
        if (w <= 0 || h <= 0) return;

        var viewW = Scroll.ViewportWidth;
        var viewH = Scroll.ViewportHeight;
        if (viewW <= 0 || viewH <= 0)
        {
            viewW = Scroll.ActualWidth > 0 ? Scroll.ActualWidth : 400;
            viewH = Scroll.ActualHeight > 0 ? Scroll.ActualHeight : 300;
        }

        // Default: 60% of native (1:1) size. Do not use fit*0.6 — that is ~10% when fit≈0.17.
        _scale = Math.Clamp(DefaultPreviewZoomPercent, MinScale, MaxScale);
        ApplyTransform();
        UpdateZoomText();
    }

    public void Clear()
    {
        SetSource(null);
    }

    private static MarkupRegion CloneRegion(MarkupRegion m) => new()
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

    private void DocumentImage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MarkupCanvas == null || DocumentImage == null) return;
        MarkupCanvas.Width = DocumentImage.ActualWidth;
        MarkupCanvas.Height = DocumentImage.ActualHeight;
        if (OcrOverlayCanvas != null)
        {
            OcrOverlayCanvas.Width = DocumentImage.ActualWidth;
            OcrOverlayCanvas.Height = DocumentImage.ActualHeight;
        }
        RebuildMarkupShapes();
        if (_ocrTextSelectMode && HasOcrOverlayContent())
        {
            if (UseNewOcrSelectionLayer && _ocrSpanSurface != null && OcrOverlayCanvas != null)
            {
                _ocrSpanSurface.Width = Math.Max(1, OcrOverlayCanvas.Width);
                _ocrSpanSurface.Height = Math.Max(1, OcrOverlayCanvas.Height);
                _ocrSpanSurface.RefreshGeometry();
            }
            else
                RebuildOcrOverlayVisuals();
        }
    }

    private void SyncMarkupAndOcrHitTest()
    {
        if (MarkupCanvas == null || OcrOverlayCanvas == null) return;
        var hasImage = DocumentImage?.Source != null;

        if (_ocrTextSelectMode)
        {
            MarkupCanvas.IsHitTestVisible = false;
            OcrOverlayCanvas.IsHitTestVisible = hasImage;
            OcrOverlayCanvas.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
            if (OcrCopyTextBtn != null) OcrCopyTextBtn.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
            if (OcrToolbarSeparator != null) OcrToolbarSeparator.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        OcrOverlayCanvas.IsHitTestVisible = false;
        OcrOverlayCanvas.Visibility = Visibility.Collapsed;
        if (OcrCopyTextBtn != null) OcrCopyTextBtn.Visibility = Visibility.Collapsed;
        if (OcrToolbarSeparator != null) OcrToolbarSeparator.Visibility = Visibility.Collapsed;
        // Pan and drawing tools need the canvas as hit target. Browse leaves the image/scroll area usable
        // (scrollbars, wheel scroll) without the overlay blocking input.
        MarkupCanvas.IsHitTestVisible = hasImage && !_markupReadOnly && _markupTool != DocumentMarkupTool.Browse;
    }

    /// <summary>Enables or disables the overlay layer; clearing regions when turning off.</summary>
    public void SetOcrTextSelectMode(bool enabled)
    {
        _ocrTextSelectMode = enabled;
        if (!enabled)
        {
            _ocrOverlayLineRegions = null;
            _ocrSelectableLayout = null;
            _ocrSpanSurface = null;
            OcrOverlayCanvas?.Children.Clear();
        }
        else
        {
            ReleaseScrollPanCapture();
        }

        SyncMarkupAndOcrHitTest();
    }

    /// <summary>
    /// Read-only scan regions: rounded light frosted rectangles; invisible overlay text with system selection.
    /// Regions are re-laid out when the image size or zoom changes.
    /// </summary>
    public void SetOcrTextOverlay(IReadOnlyList<PreviewOcrRegion>? regions)
    {
        if (regions == null || regions.Count == 0)
        {
            _ocrOverlayLineRegions = null;
            if (UseNewOcrSelectionLayer)
                _ocrSelectableLayout = null;
            OcrOverlayCanvas?.Children.Clear();
            _ocrSpanSurface = null;
            return;
        }

        _ocrOverlayLineRegions = new List<PreviewOcrRegion>(regions);
        if (UseNewOcrSelectionLayer)
            _ocrSelectableLayout = OcrSelectableTextLayoutBuilder.BuildFromPreviewOcrRegions(regions);
        RebuildOcrOverlayVisuals();
    }

    /// <summary>Word-level selectable layout (preferred when <see cref="UseNewOcrSelectionLayer"/> is true).</summary>
    public void SetOcrSelectableLayout(OcrSelectableTextLayout? layout)
    {
        _ocrSelectableLayout = layout;
        RebuildOcrOverlayVisuals();
    }

    private void ReleaseScrollPanCapture()
    {
        _isPanning = false;
        try
        {
            Mouse.Capture(null);
        }
        catch
        {
            // ignore
        }

        if (Scroll != null)
            Scroll.Cursor = null;
    }

    private bool HasOcrOverlayContent() =>
        UseNewOcrSelectionLayer
            ? _ocrSelectableLayout is { Spans.Count: > 0 }
            : _ocrOverlayLineRegions is { Count: > 0 };

    private void RebuildOcrOverlayVisuals()
    {
        if (OcrOverlayCanvas == null || !_ocrTextSelectMode)
            return;

        OcrOverlayCanvas.Children.Clear();
        _ocrSpanSurface = null;

        if (UseNewOcrSelectionLayer)
        {
            if (_ocrSelectableLayout == null || _ocrSelectableLayout.Spans.Count == 0)
                return;

            var surface = new OcrSpanSelectionSurface();
            surface.SetLayout(_ocrSelectableLayout, GetOcrLayoutScale);
            surface.Width = Math.Max(1, OcrOverlayCanvas.Width);
            surface.Height = Math.Max(1, OcrOverlayCanvas.Height);
            Canvas.SetLeft(surface, 0);
            Canvas.SetTop(surface, 0);
            OcrOverlayCanvas.Children.Add(surface);
            _ocrSpanSurface = surface;
            return;
        }

        if (_ocrOverlayLineRegions == null || _ocrOverlayLineRegions.Count == 0)
            return;

        var scale = GetOcrLayoutScale();
        var highlight = System.Windows.SystemColors.HighlightBrush;
        var highlightText = System.Windows.SystemColors.HighlightTextBrush;
        var hitTestFg = new SolidColorBrush(WColor.FromArgb(1, 0, 0, 0));
        var ocrTint = new SolidColorBrush(WColor.FromArgb(48, 245, 245, 248));
        var ocrBorder = new SolidColorBrush(WColor.FromArgb(50, 175, 180, 190));

        foreach (var line in _ocrOverlayLineRegions)
        {
            var text = line.Text ?? "";
            if (string.IsNullOrWhiteSpace(text) || line.Width < 1 || line.Height < 1)
                continue;

            var x = line.X * scale.sx;
            var y = line.Y * scale.sy;
            var w = Math.Max(10, line.Width * scale.sx);
            var h = Math.Max(8, line.Height * scale.sy);
            var fontSize = Math.Clamp(h * 0.7, 8, 96);
            var arabic = OcrScriptHeuristics.IsPrimarilyArabicScript(text);

            var bgRect = new WRectangle
            {
                Width = w,
                Height = h,
                Fill = ocrTint,
                Stroke = ocrBorder,
                StrokeThickness = 1,
                RadiusX = 3,
                RadiusY = 3,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            };

            var tb = new System.Windows.Controls.TextBox
            {
                Text = text,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = WBrushes.Transparent,
                Foreground = hitTestFg,
                CaretBrush = WBrushes.Transparent,
                SelectionBrush = highlight,
                SelectionTextBrush = highlightText,
                TextWrapping = TextWrapping.NoWrap,
                AcceptsReturn = false,
                Padding = new Thickness(4, 2, 4, 2),
                FontSize = fontSize,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Traditional Arabic, Tahoma, Arial Unicode MS"),
                FlowDirection = arabic ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight,
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                Width = w,
                Height = h,
                ContextMenu = null,
                Cursor = Cursors.IBeam,
                FocusVisualStyle = null,
                Focusable = true,
                IsUndoEnabled = false,
                IsHitTestVisible = true,
                IsTabStop = true
            };

            if (arabic)
            {
                try
                {
                    tb.Language = XmlLanguage.GetLanguage("ar-SA");
                    tb.FontFamily = new System.Windows.Media.FontFamily("Traditional Arabic, Segoe UI, Tahoma");
                }
                catch { }
            }

            SpellCheck.SetIsEnabled(tb, false);
            TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(tb, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(tb, TextHintingMode.Fixed);

            Canvas.SetLeft(bgRect, x);
            Canvas.SetTop(bgRect, y);
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);

            OcrOverlayCanvas.Children.Add(bgRect);
            OcrOverlayCanvas.Children.Add(tb);
        }
    }

    private void OcrCopyTextBtn_Click(object sender, RoutedEventArgs e)
    {
        if (OcrOverlayCanvas == null) return;

        if (UseNewOcrSelectionLayer && _ocrSpanSurface != null && _ocrSelectableLayout != null)
        {
            var formatted = _ocrSpanSurface.HasSelection
                ? _ocrSpanSurface.GetSelectedTextFormatted()
                : OcrCopyFormatter.FormatCopy(_ocrSelectableLayout.FullText);
            if (!string.IsNullOrEmpty(formatted))
            {
                try
                {
                    System.Windows.Clipboard.SetText(formatted);
                }
                catch
                {
                    // ignore clipboard failures
                }

                return;
            }
        }

        System.Windows.Controls.TextBox? tb = null;
        foreach (var t in OcrOverlayCanvas.Children.OfType<System.Windows.Controls.TextBox>())
        {
            if (t.IsFocused || t.IsKeyboardFocusWithin)
            {
                tb = t;
                break;
            }
        }

        if (tb == null)
            tb = OcrOverlayCanvas.Children.OfType<System.Windows.Controls.TextBox>().FirstOrDefault();

        if (tb == null) return;
        var text = tb.SelectionLength > 0 ? tb.SelectedText : tb.Text;
        text = text?.Trim() ?? "";
        if (text.Length == 0) return;
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch
        {
            // ignore clipboard failures
        }
    }

    private (double sx, double sy) GetOcrLayoutScale()
    {
        if (DocumentImage?.Source is not BitmapSource bmp || bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0)
            return (1, 1);
        var aw = DocumentImage.ActualWidth;
        var ah = DocumentImage.ActualHeight;
        if (aw <= 0 || ah <= 0)
            return (1, 1);
        return (aw / bmp.PixelWidth, ah / bmp.PixelHeight);
    }

    /// <summary>Merges OCR lines into paragraph-style blocks per column.</summary>
    /// <summary>Clears overlay regions and turns off selection mode.</summary>
    public void ClearOcrTextOverlay()
    {
        _ocrTextSelectMode = false;
        _ocrOverlayLineRegions = null;
        _ocrSelectableLayout = null;
        _ocrSpanSurface = null;
        ReleaseScrollPanCapture();
        OcrOverlayCanvas?.Children.Clear();
        SyncMarkupAndOcrHitTest();
    }

    private void RebuildMarkupShapes()
    {
        if (MarkupCanvas == null) return;
        MarkupCanvas.Children.Clear();
        var cw = MarkupCanvas.ActualWidth;
        var ch = MarkupCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return;

        foreach (var m in _markupModel.Where(x => x.PreviewSurface == MarkupPreviewSurface.Image))
            MarkupCanvas.Children.Add(MarkupPreviewShapeFactory.Create(m, cw, ch));
    }

    private void ApplyTransform()
    {
        if (ScaleTransform == null || RotateTransform == null) return;
        ScaleTransform.ScaleX = _scale;
        ScaleTransform.ScaleY = _scale;
        RotateTransform.Angle = _rotationAngle;
        if (_ocrTextSelectMode && HasOcrOverlayContent())
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (UseNewOcrSelectionLayer && _ocrSpanSurface != null && OcrOverlayCanvas != null)
                {
                    _ocrSpanSurface.Width = Math.Max(1, OcrOverlayCanvas.Width);
                    _ocrSpanSurface.Height = Math.Max(1, OcrOverlayCanvas.Height);
                    _ocrSpanSurface.RefreshGeometry();
                }
                else
                    RebuildOcrOverlayVisuals();
            }), DispatcherPriority.Background);
        }
    }

    private void UpdateZoomText()
    {
        if (ZoomText != null)
            ZoomText.Text = $"{_scale * 100:F0}%";
    }

    private void Zoom(double factor, Point? center = null)
    {
        var newScale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        if (newScale == _scale) return;

        if (center.HasValue && Scroll != null && ContentHost != null)
        {
            var pos = center.Value;
            var relX = (Scroll.HorizontalOffset + pos.X) / _scale;
            var relY = (Scroll.VerticalOffset + pos.Y) / _scale;
            _scale = newScale;
            ApplyTransform();
            UpdateZoomText();
            Scroll.ScrollToHorizontalOffset(Math.Max(0, relX * _scale - pos.X));
            Scroll.ScrollToVerticalOffset(Math.Max(0, relY * _scale - pos.Y));
        }
        else
        {
            _scale = newScale;
            ApplyTransform();
            UpdateZoomText();
        }
        Dispatcher.BeginInvoke(RebuildMarkupShapes);
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Zoom(ZoomStep);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Zoom(1.0 / ZoomStep);

    private void PanLeft_Click(object sender, RoutedEventArgs e) => PanScroll(-PanStep, 0);
    private void PanRight_Click(object sender, RoutedEventArgs e) => PanScroll(PanStep, 0);
    private void PanUp_Click(object sender, RoutedEventArgs e) => PanScroll(0, -PanStep);
    private void PanDown_Click(object sender, RoutedEventArgs e) => PanScroll(0, PanStep);

    private void PanScroll(double dx, double dy)
    {
        if (Scroll == null || DocumentImage?.Source == null) return;
        Scroll.ScrollToHorizontalOffset(Math.Max(0, Scroll.HorizontalOffset + dx));
        Scroll.ScrollToVerticalOffset(Math.Max(0, Scroll.VerticalOffset + dy));
    }

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentImage?.Source == null || Scroll == null) return;

        var img = DocumentImage.Source as BitmapImage;
        if (img == null) return;

        var w = img.PixelWidth;
        var h = img.PixelHeight;
        if (w <= 0 || h <= 0) return;

        var viewW = Scroll.ViewportWidth;
        var viewH = Scroll.ViewportHeight;
        if (viewW <= 0 || viewH <= 0) return;

        var scaleW = viewW / w;
        var scaleH = viewH / h;
        var fit = Math.Min(scaleW, scaleH);
        _scale = Math.Clamp(fit, MinScale, MaxScale);
        ApplyTransform();
        UpdateZoomText();
        Dispatcher.BeginInvoke(RebuildMarkupShapes);
    }

    private void ActualSize_Click(object sender, RoutedEventArgs e)
    {
        _scale = 1.0;
        ApplyTransform();
        UpdateZoomText();
        Dispatcher.BeginInvoke(RebuildMarkupShapes);
    }

    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        ClearMarkup();
        ClearOcrTextOverlay();
        _rotationAngle = (_rotationAngle + 90) % 360;
        ApplyTransform();
        ImageRotationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Scroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DocumentImage?.Source == null) return;
        // Browse: wheel scrolls the document; hold Ctrl to zoom (same idea as PDF preview).
        if (_markupTool == DocumentMarkupTool.Browse &&
            (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            return;
        e.Handled = true;
        var delta = e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep;
        var pos = e.GetPosition(Scroll);
        Zoom(delta, pos);
    }

    private void Scroll_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Scroll == null) return;
        // OCR text selection uses drag; do not capture the scroll viewer for panning.
        if (_ocrTextSelectMode)
            return;
        if (!_markupReadOnly && _markupTool is not DocumentMarkupTool.Pan) return;
        _isPanning = true;
        _panStart = e.GetPosition(this);
        _scrollStartH = Scroll.HorizontalOffset;
        _scrollStartV = Scroll.VerticalOffset;
        Mouse.Capture(Scroll, CaptureMode.SubTree);
        Scroll.Cursor = Cursors.Hand;
    }

    private void Scroll_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        Mouse.Capture(null);
        if (Scroll != null) Scroll.Cursor = null;
    }

    private void Scroll_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || Scroll == null) return;
        var now = e.GetPosition(this);
        var dx = _panStart.X - now.X;
        var dy = _panStart.Y - now.Y;
        Scroll.ScrollToHorizontalOffset(_scrollStartH + dx);
        Scroll.ScrollToVerticalOffset(_scrollStartV + dy);
        _panStart = now;
        _scrollStartH = Scroll.HorizontalOffset;
        _scrollStartV = Scroll.VerticalOffset;
    }

    private void MarkupCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_markupReadOnly || MarkupCanvas == null || DocumentImage?.Source == null) return;
        var pos = e.GetPosition(MarkupCanvas);
        var cw = MarkupCanvas.ActualWidth;
        var ch = MarkupCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return;

        if (_markupTool == DocumentMarkupTool.Pan)
        {
            e.Handled = true;
            if (Scroll == null) return;
            _isPanning = true;
            _panStart = e.GetPosition(this);
            _scrollStartH = Scroll.HorizontalOffset;
            _scrollStartV = Scroll.VerticalOffset;
            Mouse.Capture(Scroll, CaptureMode.SubTree);
            Scroll.Cursor = Cursors.Hand;
            return;
        }

        if (_markupTool == DocumentMarkupTool.Text)
        {
            e.Handled = true;
            var text = Interaction.InputBox("Enter annotation text:", "Text", "", -1, -1);
            if (string.IsNullOrWhiteSpace(text)) return;
            const double tw = 0.28;
            const double th = 0.06;
            var nx = Math.Clamp(pos.X / cw - 0.02, 0, 1 - tw);
            var ny = Math.Clamp(pos.Y / ch - 0.02, 0, 1 - th);
            _markupModel.Add(new MarkupRegion
            {
                Kind = MarkupKind.Text,
                Color = MarkupPreviewShapeFactory.TextForegroundColor,
                NormX = nx,
                NormY = ny,
                NormW = tw,
                NormH = th,
                Label = text.Trim(),
                PageIndex = 0,
                PreviewSurface = MarkupPreviewSurface.Image
            });
            _markupDirty = true;
            RebuildMarkupShapes();
            return;
        }

        if (_markupTool == DocumentMarkupTool.Markup)
        {
            e.Handled = true;
            _inkPoints = new List<Point> { pos };
            var stroke = (WBrush)new BrushConverter().ConvertFromString(MarkupPreviewShapeFactory.InkStrokeColor)!;
            _inkPolyline = new System.Windows.Shapes.Polyline
            {
                Stroke = stroke,
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            _inkPolyline.Points.Add(pos);
            MarkupCanvas.Children.Add(_inkPolyline);
            Mouse.Capture(MarkupCanvas, CaptureMode.SubTree);
            return;
        }

        if (_markupTool == DocumentMarkupTool.Erase)
        {
            e.Handled = true;
            if (TryEraseAt(pos, cw, ch))
                _markupDirty = true;
            return;
        }

        if (_markupTool is not (DocumentMarkupTool.Highlight or DocumentMarkupTool.Rectangle))
            return;

        e.Handled = true;
        _drawStart = pos;
        _rubberBand = new WRectangle
        {
            Stroke = WBrushes.CornflowerBlue,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = WBrushes.Transparent,
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(_rubberBand, pos.X);
        Canvas.SetTop(_rubberBand, pos.Y);
        MarkupCanvas.Children.Add(_rubberBand);
        Mouse.Capture(MarkupCanvas, CaptureMode.SubTree);
    }

    private bool TryEraseAt(Point pos, double cw, double ch)
    {
        for (var i = _markupModel.Count - 1; i >= 0; i--)
        {
            var m = _markupModel[i];
            if (m.PreviewSurface != MarkupPreviewSurface.Image) continue;
            var x = m.NormX * cw;
            var y = m.NormY * ch;
            var w = m.NormW * cw;
            var h = m.NormH * ch;
            if (pos.X >= x && pos.X <= x + w && pos.Y >= y && pos.Y <= y + h)
            {
                _markupModel.RemoveAt(i);
                RebuildMarkupShapes();
                return true;
            }
        }
        return false;
    }

    private void MarkupCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_inkPolyline != null && _inkPoints != null && MarkupCanvas != null)
        {
            var p = e.GetPosition(MarkupCanvas);
            _inkPoints.Add(p);
            _inkPolyline.Points.Add(p);
            return;
        }

        if (_rubberBand == null || _drawStart == null || MarkupCanvas == null) return;
        var pos = e.GetPosition(MarkupCanvas);
        var x = Math.Min(_drawStart.Value.X, pos.X);
        var y = Math.Min(_drawStart.Value.Y, pos.Y);
        var w = Math.Abs(pos.X - _drawStart.Value.X);
        var h = Math.Abs(pos.Y - _drawStart.Value.Y);
        Canvas.SetLeft(_rubberBand, x);
        Canvas.SetTop(_rubberBand, y);
        _rubberBand.Width = Math.Max(1, w);
        _rubberBand.Height = Math.Max(1, h);
    }

    private void MarkupCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning && Scroll != null)
        {
            // Pan uses Scroll capture; Scroll_MouseLeftButtonUp ends the gesture. Do not release capture here.
            return;
        }

        if (_inkPolyline != null && _inkPoints != null && MarkupCanvas != null)
        {
            e.Handled = true;
            Mouse.Capture(null);
            var inkCw = MarkupCanvas.ActualWidth;
            var inkCh = MarkupCanvas.ActualHeight;
            MarkupCanvas.Children.Remove(_inkPolyline);
            _inkPolyline = null;
            var pts = _inkPoints;
            _inkPoints = null;
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
            _markupModel.Add(new MarkupRegion
            {
                Kind = MarkupKind.Markup,
                Color = MarkupPreviewShapeFactory.InkStrokeColor,
                NormX = inkNx,
                NormY = inkNy,
                NormW = inkNw,
                NormH = inkNh,
                Label = MarkupPreviewShapeFactory.SerializeInkPoints(pts, inkCw, inkCh),
                PageIndex = 0,
                PreviewSurface = MarkupPreviewSurface.Image
            });
            _markupDirty = true;
            RebuildMarkupShapes();
            return;
        }

        if (_rubberBand == null || _drawStart == null || MarkupCanvas == null)
        {
            Mouse.Capture(null);
            return;
        }

        e.Handled = true;
        Mouse.Capture(null);

        var cw = MarkupCanvas.ActualWidth;
        var ch = MarkupCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0)
        {
            MarkupCanvas.Children.Remove(_rubberBand);
            _rubberBand = null;
            _drawStart = null;
            return;
        }

        var left = Canvas.GetLeft(_rubberBand);
        var top = Canvas.GetTop(_rubberBand);
        var w = _rubberBand.Width;
        var h = _rubberBand.Height;
        MarkupCanvas.Children.Remove(_rubberBand);
        _rubberBand = null;
        _drawStart = null;

        if (w < 3 || h < 3) return;

        var nx = left / cw;
        var ny = top / ch;
        var nw = w / cw;
        var nh = h / ch;
        nx = Math.Clamp(nx, 0, 1);
        ny = Math.Clamp(ny, 0, 1);
        nw = Math.Clamp(nw, 0, 1 - nx);
        nh = Math.Clamp(nh, 0, 1 - ny);

        var region = new MarkupRegion
        {
            Kind = _markupTool == DocumentMarkupTool.Highlight ? MarkupKind.Highlight : MarkupKind.Rectangle,
            Color = _markupTool == DocumentMarkupTool.Highlight ? HighlightFill : RectangleStroke,
            NormX = nx,
            NormY = ny,
            NormW = nw,
            NormH = nh,
            PageIndex = 0,
            PreviewSurface = MarkupPreviewSurface.Image
        };
        _markupModel.Add(region);
        _markupDirty = true;
        RebuildMarkupShapes();
    }
}
