using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PdfiumViewer;
using WorkAudit.Core.Helpers;

namespace WorkAudit.Controls;

public partial class PdfPreviewToolsPanel : UserControl
{
    private PDFViewer? _viewer;
    private bool _dragPanActive;
    private System.Windows.Point _dragPanLast;

    public PdfPreviewToolsPanel()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (PdfPanDragBtn?.IsChecked == true)
                SubscribeViewerEvents();
        };
    }

    public void SetTargetViewer(PDFViewer? viewer)
    {
        UnsubscribeViewerEvents();
        EndDragPan();
        _viewer = viewer;
        UpdateZoomLabel();
        if (PdfPanDragBtn?.IsChecked == true)
            SubscribeViewerEvents();
    }

    private void UpdateZoomLabel()
    {
        if (PdfZoomLabel == null) return;
        if (_viewer == null)
        {
            PdfZoomLabel.Text = "PDF";
            return;
        }
        PdfZoomLabel.Text = $"{_viewer.Zoom * 100:F0}%";
    }

    private void PdfZoomOut_Click(object sender, RoutedEventArgs e)
    {
        PdfiumViewerTools.ZoomOut(_viewer);
        UpdateZoomLabel();
    }

    private void PdfZoomIn_Click(object sender, RoutedEventArgs e)
    {
        PdfiumViewerTools.ZoomIn(_viewer);
        UpdateZoomLabel();
    }

    private void PdfFitWidth_Click(object sender, RoutedEventArgs e)
    {
        PdfiumViewerTools.FitWidth(_viewer);
        UpdateZoomLabel();
    }

    private void PdfPanLeft_Click(object sender, RoutedEventArgs e) => PdfiumViewerTools.PanLeft(_viewer);
    private void PdfPanRight_Click(object sender, RoutedEventArgs e) => PdfiumViewerTools.PanRight(_viewer);
    private void PdfPanUp_Click(object sender, RoutedEventArgs e) => PdfiumViewerTools.PanUp(_viewer);
    private void PdfPanDown_Click(object sender, RoutedEventArgs e) => PdfiumViewerTools.PanDown(_viewer);

    private void PdfPanDrag_Checked(object sender, RoutedEventArgs e) => SubscribeViewerEvents();

    private void PdfPanDrag_Unchecked(object sender, RoutedEventArgs e)
    {
        UnsubscribeViewerEvents();
        EndDragPan();
    }

    private void SubscribeViewerEvents()
    {
        if (_viewer == null || PdfPanDragBtn?.IsChecked != true) return;
        UnsubscribeViewerEvents();
        _viewer.PreviewMouseLeftButtonDown += Viewer_PreviewMouseLeftButtonDown;
        _viewer.PreviewMouseMove += Viewer_PreviewMouseMove;
        _viewer.PreviewMouseLeftButtonUp += Viewer_PreviewMouseLeftButtonUp;
        _viewer.LostMouseCapture += Viewer_LostMouseCapture;
    }

    private void UnsubscribeViewerEvents()
    {
        if (_viewer == null) return;
        _viewer.PreviewMouseLeftButtonDown -= Viewer_PreviewMouseLeftButtonDown;
        _viewer.PreviewMouseMove -= Viewer_PreviewMouseMove;
        _viewer.PreviewMouseLeftButtonUp -= Viewer_PreviewMouseLeftButtonUp;
        _viewer.LostMouseCapture -= Viewer_LostMouseCapture;
    }

    private void Viewer_LostMouseCapture(object sender, MouseEventArgs e) => EndDragPan();

    private void Viewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PdfPanDragBtn?.IsChecked != true || _viewer == null) return;
        _dragPanActive = true;
        _dragPanLast = e.GetPosition(_viewer);
        _viewer.CaptureMouse();
        _viewer.Cursor = System.Windows.Input.Cursors.Hand;
        e.Handled = true;
    }

    private void Viewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragPanActive || _viewer == null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDragPan();
            return;
        }
        var now = e.GetPosition(_viewer);
        var dx = _dragPanLast.X - now.X;
        var dy = _dragPanLast.Y - now.Y;
        _dragPanLast = now;
        if (dx != 0 || dy != 0)
            PdfiumViewerTools.Pan(_viewer, dx, dy);
        e.Handled = true;
    }

    private void Viewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragPanActive) return;
        EndDragPan();
        e.Handled = true;
    }

    private void EndDragPan()
    {
        _dragPanActive = false;
        if (_viewer == null) return;
        if (_viewer.IsMouseCaptured)
            _viewer.ReleaseMouseCapture();
        _viewer.Cursor = null;
    }
}
