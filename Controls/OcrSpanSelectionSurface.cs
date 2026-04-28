using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WorkAudit.Core.TextExtraction;
using MediaBrush = System.Windows.Media.Brush;
using MediaPen = System.Windows.Media.Pen;
using WPoint = System.Windows.Point;
using WColor = System.Windows.Media.Color;

namespace WorkAudit.Controls;

/// <summary>
/// Span-based OCR selection: hit-test in image space, merged per-line highlight, index range selection.
/// </summary>
public sealed class OcrSpanSelectionSurface : Canvas
{
    private const double HitInflateImagePx = 6;

    private static readonly MediaBrush TintBrush = CreateTintBrush();
    private static readonly MediaBrush SelectionBrush = CreateSelectionBrush();
    private static readonly MediaPen BorderPen = CreateBorderPen();

    private OcrSelectableTextLayout? _layout;
    private Func<(double sx, double sy)> _getScale = static () => (1, 1);

    private Rect[] _viewRects = Array.Empty<Rect>();
    private Rect[] _hitImageRects = Array.Empty<Rect>();

    private int _anchorSpan = -1;
    private int _activeSpan = -1;
    private bool _dragging;

    public OcrSpanSelectionSurface()
    {
        Focusable = true;
        Cursor = System.Windows.Input.Cursors.IBeam;
        SnapsToDevicePixels = true;
        FocusVisualStyle = null;
        Background = System.Windows.Media.Brushes.Transparent;
    }

    public bool HasSelection => _layout != null && _anchorSpan >= 0 && _activeSpan >= 0;

    public void SetLayout(OcrSelectableTextLayout? layout, Func<(double sx, double sy)> getScale)
    {
        _layout = layout;
        _getScale = getScale ?? (static () => (1, 1));
        _anchorSpan = -1;
        _activeSpan = -1;
        _dragging = false;
        RefreshGeometry();
    }

    /// <summary>Rebuild cached rects when image view size or scale changed.</summary>
    public void RefreshGeometry()
    {
        RebuildGeometryCache();
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        _anchorSpan = -1;
        _activeSpan = -1;
        _dragging = false;
        InvalidateVisual();
    }

    public void SelectAll()
    {
        if (_layout == null || _layout.Spans.Count == 0) return;
        _anchorSpan = 0;
        _activeSpan = _layout.Spans.Count - 1;
        InvalidateVisual();
    }

    public string? GetSelectedTextFormatted()
    {
        if (_layout == null) return null;
        var range = GetSelectionSpanIndices();
        if (range == null) return null;
        var (lo, hi) = range.Value;
        var raw = _layout.GetTextForSpanRange(lo, hi);
        return OcrCopyFormatter.FormatCopy(raw);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_layout == null || _layout.Spans.Count == 0) return;
        Focus();
        var idx = HitTest(e.GetPosition(this));
        if (!idx.HasValue) return;
        _anchorSpan = idx.Value;
        _activeSpan = idx.Value;
        _dragging = true;
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || _layout == null) return;
        var idx = HitTest(e.GetPosition(this));
        if (idx.HasValue && idx.Value != _activeSpan)
        {
            _activeSpan = idx.Value;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _dragging = false;
        base.OnLostMouseCapture(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_layout == null || _layout.Spans.Count == 0) return;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
        {
            SelectAll();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            var t = GetSelectedTextFormatted();
            if (!string.IsNullOrEmpty(t))
            {
                try { System.Windows.Clipboard.SetText(t); } catch { /* ignore */ }
            }

            e.Handled = true;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_layout == null || _viewRects.Length == 0) return;

        for (var i = 0; i < _viewRects.Length; i++)
        {
            dc.DrawRoundedRectangle(TintBrush, BorderPen, _viewRects[i], 3, 3);
        }

        var range = GetSelectionSpanIndices();
        if (range == null) return;
        var (lo, hi) = range.Value;
        DrawMergedSelection(dc, lo, hi);
    }

    private void DrawMergedSelection(DrawingContext dc, int lo, int hi)
    {
        var byLine = new Dictionary<int, List<int>>();
        for (var i = lo; i <= hi; i++)
        {
            var s = _layout!.Spans[i];
            if (!byLine.TryGetValue(s.LineIndex, out var list))
            {
                list = new List<int>();
                byLine[s.LineIndex] = list;
            }

            list.Add(i);
        }

        foreach (var list in byLine.Values)
        {
            list.Sort();
            var runStart = list[0];
            for (var k = 0; k < list.Count; k++)
            {
                var atEnd = k == list.Count - 1;
                var contiguousNext = !atEnd && list[k + 1] == list[k] + 1;
                if (atEnd || !contiguousNext)
                {
                    var runEnd = list[k];
                    UnionViewRects(runStart, runEnd, out var rect);
                    dc.DrawRectangle(SelectionBrush, null, rect);
                    if (!atEnd)
                        runStart = list[k + 1];
                }
            }
        }
    }

    private void UnionViewRects(int from, int to, out Rect union)
    {
        union = _viewRects[from];
        for (var i = from + 1; i <= to; i++)
            union = Rect.Union(union, _viewRects[i]);
    }

    private void RebuildGeometryCache()
    {
        if (_layout == null || _layout.Spans.Count == 0)
        {
            _viewRects = Array.Empty<Rect>();
            _hitImageRects = Array.Empty<Rect>();
            return;
        }

        var (sx, sy) = _getScale();
        var spans = _layout.Spans;
        var n = spans.Count;
        _viewRects = new Rect[n];
        _hitImageRects = new Rect[n];
        for (var i = 0; i < n; i++)
        {
            var s = spans[i];
            _viewRects[i] = new Rect(s.X * sx, s.Y * sy, Math.Max(1, s.Width * sx), Math.Max(1, s.Height * sy));
            _hitImageRects[i] = InflateImageRect(s.X, s.Y, s.Width, s.Height, HitInflateImagePx);
        }
    }

    private static Rect InflateImageRect(double x, double y, double w, double h, double pad) =>
        new(x - pad, y - pad, w + 2 * pad, h + 2 * pad);

    private WPoint ViewToImage(WPoint viewPt)
    {
        var (sx, sy) = _getScale();
        if (sx < 1e-9) sx = 1;
        if (sy < 1e-9) sy = 1;
        return new WPoint(viewPt.X / sx, viewPt.Y / sy);
    }

    private int? HitTest(WPoint viewPt)
    {
        if (_layout == null || _hitImageRects.Length == 0) return null;
        var img = ViewToImage(viewPt);
        for (var i = 0; i < _hitImageRects.Length; i++)
        {
            if (_hitImageRects[i].Contains(img))
                return i;
        }

        return NearestSpanOnClosestLine(img);
    }

    /// <summary>Pick nearest span center only on the line whose vertical band is closest to the point.</summary>
    private int? NearestSpanOnClosestLine(WPoint imgPt)
    {
        if (_layout == null) return null;
        var lineIds = _layout.Spans.Select(s => s.LineIndex).Distinct().ToList();
        if (lineIds.Count == 0) return null;

        var bestLine = lineIds[0];
        var bestVertDist = double.MaxValue;
        foreach (var li in lineIds)
        {
            var onLine = _layout.Spans.Where(s => s.LineIndex == li).ToList();
            if (onLine.Count == 0) continue;
            var minY = onLine.Min(s => s.Y);
            var maxY = onLine.Max(s => s.Y + s.Height);
            double vert;
            if (imgPt.Y < minY) vert = minY - imgPt.Y;
            else if (imgPt.Y > maxY) vert = imgPt.Y - maxY;
            else vert = 0;
            if (vert < bestVertDist)
            {
                bestVertDist = vert;
                bestLine = li;
            }
        }

        var candidates = _layout.Spans
            .Select((s, i) => (s, i))
            .Where(x => x.s.LineIndex == bestLine)
            .ToList();
        if (candidates.Count == 0) return null;

        var bestIdx = candidates[0].i;
        var bestD = double.MaxValue;
        foreach (var (s, i) in candidates)
        {
            var dx = imgPt.X - s.CenterX;
            var dy = imgPt.Y - s.CenterY;
            var d = dx * dx + dy * dy;
            if (d < bestD)
            {
                bestD = d;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    private (int lo, int hi)? GetSelectionSpanIndices()
    {
        if (_layout == null || _anchorSpan < 0 || _activeSpan < 0) return null;
        var lo = Math.Min(_anchorSpan, _activeSpan);
        var hi = Math.Max(_anchorSpan, _activeSpan);
        lo = Math.Clamp(lo, 0, _layout.Spans.Count - 1);
        hi = Math.Clamp(hi, 0, _layout.Spans.Count - 1);
        return (lo, hi);
    }

    private static SolidColorBrush CreateTintBrush()
    {
        var b = new SolidColorBrush(WColor.FromArgb(48, 245, 245, 248));
        b.Freeze();
        return b;
    }

    private static SolidColorBrush CreateSelectionBrush()
    {
        var hb = System.Windows.SystemColors.HighlightBrush;
        if (hb is SolidColorBrush scb && scb.CanFreeze)
        {
            var c = scb.Color;
            var copy = new SolidColorBrush(WColor.FromArgb(140, c.R, c.G, c.B));
            copy.Freeze();
            return copy;
        }

        var fallback = new SolidColorBrush(WColor.FromArgb(140, 0, 120, 215));
        fallback.Freeze();
        return fallback;
    }

    private static MediaPen CreateBorderPen()
    {
        var p = new MediaPen(new SolidColorBrush(WColor.FromArgb(50, 175, 180, 190)), 1);
        if (p.Brush.CanFreeze) p.Brush.Freeze();
        p.Freeze();
        return p;
    }
}
