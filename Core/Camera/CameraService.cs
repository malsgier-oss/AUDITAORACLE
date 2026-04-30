using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DirectShowLib;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Core.Camera;

/// <summary>
/// Service for camera capture and live video preview.
/// Uses OpenCvSharp for cross-platform webcam access.
/// </summary>
public interface ICameraService : IDisposable
{
    bool IsCapturing { get; }
    int CurrentCameraIndex { get; }
    int CurrentWidth { get; }
    int CurrentHeight { get; }
    List<CameraInfo> GetAvailableCameras();
    bool StartCapture(int cameraIndex = 0);
    Task<bool> StartCaptureAsync(int cameraIndex, CancellationToken cancellationToken = default);
    void StopCapture();
    BitmapSource? CaptureFrame();
    Mat? CaptureFrameMat();
    byte[]? CaptureFrameBytes();
    Task<string?> CaptureAndSaveAsync(string outputPath);

    event Action<BitmapSource>? FrameReady;
    event Action<string>? Error;
}

public class CameraService : ICameraService
{
    /// <summary>
    /// Preferred stream sizes for document cameras (MJPEG on many UVC devices), descending by megapixels,
    /// then common 16:9 / legacy modes. Matches modes reported by devices such as Doccamera (up to ~16 MP).
    /// </summary>
    private static readonly (int W, int H)[] DocumentCaptureResolutions =
    {
        (4608, 3456),
        (4096, 2160),
        (3840, 2160),
        (3264, 2448),
        (2592, 1944),
        (2560, 1440),
        (2048, 1536),
        (1920, 1080),
        (1600, 1200),
        (1280, 960),
        (1280, 720),
        (800, 600),
        (640, 480),
    };

    private readonly ILogger _log = LoggingService.ForContext<CameraService>();
    private VideoCapture? _capture;
    private Mat? _frame;
    private Thread? _captureThread;
    private bool _isRunning;
    private readonly object _lock = new();

    public bool IsCapturing => _isRunning;
    public int CurrentCameraIndex { get; private set; } = -1;
    public int CurrentWidth { get; private set; }
    public int CurrentHeight { get; private set; }

    public event Action<BitmapSource>? FrameReady;
    public event Action<string>? Error;

    public List<CameraInfo> GetAvailableCameras()
    {
        var cameras = new List<CameraInfo>();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                // Enumerate all video input devices (no opening = no lag). Index matches OpenCV/DirectShow.
                var deviceList = GetAllVideoInputIndices();
                for (var j = 0; j < deviceList.Count; j++)
                {
                    var (index, name) = deviceList[j];
                    var displayName = string.IsNullOrWhiteSpace(name) ? (deviceList.Count == 1 ? "Camera" : $"Camera {j + 1}") : name;
                    cameras.Add(new CameraInfo
                    {
                        Index = index,
                        Name = displayName,
                        Width = 0,
                        Height = 0
                    });
                    _log.Debug("Found camera {Index}: {Name}", index, displayName);
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Windows camera enumeration failed, falling back to generic probing");
                return GetAvailableCamerasFallback();
            }
        }
        else
        {
            return GetAvailableCamerasFallback();
        }

        return cameras;
    }

    /// <summary>Enumerates all video input devices via DirectShow (index, display name). Does not open devices.</summary>
    private static List<(int Index, string Name)> GetAllVideoInputIndices()
    {
        var result = new List<(int, string)>();
        try
        {
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            for (int i = 0; i < devices.Length; i++)
            {
                var dev = devices[i];
                var name = dev.Name?.Trim() ?? "";
                var displayName = string.IsNullOrWhiteSpace(name) ? $"Camera {i + 1}" : name;
                result.Add((i, displayName));
            }
        }
        catch
        {
            // Ignore DirectShow errors
        }
        return result;
    }

    private List<CameraInfo> GetAvailableCamerasFallback()
    {
        var cameras = new List<CameraInfo>();
        for (int i = 0; i < 5; i++)
        {
            try
            {
                using var capture = new VideoCapture(i);
                if (capture.IsOpened())
                {
                    var width = capture.Get(VideoCaptureProperties.FrameWidth);
                    var height = capture.Get(VideoCaptureProperties.FrameHeight);
                    cameras.Add(new CameraInfo
                    {
                        Index = i,
                        Name = i == 0 ? "Camera" : $"Camera {i + 1}",
                        Width = (int)width,
                        Height = (int)height
                    });
                    capture.Release();
                    _log.Debug("Found camera {Index}: {Width}x{Height}", i, width, height);
                }
                else
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                _log.Debug("Camera {Index} not available: {Error}", i, ex.Message);
                break;
            }
        }

        return cameras;
    }

    public bool StartCapture(int cameraIndex = 0)
    {
        return StartCaptureAsync(cameraIndex).GetAwaiter().GetResult();
    }

    public async Task<bool> StartCaptureAsync(int cameraIndex, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isRunning)
            {
                _log.Warning("Capture already running");
                return false;
            }
        }

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
                if (!capture.IsOpened())
                {
                    _log.Error("Failed to open camera {Index} with DShow, trying default", cameraIndex);
                    capture.Dispose();
                    capture = new VideoCapture(cameraIndex);
                }

                if (!capture.IsOpened())
                {
                    _log.Error("Failed to open camera {Index}", cameraIndex);
                    Application.Current?.Dispatcher.BeginInvoke(() => Error?.Invoke($"Failed to open camera {cameraIndex}"));
                    capture.Dispose();
                    return false;
                }

                TrySetPreferredFourCC(capture);
                if (!TryNegotiateResolution(capture, DocumentCaptureResolutions, "MJPEG"))
                {
                    TryClearFourCCForFallback(capture);
                    if (!TryNegotiateResolution(capture, DocumentCaptureResolutions, "driver default codec"))
                    {
                        var fw = (int)capture.Get(VideoCaptureProperties.FrameWidth);
                        var fh = (int)capture.Get(VideoCaptureProperties.FrameHeight);
                        _log.Warning(
                            "Camera did not accept any negotiated resolution from the ladder; continuing at driver default {Width}x{Height}",
                            fw, fh);
                    }
                }

                Thread.Sleep(200);
                capture.Set(VideoCaptureProperties.AutoFocus, 1);

                var frame = new Mat();

                lock (_lock)
                {
                    if (_isRunning)
                    {
                        capture.Release();
                        capture.Dispose();
                        frame.Dispose();
                        return false;
                    }
                    _capture = capture;
                    _frame = frame;
                    _isRunning = true;
                    CurrentCameraIndex = cameraIndex;
                    CurrentWidth = (int)capture.Get(VideoCaptureProperties.FrameWidth);
                    CurrentHeight = (int)capture.Get(VideoCaptureProperties.FrameHeight);
                    _captureThread = new Thread(CaptureLoop)
                    {
                        IsBackground = true,
                        Name = "CameraCapture"
                    };
                    _captureThread.Start();
                }

                _log.Information(
                    "Camera capture started: Camera {Index}, stream {Width}x{Height}",
                    cameraIndex,
                    CurrentWidth,
                    CurrentHeight);
                return true;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error starting camera capture");
            Application.Current?.Dispatcher.BeginInvoke(() => Error?.Invoke($"Error starting camera: {ex.Message}"));
            return false;
        }
    }

    public void StopCapture()
    {
        lock (_lock)
        {
            _isRunning = false;
        }

        _captureThread?.Join(1000);

        lock (_lock)
        {
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;

            _frame?.Dispose();
            _frame = null;

            CurrentCameraIndex = -1;
            CurrentWidth = 0;
            CurrentHeight = 0;
        }

        _log.Information("Camera capture stopped");
    }

    private void CaptureLoop()
    {
        while (_isRunning)
        {
            try
            {
                Mat? frameCopy = null;
                lock (_lock)
                {
                    if (_capture == null || _frame == null || !_isRunning)
                        break;

                    if (_capture.Read(_frame) && !_frame.Empty())
                        frameCopy = _frame.Clone();
                }

                if (frameCopy != null)
                {
                    try
                    {
                        var bitmapSource = frameCopy.ToBitmapSource();
                        frameCopy.Dispose();
                        frameCopy = null;
                        bitmapSource.Freeze();
                        Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                FrameReady?.Invoke(bitmapSource);
                            }
                            catch (Exception ex)
                            {
                                _log.Warning(ex, "Error raising FrameReady");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "Error converting frame to BitmapSource");
                        frameCopy?.Dispose();
                    }
                }

                Thread.Sleep(33); // ~30 FPS
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Error in capture loop");
            }
        }
    }

    public Mat? CaptureFrameMat()
    {
        lock (_lock)
        {
            if (_frame == null || _frame.Empty())
                return null;

            return _frame.Clone();
        }
    }

    public BitmapSource? CaptureFrame()
    {
        lock (_lock)
        {
            if (_frame == null || _frame.Empty())
                return null;

            try
            {
                var bitmapSource = _frame.ToBitmapSource();
                bitmapSource.Freeze();
                return bitmapSource;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error capturing frame");
            }

            return null;
        }
    }

    public byte[]? CaptureFrameBytes()
    {
        lock (_lock)
        {
            if (_capture == null || _frame == null)
                return null;

            try
            {
                if (_capture.Read(_frame) && !_frame.Empty())
                {
                    return _frame.ToBytes(".png");
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error capturing frame bytes");
            }

            return null;
        }
    }

    public async Task<string?> CaptureAndSaveAsync(string outputPath)
    {
        return await Task.Run(() =>
        {
            lock (_lock)
            {
                if (_capture == null)
                    return null;

                try
                {
                    using var frame = new Mat();
                    if (_capture.Read(frame) && !frame.Empty())
                    {
                        var directory = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(directory))
                            Directory.CreateDirectory(directory);

                        var ext = Path.GetExtension(outputPath).ToLowerInvariant();
                        if (ext is ".jpg" or ".jpeg")
                            Cv2.ImWrite(outputPath, frame, new ImageEncodingParam(ImwriteFlags.JpegQuality, 100));
                        else
                            Cv2.ImWrite(outputPath, frame);
                        _log.Information("Frame saved to: {Path}", outputPath);
                        return outputPath;
                    }
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error saving frame");
                }

                return null;
            }
        });
    }

    public void Dispose()
    {
        StopCapture();
        GC.SuppressFinalize(this);
    }

    private static void TrySetPreferredFourCC(VideoCapture capture)
    {
        try
        {
            capture.Set(VideoCaptureProperties.FourCC, FourCC.MJPG);
        }
        catch
        {
            // Some drivers may not support changing FourCC; ignore failures.
        }
    }

    /// <summary>Clear FourCC so the DirectShow backend can pick YUY2/uncompressed modes that list higher resolutions.</summary>
    private static void TryClearFourCCForFallback(VideoCapture capture)
    {
        try
        {
            capture.Set(VideoCaptureProperties.FourCC, 0);
        }
        catch
        {
            try
            {
                capture.Set(VideoCaptureProperties.FourCC, FourCC.FromString("    "));
            }
            catch
            {
                // Ignore — second negotiation pass may still work through size-only hints on some drivers.
            }
        }
    }

    /// <returns>True if a ladder entry was accepted exactly by the driver.</returns>
    private bool TryNegotiateResolution(VideoCapture capture, (int W, int H)[] ladder, string passLabel)
    {
        foreach (var (w, h) in ladder)
        {
            capture.Set(VideoCaptureProperties.FrameWidth, w);
            capture.Set(VideoCaptureProperties.FrameHeight, h);
            var actualW = (int)capture.Get(VideoCaptureProperties.FrameWidth);
            var actualH = (int)capture.Get(VideoCaptureProperties.FrameHeight);
            if (actualW == w && actualH == h)
            {
                _log.Information(
                    "Camera negotiated resolution ({Pass}): {Width}x{Height}",
                    passLabel,
                    w,
                    h);
                return true;
            }

            _log.Debug(
                "Camera resolution attempt ({Pass}): requested {ReqW}x{ReqH}, got {ActW}x{ActH}",
                passLabel,
                w,
                h,
                actualW,
                actualH);
        }

        return false;
    }

}

public class CameraInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }

    public override string ToString() => Name;
}
