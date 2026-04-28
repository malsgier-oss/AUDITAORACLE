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

    // Auto-capture state (only when enable_auto_capture=true and CaptureMode=Auto)
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

        // Transient page/PDF files live under %TEMP%\WorkAudit_Webcam\ until import copies the PDF to branch/section/type.
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

        UpdateCameraInfoLabel();
        CameraCombo.IsEnabled = false;
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
        var defaultAuto = _configStore?.GetSettingBool("enable_auto_capture", false) ?? false;
        CaptureModeCombo.Items.Clear();
        CaptureModeCombo.Items.Add("Manual");
        CaptureModeCombo.Items.Add("Auto");
        CaptureModeCombo.SelectedIndex = defaultAuto ? 1 : 0;
        CaptureModeCombo.SelectionChanged += CaptureModeCombo_SelectionChanged;
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
    }

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
        var isAutoMode = CaptureModeCombo.SelectedIndex == 1;
        if (!enableOverlay && !isAutoMode) return;

        _overlayRunning = true;
        Task.Run(() =>
        {
            try
            {
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

                    var (found, corners, confidence) = _imageProcessing!.DetectDocumentCorners(detectMat);

                    bool shouldAutoCapture = false;
                    if (isAutoMode && found && corners.Length == 4)
                    {
                        var motionLow = ComputeMotionLow(detectMat);
                        UpdateCornerHistory(confidence, corners);
                        var cornersStable = CheckCornerStability();
                        var inCooldown = (DateTime.UtcNow - _lastAutoCaptureAt).TotalMilliseconds < GetAutoCooldownMs();

                        if (confidence >= StabilityConfidenceMin && cornersStable && motionLow && !inCooldown)
                            shouldAutoCapture = true;
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
                    var autoCapture = shouldAutoCapture;
                    var drawOverlay = enableOverlay;
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (drawOverlay)
                            DrawOverlay(found, cornersToDraw, confidence, origW, origH);
                        UpdateStatus("");
                        if (autoCapture)
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

        var multiPage = MultiPageCheck?.IsChecked == true;
        if (!multiPage && _pages.Count >= 1)
        {
            UpdateStatus("Auto: single-page captured. Press Finish to continue.");
            _explicitStatusUntil = DateTime.UtcNow.AddMilliseconds(250);
            return;
        }

        _isRetaking = false;
        _lastAutoCaptureAt = DateTime.UtcNow;
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

        var autoCrop = AutoCropCheck?.IsChecked == true;
        var isAutoMode = IsAutoModeActive();
        const DocumentEnhanceMode enhanceMode = DocumentEnhanceMode.Color;
        
        // Cache UI corners as fallback
        Point2f[]? uiCorners = null;
        lock (_detectionLock) { uiCorners = _lastCorners?.ToArray(); }
        
        var isRetaking = _isRetaking && _pages.Count > 0;
        var replaceIndex = isRetaking ? _pages.Count - 1 : -1;
        var pageNum = isRetaking ? _pages[replaceIndex].PageNumber : _pages.Count + 1;
        var cameraService = _cameraService;
        var imageProcessing = _imageProcessing;

        EnsureSessionFolder();
        BtnCapture.IsEnabled = false;
        Task.Run(() =>
        {
            Mat? mat = null;
            try
            {
                // Capture frame (using best-of-N for auto mode)
                if (isAutoMode)
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
                
                var pagePath = Path.Combine(_sessionFolder, $"page_{pageNum:D3}.png");
                Cv2.ImWrite(pagePath, enhanced);
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
                    if (isAutoMode)
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

    private bool IsAutoModeActive()
    {
        return CaptureModeCombo.SelectedIndex == 1;
    }

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
        if (IsAutoModeActive())
        {
            var autoCooldownMs = GetAutoCooldownMs();
            var elapsedSinceAutoCaptureMs = (DateTime.UtcNow - _lastAutoCaptureAt).TotalMilliseconds;
            var cooldownRemainingMs = autoCooldownMs - elapsedSinceAutoCaptureMs;
            if (cooldownRemainingMs > 0)
            {
                StatusLabel.Text = $"Auto: next capture in {cooldownRemainingMs / 1000d:0.0}s";
                return;
            }

            StatusLabel.Text = "Auto: Ready";
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
