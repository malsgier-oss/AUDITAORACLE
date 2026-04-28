using System.IO;
using OpenCvSharp;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Core.ImageProcessing;

public interface IImageProcessingService
{
    Task<ProcessedImage> ProcessImageAsync(string inputPath, ProcessingOptions? options = null);
    Task<ProcessedImage> ProcessImageAsync(byte[] imageData, ProcessingOptions? options = null);
    Mat AutoCrop(Mat image);
    Mat Deskew(Mat image);
    Mat EnhanceContrast(Mat image);
    Mat ReduceNoise(Mat image);
    Mat ConvertToGrayscale(Mat image);
    (bool HasDocument, Rect BoundingBox) DetectDocument(Mat image);
    (bool Found, Point2f[] Corners, float Confidence) DetectDocumentCorners(Mat image);
    Mat ApplyPerspectiveTransform(Mat image, Point2f[] corners, DocumentPaperSize paperSize = DocumentPaperSize.A4);
    Mat ApplyEnhanceMode(Mat image, DocumentEnhanceMode mode);
}

public enum DocumentEnhanceMode { Color, Grayscale, BlackAndWhite }

/// <summary>Output size for document scan: A4 and A3 share the same aspect ratio (ISO 216); A3 output is larger in pixels.</summary>
public enum DocumentPaperSize
{
    A4,
    A3
}

public class ImageProcessingService : IImageProcessingService
{
    private readonly ILogger _log = LoggingService.ForContext<ImageProcessingService>();

    public async Task<ProcessedImage> ProcessImageAsync(string inputPath, ProcessingOptions? options = null)
    {
        return await Task.Run(() =>
        {
            options ??= ProcessingOptions.Default;
            var result = new ProcessedImage { OriginalPath = inputPath };
            var startTime = DateTime.Now;
            try
            {
                using var original = Cv2.ImRead(inputPath);
                if (original.Empty()) { result.Success = false; result.Error = "Failed to read image"; return result; }
                result.OriginalWidth = original.Width;
                result.OriginalHeight = original.Height;
                using var processed = ProcessImage(original, options);
                result.ProcessedWidth = processed.Width;
                result.ProcessedHeight = processed.Height;
                var outputPath = GetOutputPath(inputPath, options.OutputSuffix);
                Cv2.ImWrite(outputPath, processed);
                result.ProcessedPath = outputPath;
                result.Success = true;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error processing image: {Path}", inputPath);
                result.Success = false;
                result.Error = ex.Message;
            }
            result.ProcessingTimeMs = (int)(DateTime.Now - startTime).TotalMilliseconds;
            return result;
        });
    }

    public async Task<ProcessedImage> ProcessImageAsync(byte[] imageData, ProcessingOptions? options = null)
    {
        return await Task.Run(() =>
        {
            options ??= ProcessingOptions.Default;
            var result = new ProcessedImage();
            var startTime = DateTime.Now;
            try
            {
                using var original = Mat.FromImageData(imageData);
                if (original.Empty()) { result.Success = false; result.Error = "Failed to decode image data"; return result; }
                result.OriginalWidth = original.Width;
                result.OriginalHeight = original.Height;
                using var processed = ProcessImage(original, options);
                result.ProcessedWidth = processed.Width;
                result.ProcessedHeight = processed.Height;
                result.ProcessedData = processed.ToBytes(".png");
                result.Success = true;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error processing image data");
                result.Success = false;
                result.Error = ex.Message;
            }
            result.ProcessingTimeMs = (int)(DateTime.Now - startTime).TotalMilliseconds;
            return result;
        });
    }

    private Mat ProcessImage(Mat original, ProcessingOptions options)
    {
        var current = original.Clone();
        try
        {
            if (options.ConvertToGrayscale) { var gray = ConvertToGrayscale(current); current.Dispose(); current = gray; }
            if (options.AutoCrop) { var cropped = AutoCrop(current); current.Dispose(); current = cropped; }
            if (options.Deskew) { var deskewed = Deskew(current); current.Dispose(); current = deskewed; }
            if (options.ReduceNoise) { var denoised = ReduceNoise(current); current.Dispose(); current = denoised; }
            if (options.EnhanceContrast) { var enhanced = EnhanceContrast(current); current.Dispose(); current = enhanced; }
            return current;
        }
        catch { current.Dispose(); throw; }
    }

    public Mat AutoCrop(Mat image)
    {
        try
        {
            using var gray = image.Channels() == 1 ? image.Clone() : image.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(5, 5), 0);
            using var edged = new Mat();
            Cv2.Canny(blurred, edged, 75, 200);
            Cv2.FindContours(edged, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            if (contours.Length == 0)
                return image.Clone();

            var largestContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).FirstOrDefault();
            if (largestContour == null || largestContour.Length == 0)
                return image.Clone();

            var boundingRect = Cv2.BoundingRect(largestContour);
            int padding = 10;
            int x = Math.Max(0, boundingRect.X - padding);
            int y = Math.Max(0, boundingRect.Y - padding);
            int width = Math.Min(image.Width - x, boundingRect.Width + 2 * padding);
            int height = Math.Min(image.Height - y, boundingRect.Height + 2 * padding);
            if (width * height < image.Width * image.Height * 0.1) return image.Clone();
            return new Mat(image, new OpenCvSharp.Rect(x, y, width, height)).Clone();
        }
        catch { return image.Clone(); }
    }

    public Mat Deskew(Mat image)
    {
        try
        {
            using var gray = image.Channels() == 1 ? image.Clone() : image.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var edges = new Mat();
            Cv2.Canny(gray, edges, 50, 150, 3);
            var lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180, 100, 50, 10);
            if (lines.Length == 0) return image.Clone();
            var angles = lines.Select(l => Math.Atan2(l.P2.Y - l.P1.Y, l.P2.X - l.P1.X) * 180 / Math.PI).Where(a => Math.Abs(a) < 45).ToList();
            if (angles.Count == 0) return image.Clone();
            var avgAngle = angles.Average();
            if (Math.Abs(avgAngle) < 0.5) return image.Clone();
            var center = new Point2f(image.Width / 2f, image.Height / 2f);
            using var rotationMatrix = Cv2.GetRotationMatrix2D(center, avgAngle, 1.0);
            var result = new Mat();
            Cv2.WarpAffine(image, result, rotationMatrix, image.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);
            return result;
        }
        catch { return image.Clone(); }
    }

    public Mat EnhanceContrast(Mat image)
    {
        try
        {
            using var clahe = Cv2.CreateCLAHE(1.2, new OpenCvSharp.Size(8, 8));
            if (image.Channels() == 1)
            {
                var result = new Mat();
                clahe.Apply(image, result);
                return result;
            }
            using var lab = new Mat();
            Cv2.CvtColor(image, lab, ColorConversionCodes.BGR2Lab);
            var channels = Cv2.Split(lab);
            var enhancedL = new Mat();
            clahe.Apply(channels[0], enhancedL);
            channels[0].Dispose();
            channels[0] = enhancedL;
            var enhanced = new Mat();
            Cv2.Merge(channels, enhanced);
            var resultColor = new Mat();
            Cv2.CvtColor(enhanced, resultColor, ColorConversionCodes.Lab2BGR);
            foreach (var ch in channels) ch.Dispose();
            enhanced.Dispose();
            return resultColor;
        }
        catch { return image.Clone(); }
    }

    public Mat ReduceNoise(Mat image)
    {
        try
        {
            var result = new Mat();
            if (image.Channels() == 1) Cv2.FastNlMeansDenoising(image, result, 10, 7, 21);
            else Cv2.FastNlMeansDenoisingColored(image, result, 10, 10, 7, 21);
            return result;
        }
        catch { return image.Clone(); }
    }

    public Mat ConvertToGrayscale(Mat image)
    {
        if (image.Channels() == 1) return image.Clone();
        var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    public (bool HasDocument, Rect BoundingBox) DetectDocument(Mat image)
    {
        var (found, corners, _) = DetectDocumentCorners(image);
        if (found) return (true, Cv2.BoundingRect(corners.Select(p => new OpenCvSharp.Point((int)p.X, (int)p.Y))));
        return (false, new Rect());
    }

    public (bool Found, Point2f[] Corners, float Confidence) DetectDocumentCorners(Mat image)
    {
        try
        {
            // Normalize size for detection to keep parameters consistent
            const int detectHeight = 800;
            double scale = (double)detectHeight / image.Height;
            using var resized = new Mat();
            if (image.Height > detectHeight)
            {
                Cv2.Resize(image, resized, new OpenCvSharp.Size((int)(image.Width * scale), detectHeight), 0, 0, InterpolationFlags.Area);
            }
            else
            {
                image.CopyTo(resized);
                scale = 1.0;
            }

            using var gray = resized.Channels() == 1 ? resized.Clone() : resized.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(7, 7), 0);

            using var canny = new Mat();
            Cv2.Canny(blurred, canny, 30, 100);

            using var adaptive = new Mat();
            Cv2.AdaptiveThreshold(blurred, adaptive, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.BinaryInv, 11, 2);
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
            using var closed = new Mat();
            Cv2.MorphologyEx(adaptive, closed, MorphTypes.Close, kernel, iterations: 2);

            using var combined = new Mat();
            Cv2.BitwiseOr(canny, closed, combined);
            using var dilated = new Mat();
            Cv2.Dilate(combined, dilated, kernel, iterations: 1);

            Cv2.FindContours(dilated, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            var imgArea = (double)(resized.Width * resized.Height);

            foreach (var contour in contours.OrderByDescending(c => Cv2.ContourArea(c)).Take(10))
            {
                var area = Cv2.ContourArea(contour);
                if (area < imgArea * 0.02) continue;

                var peri = Cv2.ArcLength(contour, true);
                var approx = Cv2.ApproxPolyDP(contour, 0.02 * peri, true);
                if (approx.Length == 4)
                {
                    var pts = approx.Select(p => new Point2f(p.X, p.Y)).ToArray();
                    var ordered = OrderPoints(pts);
                    if (ordered == null) continue;

                    var orderedI = ordered.Select(p => new OpenCvSharp.Point((int)p.X, (int)p.Y)).ToArray();
                    if (!Cv2.IsContourConvex(orderedI)) continue;

                    if (!ValidateQuadAngles(ordered, 30, 150)) continue;

                    // Map back to original scale
                    var finalPts = ordered.Select(p => new Point2f((float)(p.X / scale), (float)(p.Y / scale))).ToArray();
                    return (true, finalPts, (float)(area / imgArea));
                }
            }
            return (false, Array.Empty<Point2f>(), 0f);
        }
        catch { return (false, Array.Empty<Point2f>(), 0f); }
    }

    private static Point2f[]? OrderPoints(Point2f[] pts)
    {
        if (pts.Length != 4) return null;
        var ordered = new Point2f[4];
        var sum = pts.Select(p => p.X + p.Y).ToArray();
        ordered[0] = pts[Array.IndexOf(sum, sum.Min())];
        ordered[2] = pts[Array.IndexOf(sum, sum.Max())];
        var diff = pts.Select(p => p.Y - p.X).ToArray();
        ordered[1] = pts[Array.IndexOf(diff, diff.Min())];
        ordered[3] = pts[Array.IndexOf(diff, diff.Max())];
        return ordered;
    }

    private static bool ValidateQuadAngles(Point2f[] quad, double minDeg, double maxDeg)
    {
        for (int i = 0; i < 4; i++)
        {
            var a = quad[i]; var b = quad[(i + 1) % 4]; var c = quad[(i + 2) % 4];
            var ab = new Point2f(a.X - b.X, a.Y - b.Y);
            var cb = new Point2f(c.X - b.X, c.Y - b.Y);
            var dot = ab.X * cb.X + ab.Y * cb.Y;
            var magA = Math.Sqrt(ab.X * ab.X + ab.Y * ab.Y);
            var magC = Math.Sqrt(cb.X * cb.X + cb.Y * cb.Y);
            if (magA < 1e-6 || magC < 1e-6) return false;
            var angleDeg = Math.Acos(Math.Clamp(dot / (magA * magC), -1.0, 1.0)) * 180.0 / Math.PI;
            if (angleDeg < minDeg || angleDeg > maxDeg) return false;
        }
        return true;
    }

    /// <summary>ISO A-series portrait: long/short = 420/297 mm (A3) = 297/210 mm (A4) = sqrt(2).</summary>
    private const float IsoLongSideMm = 420f;
    private const float IsoShortSideMm = 297f;

    public Mat ApplyPerspectiveTransform(Mat image, Point2f[] corners, DocumentPaperSize paperSize = DocumentPaperSize.A4)
    {
        if (corners.Length != 4) return image.Clone();
        var center = new Point2f(corners.Average(c => c.X), corners.Average(c => c.Y));
        const float margin = 2f;
        var src = corners.Select(c => {
            var dx = c.X - center.X; var dy = c.Y - center.Y;
            var dist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dist < 1f) return c;
            return new Point2f(c.X - dx * (margin / dist), c.Y - dy * (margin / dist));
        }).ToArray();

        var w1 = (float)Math.Sqrt(Math.Pow(src[1].X - src[0].X, 2) + Math.Pow(src[1].Y - src[0].Y, 2));
        var w2 = (float)Math.Sqrt(Math.Pow(src[2].X - src[3].X, 2) + Math.Pow(src[2].Y - src[3].Y, 2));
        var h1 = (float)Math.Sqrt(Math.Pow(src[3].X - src[0].X, 2) + Math.Pow(src[3].Y - src[0].Y, 2));
        var h2 = (float)Math.Sqrt(Math.Pow(src[2].X - src[1].X, 2) + Math.Pow(src[2].Y - src[1].Y, 2));
        int maxW = (int)Math.Max(w1, w2);
        int maxH = (int)Math.Max(h1, h2);
        if (maxW < 10 || maxH < 10) return image.Clone();

        // Always enforce ISO A-series aspect ratio (same for A4 and A3).
        NormalizeToIsoPaperAspect(ref maxW, ref maxH);

        // A3 has linear dimensions sqrt(2) times A4 — scale pixel output accordingly.
        if (paperSize == DocumentPaperSize.A3)
        {
            var factor = Math.Sqrt(2);
            maxW = Math.Max(10, (int)Math.Round(maxW * factor));
            maxH = Math.Max(10, (int)Math.Round(maxH * factor));
        }

        var dst = new[] { new Point2f(0, 0), new Point2f(maxW - 1, 0), new Point2f(maxW - 1, maxH - 1), new Point2f(0, maxH - 1) };
        using var m = Cv2.GetPerspectiveTransform(src, dst);
        var result = new Mat();
        Cv2.WarpPerspective(image, result, m, new OpenCvSharp.Size(maxW, maxH), InterpolationFlags.Cubic, BorderTypes.Constant, Scalar.White);
        return result;
    }

    /// <summary>Forces destination width/height to ISO A-series proportions (short:long = 1:sqrt(2)).</summary>
    private static void NormalizeToIsoPaperAspect(ref int maxW, ref int maxH)
    {
        var ratioLongOverShort = IsoLongSideMm / IsoShortSideMm;
        if (maxW >= maxH)
            maxH = (int)Math.Round(maxW / ratioLongOverShort);
        else
            maxW = (int)Math.Round(maxH / ratioLongOverShort);
    }

    public Mat ApplyEnhanceMode(Mat image, DocumentEnhanceMode mode)
    {
        return mode switch
        {
            DocumentEnhanceMode.Grayscale => UnsharpMask(EnhanceContrast(ConvertToGrayscale(image))),
            DocumentEnhanceMode.BlackAndWhite => ApplyBwScan(image),
            _ => image.Clone() // Color mode: Keep it raw as per user request (live view is high quality)
        };
    }

    private static Mat UnsharpMask(Mat image)
    {
        using var blurred = new Mat();
        Cv2.GaussianBlur(image, blurred, new OpenCvSharp.Size(0, 0), 1.0);
        var res = new Mat();
        Cv2.AddWeighted(image, 1.5, blurred, -0.5, 0, res);
        return res;
    }

    private Mat ApplyBwScan(Mat image)
    {
        using var gray = ConvertToGrayscale(image);
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(3, 3), 0);
        var result = new Mat();
        Cv2.AdaptiveThreshold(blurred, result, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 25, 10);
        return result;
    }

    private string GetOutputPath(string path, string suffix)
    {
        return Path.Combine(Path.GetDirectoryName(path) ?? "", $"{Path.GetFileNameWithoutExtension(path)}{suffix}{Path.GetExtension(path)}");
    }
}

public class ProcessingOptions
{
    public bool AutoCrop { get; set; } = true;
    public bool Deskew { get; set; } = true;
    public bool EnhanceContrast { get; set; } = true;
    public bool ReduceNoise { get; set; } = false;
    public bool ConvertToGrayscale { get; set; } = false;
    public string OutputSuffix { get; set; } = "_processed";
    public static ProcessingOptions Default => new();
}

public class ProcessedImage
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? OriginalPath { get; set; }
    public string? ProcessedPath { get; set; }
    public byte[]? ProcessedData { get; set; }
    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }
    public int ProcessedWidth { get; set; }
    public int ProcessedHeight { get; set; }
    public int ProcessingTimeMs { get; set; }
}
