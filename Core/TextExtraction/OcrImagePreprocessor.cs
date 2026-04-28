using System;
using System.IO;
using OpenCvSharp;
using Serilog;
using WorkAudit.Storage;

namespace WorkAudit.Core.TextExtraction;

/// <summary>
/// Shared OpenCV scan preprocessing for OCR.
/// </summary>
public static class OcrImagePreprocessor
{
    /// <summary>Reads NL-means denoise strength (h); default 3; clamped 1–20.</summary>
    public static int GetNlMeansH(IConfigStore config)
    {
        var h = config.GetSettingInt("ocr_preprocess_nlmeans_h", 3);
        if (h < 1) h = 1;
        if (h > 20) h = 20;
        return h;
    }

    /// <summary>
    /// Heavy preprocessing: upscale, denoise, deskew, Otsu/adaptive threshold. Returns a temp PNG path, or <paramref name="inputPath"/> on failure.
    /// </summary>
    public static string HeavyPreprocess(string inputPath, IConfigStore config)
    {
        Mat? src = null;
        Mat? gray = null;
        Mat? resized = null;
        Mat? denoised = null;
        Mat? deskewed = null;
        Mat? otsuBinary = null;
        Mat? thresholded = null;

        try
        {
            var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_pre.png");

            // Load in color first, then convert to grayscale for processing.
            src = Cv2.ImRead(inputPath, ImreadModes.Color);
            if (src.Empty())
                return inputPath;

            // Convert to grayscale for processing
            gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            if (gray.Width < 1000 || gray.Height < 1000)
            {
                resized = new Mat();
                Cv2.Resize(gray, resized, new OpenCvSharp.Size(), 2.0, 2.0, InterpolationFlags.Cubic);
                gray.Dispose();
                gray = resized;
                resized = null;
            }

            denoised = new Mat();
            Cv2.FastNlMeansDenoising(gray, denoised, GetNlMeansH(config));

            using (var grayForAngle = denoised.Clone())
            {
                Cv2.Threshold(grayForAngle, grayForAngle, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                using var pts = new Mat();
                Cv2.FindNonZero(grayForAngle, pts);
                if (!pts.Empty() && pts.Rows > 10)
                {
                    var box = Cv2.MinAreaRect(pts);
                    var angle = box.Angle;
                    if (angle < -45)
                        angle += 90;
                    if (Math.Abs(angle) > 0.5)
                    {
                        var center = new Point2f(denoised.Cols / 2f, denoised.Rows / 2f);
                        var rot = Cv2.GetRotationMatrix2D(center, angle, 1.0);
                        deskewed = new Mat();
                        Cv2.WarpAffine(denoised, deskewed, rot, denoised.Size(),
                            InterpolationFlags.Linear, BorderTypes.Constant, Scalar.White);
                        denoised.Dispose();
                        denoised = deskewed;
                        deskewed = null;
                    }
                }
            }

            Cv2.MeanStdDev(denoised, out _, out var stdDev);
            var spread = stdDev.Val0;
            if (spread > 45)
            {
                Cv2.ImWrite(outputPath, denoised);
                Log.Debug("Preprocess: high-contrast path (std={Std})", spread);
                return outputPath;
            }

            otsuBinary = new Mat();
            Cv2.Threshold(denoised, otsuBinary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            var whiteRatio = Cv2.CountNonZero(otsuBinary) / (double)(otsuBinary.Rows * otsuBinary.Cols);
            thresholded = otsuBinary;
            otsuBinary = null;

            if (whiteRatio is < 0.02 or > 0.98)
            {
                thresholded.Dispose();
                thresholded = new Mat();
                Cv2.AdaptiveThreshold(denoised, thresholded, 255, AdaptiveThresholdTypes.GaussianC,
                    ThresholdTypes.Binary, 31, 12);
                Log.Debug("Preprocess: adaptive threshold (whiteRatio={Ratio:F3})", whiteRatio);
            }

            Cv2.ImWrite(outputPath, thresholded);
            Log.Debug("Image preprocessing completed: {Path}", outputPath);
            return outputPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during image preprocessing");
            return inputPath;
        }
        finally
        {
            src?.Dispose();
            gray?.Dispose();
            resized?.Dispose();
            denoised?.Dispose();
            deskewed?.Dispose();
            otsuBinary?.Dispose();
            thresholded?.Dispose();
        }
    }

    /// <summary>
    /// Light preprocessing: mild upscale, blur, Otsu. Returns a temp PNG path, or <paramref name="inputPath"/> on failure.
    /// </summary>
    public static string LightPreprocess(string inputPath)
    {
        Mat? src = null;
        Mat? work = null;
        try
        {
            var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_pre_light.png");
            src = Cv2.ImRead(inputPath, ImreadModes.Grayscale);
            if (src.Empty())
                return inputPath;

            work = src.Clone();
            if (work.Width < 1200 || work.Height < 1200)
                Cv2.Resize(work, work, new OpenCvSharp.Size(), 1.5, 1.5, InterpolationFlags.Cubic);

            Cv2.GaussianBlur(work, work, new OpenCvSharp.Size(3, 3), 0);
            Cv2.Threshold(work, work, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            Cv2.ImWrite(outputPath, work);
            return outputPath;
        }
        catch
        {
            return inputPath;
        }
        finally
        {
            src?.Dispose();
            work?.Dispose();
        }
    }
}
