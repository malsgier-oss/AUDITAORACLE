using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using Serilog;
using WorkAudit.Config;
using WorkAudit.Core.Camera;
using WorkAudit.Storage;
using WorkAudit.Core.Export;
using WorkAudit.Core.ImageProcessing;
using WorkAudit.Core.Import;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Core.TextExtraction;

namespace WorkAudit.Views;

public partial class WebcamView : UserControl
{
    private readonly ILogger _log = LoggingService.ForContext<WebcamView>();
    private readonly InputView? _parentInput;
    private ICameraService? _cameraService;
    private IImageProcessingService? _imageProcessing;
    private ISearchExportService? _exportService;
    private IImportService? _importService;
    private IConfigStore? _configStore;
    private bool _isViewActive;
    private string _sessionFolder = "";
    private readonly ObservableCollection<PageItem> _pages = new();
    private Point2f[]? _lastCorners;
    private float _lastConfidence;
    private DispatcherTimer? _overlayTimer;
    private DateTime _explicitStatusUntil = DateTime.MinValue;
    private bool _isRetaking;
    private readonly object _detectionLock = new();
    private const int DetectionMaxSize = 640;

    // Auto-capture state: classic Auto mode uses document corners + motion; Scan Area + "Area auto" uses motion stability only.
    private DateTime _lastAutoCaptureAt = DateTime.MinValue;
    private readonly List<(DateTime At, Point2f[] Corners)> _cornerHistory = new();
    private Mat? _lastMotionFrame;
    private readonly object _motionLock = new();
    private const float StabilityConfidenceMin = 0.35f;
    private const int StabilityDurationMs = 800;
    private const float CornerEpsilonPx = 12f;
    private const double MotionMeanDiffThreshold = 20.0;
    private const int DefaultAutoCooldownMs = 8000;
    private const int BestFrameCount = 3;
    private const int BestFrameDelayMs = 50;
    private volatile bool _overlayRunning;
    private DispatcherTimer? _captureFlashTimer;
    private bool _spaceKeyDown;
    private DispatcherTimer? _spaceTapTimer;
    private bool _spaceTapPending;
    private DateTime _lastSpaceTapAtUtc = DateTime.MinValue;
    private const int SpaceDoubleTapThresholdMs = 350;

    private const int ModeManual = 0;
    private const int ModeAuto = 1;
    private const int ModeScanArea = 2;

    /// <summary>Scan region in normalized full-frame coordinates (0–1).</summary>
    private readonly object _roiLock = new();
    private bool _hasRoi;
    private double _roiNormX, _roiNormY, _roiNormW, _roiNormH;

    private System.Windows.Point? _roiDragStart;
    private System.Windows.Shapes.Rectangle? _roiRubberBand;
    private System.Windows.Shapes.Rectangle? _roiCommittedRect;

    private DateTime _scanAreaMotionLowSince = DateTime.MinValue;
    private const int ScanAreaStabilityMs = 600;

    /// <summary>Physical device display name — locked selection for document capture.</summary>
    private const string PreferredCameraName = "Doccamera";

    public WebcamView(InputView? parentInput = null)
    {
        InitializeComponent();
        _parentInput = parentInput;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isViewActive = true;
        _imageProcessing = ServiceContainer.GetService<IImageProcessingService>();
        _exportService = ServiceContainer.GetService<ISearchExportService>();
        _importService = ServiceContainer.GetService<IImportService>();
        _configStore = ServiceContainer.GetService<IConfigStore>();
        PopulateModeControls();
        ThumbnailsList.ItemsSource = _pages;
        Focusable = true;
        Focus();

        // Transient page images (JPEG) + PDF live under %TEMP%\WorkAudit_Webcam\ until import copies the PDF to branch/section/type.
        CameraCombo.Items.Clear();
        CameraCombo.Items.Add("Loading cameras...");
        CameraCombo.SelectedIndex = 0;
        Task.Run(() =>
        {
            try
            {
                var cameras = ServiceContainer.GetService<ICameraService>().GetAvailableCameras();
                Dispatcher.BeginInvoke(() =>
                {
                    CameraCombo.Items.Clear();
                    foreach (var cam in cameras)
                        CameraCombo.Items.Add(cam);
                    if (CameraCombo.Items.Count > 0)
                        CameraCombo.SelectedIndex = 0;
                    else
                    {
                        CameraCombo.Items.Add("No cameras found");
                        CameraCombo.SelectedIndex = 0;
                    }

                    ApplyPreferredCameraSelectionAndLock();
                });
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Webcam init failed");
                Dispatcher.BeginInvoke(() =>
                {
                    CameraCombo.Items.Clear();
                    CameraCombo.Items.Add("No cameras found");
                    CameraCombo.SelectedIndex = 0;
                    ApplyPreferredCameraSelectionAndLock();
                });
            }
        });
    }

    /// <summary>Select Doccamera when present; disable combo permanently for fixed device capture.</summary>
    private void ApplyPreferredCameraSelectionAndLock()
    {
        if (CameraCombo == null) return;

        var found = false;
        foreach (var item in CameraCombo.Items)
        {
            if (item is CameraInfo ci &&
                string.Equals(ci.Name.Trim(), PreferredCameraName, StringComparison.OrdinalIgnoreCase))
            {
                CameraCombo.SelectedItem = item;
                found = true;
                break;
            }
        }

        if (!found && CameraCombo.Items.Count > 0 && CameraCombo.SelectedItem is CameraInfo first)
            _log.Warning("Preferred camera '{Preferred}' not found; using '{Actual}'", PreferredCameraName, first.Name);
        else if (!found && CameraCombo.Items.Count > 0)
            _log.Warning("Preferred camera '{Preferred}' not found", PreferredCameraName);

        UpdateDocCameraStatusIndicator(found);
        UpdateCameraInfoLabel();
        CameraCombo.IsEnabled = false;
    }

    private void UpdateDocCameraStatusIndicator(bool connected)
    {
        if (DocCameraStatusDot == null || DocCameraStatusLabel == null) return;

        if (connected)
        {
            DocCameraStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71));
            DocCameraStatusLabel.Text = "Doccamera: connected";
        }
        else
        {
            DocCameraStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
            DocCameraStatusLabel.Text = "Doccamera: not connected";
        }
    }

    private void PopulateCamerasAsync()
    {
        try
        {
            var cameras = ServiceContainer.GetService<ICameraService>().GetAvailableCameras();
            Dispatcher.Invoke(() =>
            {
                CameraCombo.Items.Clear();
                foreach (var cam in cameras)
                    CameraCombo.Items.Add(cam);
                if (CameraCombo.Items.Count > 0)
                    ApplyPreferredCameraSelectionAndLock();
                else
                {
                    CameraCombo.Items.Add("No cameras found");
                    CameraCombo.SelectedIndex = 0;
                    CameraInfoLabel.Text = "No cameras detected";
                    ApplyPreferredCameraSelectionAndLock();
                }
            });
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Camera enumeration failed");
            Dispatcher.Invoke(() =>
            {
                CameraCombo.Items.Clear();
                CameraCombo.Items.Add("No cameras found");
                CameraCombo.SelectedIndex = 0;
                CameraInfoLabel.Text = "No cameras detected";
                ApplyPreferredCameraSelectionAndLock();
            });
        }
    }

    private void PopulateModeControls()
    {
        var defaultScanArea = _configStore?.GetSettingBool("webcam_default_scan_area_mode", false) ?? false;
        var defaultAuto = _configStore?.GetSettingBool("enable_auto_capture", false) ?? false;
        CaptureModeCombo.Items.Clear();
        CaptureModeCombo.Items.Add("Manual");
        CaptureModeCombo.Items.Add("Auto");
        CaptureModeCombo.Items.Add("Scan Area");
        if (defaultScanArea)
            CaptureModeCombo.SelectedIndex = ModeScanArea;
        else
            CaptureModeCombo.SelectedIndex = defaultAuto ? ModeAuto : ModeManual;

        if (ScanAreaAutoCaptureCheck != null)
            ScanAreaAutoCaptureCheck.IsChecked = _configStore?.GetSettingBool("webcam_scan_area_auto_capture", false) ?? false;

        ApplyScanAreaUiState();
    }

    private void CaptureModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Reset auto state on any mode switch
        lock (_cornerHistory) { _cornerHistory.Clear(); }
        lock (_motionLock)
        {
            _lastMotionFrame?.Dispose();
            _lastMotionFrame = null;
        }
        _scanAreaMotionLowSince = DateTime.MinValue;

        if (CaptureModeCombo.SelectedIndex != ModeScanArea)
            ClearRoi(keepScanAreaCheckbox: true);
        else
            RedrawCommittedRoiOutline();

        ApplyScanAreaUiState();
    }

    private void ScanAreaAutoCaptureCheck_Changed(object sender, RoutedEventArgs e)
    {
        _scanAreaMotionLowSince = DateTime.MinValue;
    }

    private void ApplyScanAreaUiState()
    {
        if (ScanAreaAutoCaptureCheck == null || RoiInteractionCanvas == null)
            return;
        var scan = IsScanAreaMode();
        ScanAreaAutoCaptureCheck.Visibility = scan ? Visibility.Visible : Visibility.Collapsed;
        RoiInteractionCanvas.IsHitTestVisible = scan && _cameraService is { IsCapturing: true };
    }

    private bool IsClassicAutoMode() => CaptureModeCombo.SelectedIndex == ModeAuto;

    private bool IsScanAreaMode() => CaptureModeCombo.SelectedIndex == ModeScanArea;

    private bool IsScanAreaAutoCaptureEnabled() =>
        IsScanAreaMode() && ScanAreaAutoCaptureCheck?.IsChecked == true;

    /// <summary>Always pick the sharpest of N consecutive frames (Manual, Auto, and Scan Area)
    /// so the saved capture is the highest-quality available frame, not just the latest one.</summary>
    private bool UseBestFrameCapture() => true;

    private void ClearRoi(bool keepScanAreaCheckbox = false)
    {
        lock (_roiLock)
        {
            _hasRoi = false;
            _roiNormX = _roiNormY = _roiNormW = _roiNormH = 0;
        }
        RoiInteractionCanvas?.Children.Clear();
        _roiCommittedRect = null;
        _roiRubberBand = null;
        _roiDragStart = null;
        if (!keepScanAreaCheckbox)
            ApplyScanAreaUiState();
    }

    private void PreviewBorder_SizeChanged(object sender, SizeChangedEventArgs e) => SyncOverlayCanvasSizes();

    private void SyncOverlayCanvasSizes()
    {
        if (PreviewBorder == null || OverlayCanvas == null || RoiInteractionCanvas == null) return;
        var w = PreviewBorder.ActualWidth;
        var h = PreviewBorder.ActualHeight;
        if (w <= 0 || h <= 0) return;
        OverlayCanvas.Width = w;
        OverlayCanvas.Height = h;
        RoiInteractionCanvas.Width = w;
        RoiInteractionCanvas.Height = h;
        RedrawCommittedRoiOutline();
    }

    private (int frameW, int frameH) GetPreviewFrameSize()
    {
        if (_cameraService is { IsCapturing: true, CurrentWidth: > 0, CurrentHeight: > 0 })
            return (_cameraService.CurrentWidth, _cameraService.CurrentHeight);
        return (0, 0);
    }

    /// <summary>Letterboxing match (Uniform): image area inside canvases.</summary>
    private (double scale, double offsetX, double offsetY) GetLetterboxForFrame(int frameW, int frameH)
    {
        var imgW = PreviewImage.ActualWidth;
        var imgH = PreviewImage.ActualHeight;
        var canvasW = RoiInteractionCanvas.ActualWidth;
        var canvasH = RoiInteractionCanvas.ActualHeight;
        if (imgW <= 0 || imgH <= 0 || frameW <= 0 || frameH <= 0 || canvasW <= 0 || canvasH <= 0)
            return (0, 0, 0);
        var scaleX = imgW / frameW;
        var scaleY = imgH / frameH;
        var scale = Math.Min(scaleX, scaleY);
        var offsetX = (canvasW - frameW * scale) / 2;
        var offsetY = (canvasH - frameH * scale) / 2;
        return (scale, offsetX, offsetY);
    }

    private bool TryCanvasDragToNormalized(System.Windows.Point a, System.Windows.Point b, int frameW, int frameH,
        out double nx, out double ny, out double nw, out double nh)
    {
        nx = ny = nw = nh = 0;
        var (scale, ox, oy) = GetLetterboxForFrame(frameW, frameH);
        if (scale <= 0) return false;

        var x1 = Math.Min(a.X, b.X);
        var y1 = Math.Min(a.Y, b.Y);
        var x2 = Math.Max(a.X, b.X);
        var y2 = Math.Max(a.Y, b.Y);

        var imgLeft = ox;
        var imgTop = oy;
        var imgRight = ox + frameW * scale;
        var imgBottom = oy + frameH * scale;

        x1 = Math.Clamp(x1, imgLeft, imgRight);
        x2 = Math.Clamp(x2, imgLeft, imgRight);
        y1 = Math.Clamp(y1, imgTop, imgBottom);
        y2 = Math.Clamp(y2, imgTop, imgBottom);

        if (x2 - x1 < 4 || y2 - y1 < 4) return false;

        nx = ((x1 - ox) / scale) / frameW;
        ny = ((y1 - oy) / scale) / frameH;
        nw = ((x2 - x1) / scale) / frameW;
        nh = ((y2 - y1) / scale) / frameH;

        nx = Math.Clamp(nx, 0, 1);
        ny = Math.Clamp(ny, 0, 1);
        nw = Math.Clamp(nw, 0, 1 - nx);
        nh = Math.Clamp(nh, 0, 1 - ny);
        return nw > 1e-4 && nh > 1e-4;
    }

    private void RoiInteractionCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsScanAreaMode() || _cameraService is not { IsCapturing: true }) return;
        var (fw, fh) = GetPreviewFrameSize();
        if (fw <= 0 || fh <= 0) return;

        e.Handled = true;
        var pos = e.GetPosition(RoiInteractionCanvas);
        _roiDragStart = pos;

        if (_roiRubberBand != null)
        {
            RoiInteractionCanvas.Children.Remove(_roiRubberBand);
            _roiRubberBand = null;
        }
        if (_roiCommittedRect != null)
        {
            RoiInteractionCanvas.Children.Remove(_roiCommittedRect);
            _roiCommittedRect = null;
        }

        _roiRubberBand = new System.Windows.Shapes.Rectangle
        {
            Stroke = System.Windows.Media.Brushes.DeepSkyBlue,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = System.Windows.Media.Brushes.Transparent,
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(_roiRubberBand, pos.X);
        Canvas.SetTop(_roiRubberBand, pos.Y);
        RoiInteractionCanvas.Children.Add(_roiRubberBand);
        Mouse.Capture(RoiInteractionCanvas, CaptureMode.Element);
    }

    private void RoiInteractionCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_roiRubberBand == null || _roiDragStart == null || !IsScanAreaMode()) return;
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        e.Handled = true;
        var pos = e.GetPosition(RoiInteractionCanvas);
        var start = _roiDragStart.Value;
        var x = Math.Min(start.X, pos.X);
        var y = Math.Min(start.Y, pos.Y);
        var w = Math.Abs(pos.X - start.X);
        var h = Math.Abs(pos.Y - start.Y);
        Canvas.SetLeft(_roiRubberBand, x);
        Canvas.SetTop(_roiRubberBand, y);
        _roiRubberBand.Width = Math.Max(1, w);
        _roiRubberBand.Height = Math.Max(1, h);
    }

    private void RoiInteractionCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsScanAreaMode()) return;
        if (_roiRubberBand == null || _roiDragStart == null)
        {
            Mouse.Capture(null);
            return;
        }

        e.Handled = true;
        Mouse.Capture(null);

        var (fw, fh) = GetPreviewFrameSize();
        var end = e.GetPosition(RoiInteractionCanvas);
        var start = _roiDragStart.Value;

        RoiInteractionCanvas.Children.Remove(_roiRubberBand);
        _roiRubberBand = null;
        _roiDragStart = null;

        if (fw <= 0 || fh <= 0 ||
            !TryCanvasDragToNormalized(start, end, fw, fh, out var nx, out var ny, out var nw, out var nh))
        {
            lock (_roiLock) { _hasRoi = false; }
            UpdateStatus("Scan Area: drag a larger rectangle on the document.");
            return;
        }

        lock (_roiLock)
        {
            _hasRoi = true;
            _roiNormX = nx;
            _roiNormY = ny;
            _roiNormW = nw;
            _roiNormH = nh;
        }
        _scanAreaMotionLowSince = DateTime.MinValue;
        RedrawCommittedRoiOutline();
        UpdateStatus("Scan Area: region saved. Capture when ready.");
    }

    private void RedrawCommittedRoiOutline()
    {
        if (RoiInteractionCanvas == null) return;
        if (!IsScanAreaMode())
        {
            RoiInteractionCanvas.Children.Clear();
            _roiCommittedRect = null;
            return;
        }

        bool has;
        double nx, ny, nw, nh;
        lock (_roiLock)
        {
            has = _hasRoi;
            nx = _roiNormX;
            ny = _roiNormY;
            nw = _roiNormW;
            nh = _roiNormH;
        }
        if (!has)
        {
            if (_roiCommittedRect != null)
            {
                RoiInteractionCanvas.Children.Remove(_roiCommittedRect);
                _roiCommittedRect = null;
            }
            return;
        }

        var (fw, fh) = GetPreviewFrameSize();
        if (fw <= 0 || fh <= 0) return;

        var (scale, ox, oy) = GetLetterboxForFrame(fw, fh);
        if (scale <= 0) return;

        var left = ox + nx * fw * scale;
        var top = oy + ny * fh * scale;
        var w = nw * fw * scale;
        var h = nh * fh * scale;

        if (_roiCommittedRect == null || !RoiInteractionCanvas.Children.Contains(_roiCommittedRect))
        {
            foreach (var child in RoiInteractionCanvas.Children.OfType<System.Windows.Shapes.Rectangle>().Where(r => r != _roiRubberBand).ToList())
                RoiInteractionCanvas.Children.Remove(child);
            _roiCommittedRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = System.Windows.Media.Brushes.Cyan,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 0, 255, 255))
            };
            RoiInteractionCanvas.Children.Insert(0, _roiCommittedRect);
        }

        Canvas.SetLeft(_roiCommittedRect, left);
        Canvas.SetTop(_roiCommittedRect, top);
        _roiCommittedRect.Width = Math.Max(2, w);
        _roiCommittedRect.Height = Math.Max(2, h);
    }

    private static OpenCvSharp.Rect NormalizeRoiToPixelRect(int frameW, int frameH, double nx, double ny, double nw, double nh)
    {
        var x = (int)Math.Floor(nx * frameW);
        var y = (int)Math.Floor(ny * frameH);
        var w = (int)Math.Ceiling(nw * frameW);
        var h = (int)Math.Ceiling(nh * frameH);
        x = Math.Clamp(x, 0, Math.Max(0, frameW - 1));
        y = Math.Clamp(y, 0, Math.Max(0, frameH - 1));
        w = Math.Clamp(w, 1, frameW - x);
        h = Math.Clamp(h, 1, frameH - y);
        return new OpenCvSharp.Rect(x, y, w, h);
    }

    private void UpdateScanAreaMotionStability(bool motionLow)
    {
        var now = DateTime.UtcNow;
        if (!motionLow)
        {
            _scanAreaMotionLowSince = DateTime.MinValue;
            return;
        }
        if (_scanAreaMotionLowSince == DateTime.MinValue)
            _scanAreaMotionLowSince = now;
    }

    private bool IsScanAreaMotionStableEnough() =>
        _scanAreaMotionLowSince != DateTime.MinValue
        && (DateTime.UtcNow - _scanAreaMotionLowSince).TotalMilliseconds >= ScanAreaStabilityMs;

    private void UpdateCameraInfoLabel()
    {
        if (CameraInfoLabel == null)
            return;

        if (CameraCombo.SelectedItem is not CameraInfo cam)
        {
            CameraInfoLabel.Text = "";
            return;
        }

        if (_cameraService?.IsCapturing == true && _cameraService.CurrentWidth > 0 && _cameraService.CurrentHeight > 0)
        {
            CameraInfoLabel.Text = $"{cam.Name} ({_cameraService.CurrentWidth}x{_cameraService.CurrentHeight})";
            return;
        }

        if (cam.Width > 0 && cam.Height > 0)
            CameraInfoLabel.Text = $"{cam.Name} ({cam.Width}x{cam.Height})";
        else
            CameraInfoLabel.Text = cam.Name;
    }


    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isViewActive = false;
        if (_overlayTimer != null)
        {
            _overlayTimer.Tick -= OnOverlayTick;
            _overlayTimer.Stop();
            _overlayTimer = null;
        }
        if (_spaceTapTimer != null)
        {
            _spaceTapTimer.Tick -= SpaceTapTimer_Tick;
            _spaceTapTimer.Stop();
            _spaceTapTimer = null;
        }
        ResetSpaceGestureState();
        StopCamera();
    }

    private void EnsureSessionFolder()
    {
        if (string.IsNullOrEmpty(_sessionFolder))
            _sessionFolder = Path.Combine(Path.GetTempPath(), "WorkAudit_Webcam", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sessionFolder);
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _cameraService = ServiceContainer.GetService<ICameraService>();
            _cameraService.FrameReady += OnFrameReady;
            _cameraService.Error += OnCameraError;

            var cameraIndex = 0;
            if (CameraCombo.SelectedItem is CameraInfo info)
                cameraIndex = info.Index;

            BtnStart.IsEnabled = false;
            var statusText = StatusLabel.Text;
            StatusLabel.Text = "Starting camera…";

            var started = await _cameraService.StartCaptureAsync(cameraIndex);

            if (started)
            {
                PlaceholderText.Visibility = Visibility.Collapsed;
                BtnStop.IsEnabled = true;
                BtnCapture.IsEnabled = true;
                SyncOverlayCanvasSizes();
                ApplyScanAreaUiState();
                StartOverlayDetection();
                var width = _cameraService.CurrentWidth;
                var height = _cameraService.CurrentHeight;
                if (width > 0 && height > 0)
                {
                    CameraInfoLabel.Text = $"Camera ready: {width}x{height}";
                    if (Math.Max(width, height) < 1000)
                        UpdateStatus("Low camera resolution – text may be less sharp.");
                    else
                        UpdateStatus("");
                }
                else
                {
                    UpdateStatus("");
                }
            }
            else
            {
                BtnStart.IsEnabled = true;
                StatusLabel.Text = statusText;
                MessageBox.Show("Failed to start camera.", "Camera Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            BtnStart.IsEnabled = true;
            StatusLabel.Text = "";
            MessageBox.Show($"Error: {ex.Message}", "Camera Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e) => StopCamera();

    private void StopCamera()
    {
        if (_overlayTimer != null)
        {
            _overlayTimer.Tick -= OnOverlayTick;
            _overlayTimer.Stop();
            _overlayTimer = null;
        }
        _overlayRunning = false;
        lock (_motionLock)
        {
            _lastMotionFrame?.Dispose();
            _lastMotionFrame = null;
        }
        lock (_cornerHistory) { _cornerHistory.Clear(); }
        if (_cameraService != null)
        {
            _cameraService.FrameReady -= OnFrameReady;
            _cameraService.Error -= OnCameraError;
            _cameraService.StopCapture();
            _cameraService = null;
        }
        PlaceholderText.Visibility = Visibility.Visible;
        PreviewImage.Source = null;
        OverlayCanvas.Children.Clear();
        ClearRoi();
        BtnStart.IsEnabled = true;
        BtnStop.IsEnabled = false;
        BtnCapture.IsEnabled = false;
        BtnFinish.IsEnabled = false;
        BtnRetake.IsEnabled = false;
        BtnDelete.IsEnabled = false;
        CameraCombo.IsEnabled = false;
        ResetSpaceGestureState();
    }

    private void StartOverlayDetection()
    {
        _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _overlayTimer.Tick += OnOverlayTick;
        _overlayTimer.Start();
    }

    private void OnFrameReady(BitmapSource frame)
    {
        if (!_isViewActive) return;
        Dispatcher.BeginInvoke(() => { PreviewImage.Source = frame; });
    }

    private void OnOverlayTick(object? sender, EventArgs e)
    {
        if (_overlayRunning) return;

        var enableOverlay = _configStore?.GetSettingBool("enable_document_scan_overlay", true) ?? true;
        var scanArea = IsScanAreaMode();
        var classicAuto = IsClassicAutoMode();
        var scanAreaAuto = IsScanAreaAutoCaptureEnabled();
        var needDetection = classicAuto || (!scanArea && enableOverlay);
        if (!classicAuto && !scanAreaAuto && !needDetection && !scanArea) return;

        _overlayRunning = true;
        Task.Run(() =>
        {
            try
            {
                if (!classicAuto && !scanAreaAuto && !needDetection && scanArea)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        RedrawCommittedRoiOutline();
                        UpdateStatus("");
                    });
                    return;
                }

                var mat = _cameraService?.CaptureFrameMat();
                if (mat == null) return;

                try
                {
                    var origW = mat.Width;
                    var origH = mat.Height;
                    Mat detectMat = mat;
                    float scaleX = 1f, scaleY = 1f;
                    if (mat.Width > DetectionMaxSize || mat.Height > DetectionMaxSize)
                    {
                        var maxDim = Math.Max(mat.Width, mat.Height);
                        var scale = (float)DetectionMaxSize / maxDim;
                        var w = (int)(mat.Width * scale);
                        var h = (int)(mat.Height * scale);
                        detectMat = new Mat();
                        Cv2.Resize(mat, detectMat, new OpenCvSharp.Size(w, h));
                        scaleX = (float)origW / w;
                        scaleY = (float)origH / h;
                    }

                    bool found = false;
                    Point2f[] corners = Array.Empty<Point2f>();
                    float confidence = 0f;
                    if (needDetection)
                    {
                        var result = _imageProcessing!.DetectDocumentCorners(detectMat);
                        found = result.Found;
                        corners = result.Corners;
                        confidence = result.Confidence;
                    }

                    bool shouldClassicAuto = false;
                    if (classicAuto && found && corners.Length == 4)
                    {
                        var motionLow = ComputeMotionLow(detectMat);
                        UpdateCornerHistory(confidence, corners);
                        var cornersStable = CheckCornerStability();
                        var inCooldown = (DateTime.UtcNow - _lastAutoCaptureAt).TotalMilliseconds < GetAutoCooldownMs();

                        if (confidence >= StabilityConfidenceMin && cornersStable && motionLow && !inCooldown)
                            shouldClassicAuto = true;
                    }

                    bool shouldScanAreaAuto = false;
                    if (scanAreaAuto)
                    {
                        bool hasRoi;
                        lock (_roiLock) { hasRoi = _hasRoi; }
                        if (hasRoi)
                        {
                            var motionLow = ComputeMotionLow(detectMat);
                            UpdateScanAreaMotionStability(motionLow);
                            var inCooldown = (DateTime.UtcNow - _lastAutoCaptureAt).TotalMilliseconds < GetAutoCooldownMs();
                            if (motionLow && IsScanAreaMotionStableEnough() && !inCooldown)
                                shouldScanAreaAuto = true;
                        }
                    }

                    if (detectMat != mat)
                        detectMat.Dispose();

                    Point2f[]? fullResCorners = null;
                    if (found && corners.Length == 4)
                    {
                        fullResCorners = corners.Select(p => new Point2f(p.X * scaleX, p.Y * scaleY)).ToArray();
                    }

                    lock (_detectionLock)
                    {
                        _lastCorners = fullResCorners;
                        _lastConfidence = confidence;
                    }
                    var cornersToDraw = fullResCorners ?? corners;
                    var needDocCorners = enableOverlay && !scanArea;
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (needDocCorners)
                            DrawOverlay(found, cornersToDraw, confidence, origW, origH);
                        else
                            OverlayCanvas.Children.Clear();

                        RedrawCommittedRoiOutline();
                        UpdateStatus("");
                        if (shouldClassicAuto || shouldScanAreaAuto)
                            TryTriggerAutoCapture();
                    });
                }
                finally
                {
                    mat.Dispose();
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Overlay detection failed");
            }
            finally
            {
                _overlayRunning = false;
            }
        });
    }

    private bool ComputeMotionLow(Mat detectMat)
    {
        Mat gray;
        if (detectMat.Channels() == 1)
            gray = detectMat.Clone();
        else
        {
            gray = new Mat();
            Cv2.CvtColor(detectMat, gray, ColorConversionCodes.BGR2GRAY);
        }
        using (gray)
        {
            using var small = new Mat();
            Cv2.Resize(gray, small, new OpenCvSharp.Size(80, 60));
            lock (_motionLock)
            {
                if (_lastMotionFrame == null || _lastMotionFrame.Empty())
                {
                    _lastMotionFrame?.Dispose();
                    _lastMotionFrame = small.Clone();
                    return false;
                }
                using var diff = new Mat();
                Cv2.Absdiff(small, _lastMotionFrame, diff);
                var mean = Cv2.Mean(diff);
                var meanVal = mean.Val0;
                _lastMotionFrame.Dispose();
                _lastMotionFrame = small.Clone();
                return meanVal < MotionMeanDiffThreshold;
            }
        }
    }

    private void UpdateCornerHistory(float confidence, Point2f[] corners)
    {
        if (confidence < StabilityConfidenceMin) return;
        var now = DateTime.UtcNow;
        lock (_cornerHistory)
        {
            _cornerHistory.Add((now, corners.Select(p => new Point2f(p.X, p.Y)).ToArray()));
            while (_cornerHistory.Count > 0 && (now - _cornerHistory[0].At).TotalMilliseconds > StabilityDurationMs + 100)
                _cornerHistory.RemoveAt(0);
        }
    }

    private bool CheckCornerStability()
    {
        lock (_cornerHistory)
        {
            if (_cornerHistory.Count < 2) return false;
            var oldest = _cornerHistory[0].At;
            if ((DateTime.UtcNow - oldest).TotalMilliseconds < StabilityDurationMs) return false;

            var samples = _cornerHistory.Select(x => x.Corners).ToList();
            for (int i = 0; i < 4; i++)
            {
                var pts = samples.Select(s => s[i]).ToArray();
                var cx = pts.Average(p => p.X);
                var cy = pts.Average(p => p.Y);
                var maxDist = pts.Max(p => (float)Math.Sqrt(Math.Pow(p.X - cx, 2) + Math.Pow(p.Y - cy, 2)));
                if (maxDist > CornerEpsilonPx) return false;
            }
            return true;
        }
    }

    private void TryTriggerAutoCapture()
    {
        if (_cameraService == null || !_cameraService.IsCapturing) return;
        if (!BtnCapture.IsEnabled) return; // Do not trigger while capture/processing is running
        if (IsScanAreaMode())
        {
            lock (_roiLock)
            {
                if (!_hasRoi) return;
            }
        }

        var multiPage = MultiPageCheck?.IsChecked == true;
        if (!multiPage && _pages.Count >= 1)
        {
            var prefix = IsScanAreaAutoCaptureEnabled() ? "Scan Area" : "Auto";
            UpdateStatus($"{prefix}: single-page captured. Press Finish to continue.");
            _explicitStatusUntil = DateTime.UtcNow.AddMilliseconds(250);
            return;
        }

        _isRetaking = false;
        _lastAutoCaptureAt = DateTime.UtcNow;
        _scanAreaMotionLowSince = DateTime.MinValue;
        lock (_cornerHistory) { _cornerHistory.Clear(); }
        BtnCapture.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    }

    private void DrawOverlay(bool found, Point2f[]? corners, float confidence, int frameW, int frameH)
    {
        OverlayCanvas.Children.Clear();
        if (!found || corners == null || corners.Length != 4) return;

        var imgW = PreviewImage.ActualWidth;
        var imgH = PreviewImage.ActualHeight;
        if (imgW <= 0 || imgH <= 0) return;

        var scaleX = imgW / Math.Max(1, frameW);
        var scaleY = imgH / Math.Max(1, frameH);
        var scale = Math.Min(scaleX, scaleY);
        var offsetX = (OverlayCanvas.ActualWidth - frameW * scale) / 2;
        var offsetY = (OverlayCanvas.ActualHeight - frameH * scale) / 2;

        var pts = corners.Select(p => new System.Windows.Point(p.X * scale + offsetX, p.Y * scale + offsetY)).ToArray();
        var poly = new System.Windows.Shapes.Polygon
        {
            Points = new PointCollection(pts),
            Stroke = confidence >= 0.5 ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 180, 0)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 0)),
            StrokeThickness = 2,
            Fill = System.Windows.Media.Brushes.Transparent
        };
        OverlayCanvas.Children.Add(poly);
        foreach (var pt in pts)
        {
            var el = new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8,
                Fill = System.Windows.Media.Brushes.White,
                Stroke = System.Windows.Media.Brushes.DarkGray,
                StrokeThickness = 1
            };
            Canvas.SetLeft(el, pt.X - 4);
            Canvas.SetTop(el, pt.Y - 4);
            OverlayCanvas.Children.Add(el);
        }
    }

    private void OnCameraError(string message)
    {
        Dispatcher.BeginInvoke(() =>
            MessageBox.Show(message, "Camera Error", MessageBoxButton.OK, MessageBoxImage.Warning));
    }

    private void WebcamView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        // Do not intercept Alt+Space (Windows system menu) or other modifier combos for Space
        if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            return;
        switch (e.Key)
        {
            case Key.Return:
                BtnCapture.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                e.Handled = true;
                break;
            case Key.Space:
                // OS repeat while keyboard Space is held — keep first press only.
                if (e.IsRepeat)
                {
                    e.Handled = true;
                    break;
                }
                if (_spaceKeyDown)
                {
                    e.Handled = true;
                    break;
                }
                _spaceKeyDown = true;
                e.Handled = true;
                break;
            case Key.OemBackslash:
            case Key.Oem5:  // Oem102 shares value with Oem5; both map to backslash/pipe key on various layouts
                BtnFinish.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                e.Handled = true;
                break;
            case Key.R:
                BtnRetake.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                e.Handled = true;
                break;
            case Key.Delete:
                BtnDelete.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                e.Handled = true;
                break;
            case Key.Escape:
                if (_pages.Count > 0)
                {
                    if (MessageBox.Show("Exit and discard captured pages?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        DeleteSessionFolderIfAllowed();
                        _pages.Clear();
                        _isRetaking = false;
                        UpdatePageCount();
                        StopCamera();
                    }
                }
                else
                    StopCamera();
                e.Handled = true;
                break;
        }
    }

    private void WebcamView_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) return;

        if (!_spaceKeyDown)
        {
            e.Handled = true;
            return;
        }
        _spaceKeyDown = false;
        HandleSpaceTapGesture();

        e.Handled = true;
    }

    private void ResetSpaceGestureState()
    {
        _spaceKeyDown = false;
        _spaceTapPending = false;
        _lastSpaceTapAtUtc = DateTime.MinValue;
        StopSpaceTapTimer();
    }

    private void HandleSpaceTapGesture()
    {
        var now = DateTime.UtcNow;
        if (_spaceTapPending && (now - _lastSpaceTapAtUtc).TotalMilliseconds <= SpaceDoubleTapThresholdMs)
        {
            _spaceTapPending = false;
            StopSpaceTapTimer();
            BtnFinish.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            return;
        }

        _spaceTapPending = true;
        _lastSpaceTapAtUtc = now;
        StartSpaceTapTimer();
    }

    private void StartSpaceTapTimer()
    {
        _spaceTapTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SpaceDoubleTapThresholdMs) };
        _spaceTapTimer.Tick -= SpaceTapTimer_Tick;
        _spaceTapTimer.Tick += SpaceTapTimer_Tick;
        _spaceTapTimer.Stop();
        _spaceTapTimer.Start();
    }

    private void StopSpaceTapTimer()
    {
        if (_spaceTapTimer == null) return;
        _spaceTapTimer.Tick -= SpaceTapTimer_Tick;
        _spaceTapTimer.Stop();
    }

    private void SpaceTapTimer_Tick(object? sender, EventArgs e)
    {
        StopSpaceTapTimer();
        if (!_spaceTapPending) return;

        _spaceTapPending = false;
        BtnCapture.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    }

    private void BtnCapture_Click(object sender, RoutedEventArgs e)
    {
        if (_cameraService == null || !_cameraService.IsCapturing || _imageProcessing == null) return;
        var multiPage = MultiPageCheck?.IsChecked == true;
        if (!multiPage && _pages.Count >= 1 && !_isRetaking)
        {
            UpdateStatus("Single-page mode: page already captured. Press Finish to continue.");
            _explicitStatusUntil = DateTime.UtcNow.AddMilliseconds(800);
            return;
        }

        var scanArea = IsScanAreaMode();
        if (scanArea)
        {
            lock (_roiLock)
            {
                if (!_hasRoi)
                {
                    UpdateStatus("Scan Area: drag a rectangle on the preview to set the scan region.");
                    _explicitStatusUntil = DateTime.UtcNow.AddMilliseconds(1200);
                    return;
                }
            }
        }

        var autoCrop = AutoCropCheck?.IsChecked == true;
        var useBestFrame = UseBestFrameCapture();
        const DocumentEnhanceMode enhanceMode = DocumentEnhanceMode.Color;

        double capRnx = 0, capRny = 0, capRnw = 0, capRnh = 0;
        if (scanArea)
        {
            lock (_roiLock)
            {
                capRnx = _roiNormX;
                capRny = _roiNormY;
                capRnw = _roiNormW;
                capRnh = _roiNormH;
            }
        }

        var isRetaking = _isRetaking && _pages.Count > 0;
        var replaceIndex = isRetaking ? _pages.Count - 1 : -1;
        var pageNum = isRetaking ? _pages[replaceIndex].PageNumber : _pages.Count + 1;
        var cameraService = _cameraService;
        var imageProcessing = _imageProcessing;
        var configStore = _configStore;

        EnsureSessionFolder();
        BtnCapture.IsEnabled = false;
        Task.Run(() =>
        {
            Mat? mat = null;
            try
            {
                // Capture frame (best-of-N for classic Auto or Scan Area + Area auto)
                if (useBestFrame)
                {
                    Mat? bestFrame = null;
                    double bestSharp = -1;
                    for (int i = 0; i < BestFrameCount; i++)
                    {
                        var candidate = cameraService.CaptureFrameMat();
                        if (candidate == null) continue;
                        var s = ComputeSharpness(candidate);
                        if (s > bestSharp) { bestFrame?.Dispose(); bestFrame = candidate; bestSharp = s; }
                        else candidate.Dispose();
                        if (i < BestFrameCount - 1) Thread.Sleep(BestFrameDelayMs);
                    }
                    mat = bestFrame;
                }
                else
                {
                    mat = cameraService.CaptureFrameMat();
                }

                if (mat == null) { Dispatcher.BeginInvoke(() => { BtnCapture.IsEnabled = true; }); return; }

                if (scanArea)
                {
                    try
                    {
                        var roiPx = NormalizeRoiToPixelRect(mat.Width, mat.Height, capRnx, capRny, capRnw, capRnh);
                        var cropped = new Mat(mat, roiPx).Clone();
                        mat.Dispose();
                        mat = cropped;
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "Scan Area ROI crop failed");
                        mat.Dispose();
                        Dispatcher.BeginInvoke(() =>
                        {
                            MessageBox.Show("Could not crop the selected scan region.", "Scan Area", MessageBoxButton.OK, MessageBoxImage.Warning);
                            BtnCapture.IsEnabled = true;
                        });
                        return;
                    }
                }

                Point2f[]? uiCorners = null;
                if (!scanArea)
                    lock (_detectionLock) { uiCorners = _lastCorners?.ToArray(); }

                Mat processed;
                if (autoCrop)
                {
                    // Try to re-detect corners on high-res frame for better accuracy
                    var (found, hiResCorners, _) = imageProcessing.DetectDocumentCorners(mat);
                    var cornersToUse = found ? hiResCorners : uiCorners;

                    if (cornersToUse != null && cornersToUse.Length == 4)
                        processed = imageProcessing.ApplyPerspectiveTransform(mat, cornersToUse, DocumentPaperSize.A4);
                    else
                        processed = mat.Clone();
                }
                else
                {
                    processed = mat.Clone();
                }
                mat.Dispose(); mat = null;

                var enhanced = imageProcessing.ApplyEnhanceMode(processed, enhanceMode);
                processed.Dispose();

                var maxPageLongEdge = configStore?.GetSettingInt("webcam_max_page_long_edge_px", 2480) ?? 2480;
                enhanced = DownscaleIfExceedsLongEdge(enhanced, maxPageLongEdge);

                var jpegQuality = Math.Clamp(configStore?.GetSettingInt("webcam_jpeg_quality", 93) ?? 93, 1, 100);
                var pagePath = Path.Combine(_sessionFolder, $"page_{pageNum:D3}.jpg");
                Cv2.ImWrite(pagePath, enhanced, new ImageEncodingParam(ImwriteFlags.JpegQuality, jpegQuality));
                var thumbPath = Path.Combine(_sessionFolder, $"thumb_{pageNum:D3}.png");
                using var thumb = new Mat();
                Cv2.Resize(enhanced, thumb, new OpenCvSharp.Size(80, 100));
                Cv2.ImWrite(thumbPath, thumb);
                enhanced.Dispose();

                Dispatcher.BeginInvoke(() =>
                {
                    var multiPage = MultiPageCheck?.IsChecked == true;
                    var item = new PageItem { FullPath = pagePath, ThumbPath = thumbPath, PageNumber = pageNum };
                    if (isRetaking && replaceIndex >= 0)
                    {
                        var old = _pages[replaceIndex];
                        try
                        {
                            if (File.Exists(old.FullPath)) File.Delete(old.FullPath);
                            if (File.Exists(old.ThumbPath)) File.Delete(old.ThumbPath);
                        }
                        catch (Exception ex)
                        {
                            _log.Debug(ex, "Failed to delete old page file");
                        }

                        _pages[replaceIndex] = item;
                    }
                    else { _pages.Add(item); }
                    
                    // Trigger background OCR immediately on capture
                    var ocrSvc = ServiceContainer.GetService<IOcrService>();
                    if (ocrSvc != null)
                    {
                        Task.Run(async () =>
                        {
                            try { item.OcrText = await ocrSvc.ExtractTextAsync(item.FullPath); }
                            catch (Exception ex) { Serilog.Log.Warning(ex, "Background OCR failed for captured page {Page}", item.PageNumber); }
                        });
                    }

                    _isRetaking = false;
                    UpdatePageCount(); UpdateButtonStates();
                    UpdateStatus(isRetaking ? "Page replaced." : "Page captured.");
                    if (useBestFrame)
                        TriggerCaptureFlash();
                    BtnCapture.IsEnabled = true;

                    // Single-page mode: finish document immediately after the first successful capture.
                    if (!multiPage && _pages.Count >= 1)
                    {
                        BtnFinish.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    }
                });
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Capture failed");
                mat?.Dispose();
                Dispatcher.BeginInvoke(() => { MessageBox.Show($"Capture failed: {ex.Message}", "Error"); BtnCapture.IsEnabled = true; });
            }
        });
    }

    /// <summary>
    /// When maxLongEdgePx is positive and the long edge exceeds it, downscales with area interpolation (disposing source).
    /// Otherwise returns source unchanged.
    /// </summary>
    private static Mat DownscaleIfExceedsLongEdge(Mat source, int maxLongEdgePx)
    {
        if (maxLongEdgePx <= 0)
            return source;
        var longEdge = Math.Max(source.Width, source.Height);
        if (longEdge <= maxLongEdgePx)
            return source;
        var scale = (double)maxLongEdgePx / longEdge;
        var nw = Math.Max(1, (int)Math.Round(source.Width * scale));
        var nh = Math.Max(1, (int)Math.Round(source.Height * scale));
        var dst = new Mat();
        Cv2.Resize(source, dst, new OpenCvSharp.Size(nw, nh), 0, 0, InterpolationFlags.Area);
        source.Dispose();
        return dst;
    }

    /// <summary>Compute Laplacian variance as a sharpness metric (higher = sharper).</summary>
    private static double ComputeSharpness(Mat frame)
    {
        using var gray = frame.Channels() == 1 ? frame.Clone() : frame.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var laplacian = new Mat();
        Cv2.Laplacian(gray, laplacian, MatType.CV_64F);
        Cv2.MeanStdDev(laplacian, out _, out var stddev);
        return stddev.Val0 * stddev.Val0; // variance
    }

    private void BtnFinish_Click(object sender, RoutedEventArgs e)
    {
        if (_pages.Count == 0)
        {
            UpdateStatus("No pages to finish.");
            return;
        }

        var pageSnapshots = _pages
            .Select(p => new
            {
                FullPath = p.FullPath,
                OcrText = p.OcrText ?? string.Empty
            })
            .ToList();
        var pagePaths = pageSnapshots.Select(p => p.FullPath).ToList();
        var sessionFolder = _sessionFolder;
        var exportSvc = _exportService;
        var importSvc = _importService;
        if (exportSvc == null || importSvc == null) return;

        var config = ServiceContainer.GetService<AppConfiguration>();
        var userBranch = config?.CurrentUserBranch ?? Branches.Default;
        var baseDir = config?.BaseDirectory ?? Defaults.GetDefaultBaseDir();
        var selectedBranch = _parentInput?.BranchCombo?.SelectedItem as string ?? userBranch;
        var documentDate = _parentInput?.DocumentDatePicker?.SelectedDate ?? DateTime.Today;
        var selectedSection = _parentInput?.GetSelectedSectionOrNull();
        var selectedDocumentType = _parentInput?.GetSelectedDocumentTypeOrNull();

        BtnFinish.IsEnabled = false;
        UpdateStatus("Creating PDF...");
        Task.Run(() =>
        {
            try
            {
                var pdfPath = Path.Combine(sessionFolder, $"document_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                exportSvc.CreatePdfFromImagePaths(pagePaths, pdfPath);

                var options = new ImportOptions
                {
                    Branch = selectedBranch,
                    Section = selectedSection,
                    DocumentType = selectedDocumentType,
                    DocumentDate = documentDate,
                    BaseDirectory = baseDir,
                    CopyToBaseDir = true,
                    ExtractPhoto = false,
                    PreExtractedPageTexts = pageSnapshots.Select(p => p.OcrText).ToList()
                };

                var result = importSvc.ImportFileAsync(pdfPath, options).GetAwaiter().GetResult();

                Dispatcher.BeginInvoke(() =>
                {
                    ResetSession();
                    UpdateStatus($"Document saved: {result.SuccessCount} item(s) imported.");
                });
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Finish document failed");
                Dispatcher.BeginInvoke(() =>
                {
                    MessageBox.Show($"Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    BtnFinish.IsEnabled = true;
                });
            }
        });
    }

    private void BtnRetake_Click(object sender, RoutedEventArgs e)
    {
        if (_pages.Count == 0) return;
        _isRetaking = true;
        UpdateButtonStates();
        UpdateStatus("Retake: next capture will replace last page.");
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ThumbnailsList.SelectedItem is not PageItem item) return;
        _pages.Remove(item);
        _isRetaking = false;
        try { File.Delete(item.FullPath); if (File.Exists(item.ThumbPath)) File.Delete(item.ThumbPath); } catch (Exception ex) { _log.Debug(ex, "Failed to delete page file"); }
        UpdatePageCount();
        UpdateButtonStates();
        UpdateStatus("Page deleted.");
    }

    private void ThumbnailsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtonStates();

    private void ResetSession()
    {
        _pages.Clear();
        _isRetaking = false;
        DeleteSessionFolderIfAllowed();
        _sessionFolder = "";
        UpdatePageCount();
        UpdateButtonStates();
        UpdateStatus("Ready for next document.");
    }

    private void DeleteSessionFolderIfAllowed()
    {
        var keepFolder = _configStore?.GetSettingBool("webcam_keep_session_folder", false) ?? false;
        if (keepFolder || string.IsNullOrEmpty(_sessionFolder) || !Directory.Exists(_sessionFolder)) return;
        try
        {
            Directory.Delete(_sessionFolder, recursive: true);
            _log.Debug("Deleted session folder: {Path}", _sessionFolder);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to delete session folder: {Path}", _sessionFolder);
        }
    }

    private void UpdatePageCount()
    {
        PageCountLabel.Text = $"Pages: {_pages.Count}";
    }

    private void UpdateButtonStates()
    {
        BtnFinish.IsEnabled = _pages.Count > 0;
        BtnRetake.IsEnabled = _pages.Count > 0;
        BtnDelete.IsEnabled = ThumbnailsList.SelectedItem != null;
    }

    private void TriggerCaptureFlash()
    {
        if (CaptureFlashOverlay == null) return;

        _captureFlashTimer?.Stop();
        CaptureFlashOverlay.Visibility = Visibility.Collapsed;
        CaptureFlashOverlay.Visibility = Visibility.Visible;
        _captureFlashTimer ??= new DispatcherTimer();
        _captureFlashTimer.Interval = TimeSpan.FromMilliseconds(180);
        _captureFlashTimer.Tick -= CaptureFlashTimer_Tick;
        _captureFlashTimer.Tick += CaptureFlashTimer_Tick;
        _captureFlashTimer.Start();
    }

    private void CaptureFlashTimer_Tick(object? sender, EventArgs e)
    {
        if (_captureFlashTimer == null) return;
        _captureFlashTimer.Stop();
        _captureFlashTimer.Tick -= CaptureFlashTimer_Tick;
        if (CaptureFlashOverlay != null)
        {
            CaptureFlashOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private bool ShowsAutoCooldownStatus() =>
        IsClassicAutoMode() || IsScanAreaAutoCaptureEnabled();

    private int GetAutoCooldownMs()
    {
        var defaultSeconds = DefaultAutoCooldownMs / 1000;
        var seconds = _configStore?.GetSettingInt("auto_capture_cooldown_seconds", defaultSeconds) ?? defaultSeconds;
        seconds = Math.Clamp(seconds, 1, 30);
        return seconds * 1000;
    }

    private void UpdateStatus(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            StatusLabel.Text = text;
            _explicitStatusUntil = DateTime.UtcNow.AddMilliseconds(250);
            return;
        }
        if (DateTime.UtcNow < _explicitStatusUntil) return;
        if (ShowsAutoCooldownStatus())
        {
            var prefix = IsScanAreaAutoCaptureEnabled() ? "Scan Area" : "Auto";
            var autoCooldownMs = GetAutoCooldownMs();
            var elapsedSinceAutoCaptureMs = (DateTime.UtcNow - _lastAutoCaptureAt).TotalMilliseconds;
            var cooldownRemainingMs = autoCooldownMs - elapsedSinceAutoCaptureMs;
            if (cooldownRemainingMs > 0)
            {
                StatusLabel.Text = $"{prefix}: next capture in {cooldownRemainingMs / 1000d:0.0}s";
                return;
            }

            StatusLabel.Text = $"{prefix}: Ready";
            return;
        }
        if (IsScanAreaMode())
        {
            lock (_roiLock)
            {
                if (!_hasRoi)
                    StatusLabel.Text = "Scan Area: drag to select region";
                else if (IsScanAreaAutoCaptureEnabled())
                    StatusLabel.Text = "Scan Area: hold steady for auto-capture";
                else
                    StatusLabel.Text = "Scan Area: Ready — press Enter or Space to capture";
            }
            return;
        }
        lock (_detectionLock)
        {
            StatusLabel.Text = _lastCorners != null && _lastConfidence >= 0.5f ? "Document detected" : "Hold steady / Not detected";
        }
    }

    private sealed class PageItem
    {
        public string FullPath { get; set; } = "";
        public string ThumbPath { get; set; } = "";
        public int PageNumber { get; set; }
        public string? OcrText { get; set; }
        public string DisplayLabel => $"Page {PageNumber}";
    }
}
