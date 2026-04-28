using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdfiumViewer;

namespace WorkAudit.Core.Helpers;

/// <summary>Zoom and pan helpers for <see cref="PDFViewer"/> (PdfiumViewer.Net.WPF).</summary>
public static class PdfiumViewerTools
{
    /// <summary>Default preview zoom when opening a PDF (1.0 = 100%).</summary>
    public const double DefaultDocumentZoom = 0.6;

    private const double ZoomStep = 1.25;
    private const double PanPixels = 64;
    private static readonly FieldInfo? PdfViewerScrollField =
        typeof(PDFViewer).GetField("_scroll", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void ZoomIn(PDFViewer? viewer)
    {
        if (viewer == null) return;
        viewer.FitWidth = false;
        var z = viewer.Zoom * ZoomStep;
        viewer.Zoom = Math.Min(z, viewer.ZoomMax);
    }

    public static void ZoomOut(PDFViewer? viewer)
    {
        if (viewer == null) return;
        viewer.FitWidth = false;
        var z = viewer.Zoom / ZoomStep;
        viewer.Zoom = Math.Max(z, viewer.ZoomMin);
    }

    public static void FitWidth(PDFViewer? viewer)
    {
        if (viewer == null) return;
        viewer.FitWidth = true;
    }

    /// <summary>Apply default zoom (e.g. 60%) after a document is loaded. Turns off fit-width.</summary>
    public static void ApplyDefaultDocumentZoom(PDFViewer? viewer)
    {
        if (viewer == null) return;
        viewer.FitWidth = false;
        var z = DefaultDocumentZoom;
        viewer.Zoom = Math.Clamp(z, viewer.ZoomMin, viewer.ZoomMax);
    }

    public static void Pan(PDFViewer? viewer, double deltaX, double deltaY)
    {
        if (viewer == null) return;
        var scroll = GetPdfViewerScrollViewer(viewer);
        if (scroll == null) return;
        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + deltaX);
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + deltaY);
    }

    /// <summary>
    /// PDFViewer keeps its template <see cref="ScrollViewer"/> in a private <c>_scroll</c> field.
    /// Visual-tree search alone often misses it; reflection matches the library implementation.
    /// </summary>
    private static ScrollViewer? GetPdfViewerScrollViewer(PDFViewer viewer)
    {
        if (PdfViewerScrollField?.GetValue(viewer) is ScrollViewer fromField)
            return fromField;
        return FindDescendant<ScrollViewer>(viewer);
    }

    public static void PanLeft(PDFViewer? viewer) => Pan(viewer, -PanPixels, 0);
    public static void PanRight(PDFViewer? viewer) => Pan(viewer, PanPixels, 0);
    public static void PanUp(PDFViewer? viewer) => Pan(viewer, 0, -PanPixels);
    public static void PanDown(PDFViewer? viewer) => Pan(viewer, 0, PanPixels);

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;
            var nested = FindDescendant<T>(child);
            if (nested != null)
                return nested;
        }
        return null;
    }
}
