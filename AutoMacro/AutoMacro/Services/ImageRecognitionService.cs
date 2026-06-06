using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using OpenCvSharp;
using DrawingPoint = System.Drawing.Point;
using WinForms = System.Windows.Forms;

namespace AutoMacro.Services;

public class ImageRecognitionService : IImageRecognitionService
{
    private static readonly double[] TemplateScales = { 1.0, 0.95, 1.05, 0.9, 1.1, 0.85, 1.15, 0.8, 1.2 };

    private readonly IRunLogger _logger;
    private bool _openCvFallbackLogged;

    public ImageRecognitionService(IRunLogger logger)
    {
        _logger = logger;
    }

    public async Task<(bool Found, int X, int Y, double Score)> FindImageAsync(
        string imagePath,
        double threshold,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var normalizedThreshold = Math.Clamp(threshold <= 0 ? 0.92 : threshold, 0.5, 1.0);
        var endAt = DateTime.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));
        var bestScore = 0d;
        var bestX = 0;
        var bestY = 0;

        if (!File.Exists(imagePath))
        {
            _logger.Error("Image", $"模板图片不存在: {imagePath}", captureScreenshot: true);
            return (false, 0, 0, 0);
        }

        using var templateSource = new Bitmap(imagePath);
        using var template = CloneAs24Bpp(templateSource);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capture = CaptureScreen();
            using var screen = capture.Bitmap;
            var result = FindInBitmap(screen, template, normalizedThreshold, capture.Left, capture.Top);
            if (result.Found)
            {
                _logger.Info("Image", $"找到图片: {Path.GetFileName(imagePath)}, score={result.Score:0.000}, x={result.X}, y={result.Y}");
                return result;
            }

            if (result.Score > bestScore)
            {
                bestScore = result.Score;
                bestX = result.X;
                bestY = result.Y;
            }

            await Task.Delay(250, cancellationToken);
        } while (DateTime.UtcNow < endAt);

        _logger.Error("Image", $"等待图片超时: {Path.GetFileName(imagePath)}, bestScore={bestScore:0.000}", captureScreenshot: true);
        return (false, bestX, bestY, bestScore);
    }

    private (bool Found, int X, int Y, double Score) FindInBitmap(
        Bitmap screen,
        Bitmap template,
        double threshold,
        int offsetX,
        int offsetY)
    {
        try
        {
            return FindWithOpenCv(screen, template, threshold, offsetX, offsetY);
        }
        catch (Exception ex)
        {
            if (!_openCvFallbackLogged)
            {
                _openCvFallbackLogged = true;
                _logger.Warn("Image", $"OpenCV 图片识别不可用，已切换到兼容算法: {ex.Message}");
            }

            return FindWithSampling(screen, template, threshold, offsetX, offsetY);
        }
    }

    private static ScreenCapture CaptureScreen()
    {
        var bounds = WinForms.SystemInformation.VirtualScreen;
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        return new ScreenCapture(bitmap, bounds.Left, bounds.Top);
    }

    private (bool Found, int X, int Y, double Score) FindWithOpenCv(
        Bitmap screen,
        Bitmap template,
        double threshold,
        int offsetX,
        int offsetY)
    {
        using var screenColor = BitmapToMat(screen);
        using var templateColor = BitmapToMat(template);
        using var screenGray = new Mat();
        using var templateGray = new Mat();

        Cv2.CvtColor(screenColor, screenGray, ColorConversionCodes.BGR2GRAY);
        Cv2.CvtColor(templateColor, templateGray, ColorConversionCodes.BGR2GRAY);

        if (templateGray.Width > screenGray.Width || templateGray.Height > screenGray.Height)
            return (false, 0, 0, 0);

        if (IsLowVarianceTemplate(templateGray))
        {
            if (!_openCvFallbackLogged)
            {
                _openCvFallbackLogged = true;
                _logger.Warn("Image", "模板图片细节太少，OpenCV 匹配容易误判，已切换到兼容算法。建议截取包含文字、图标或边框的图片。");
            }

            return FindWithSampling(screen, template, threshold, offsetX, offsetY);
        }

        var bestScore = 0d;
        var bestX = 0;
        var bestY = 0;

        foreach (var scale in TemplateScales)
        {
            var width = Math.Max(1, (int)Math.Round(templateGray.Width * scale));
            var height = Math.Max(1, (int)Math.Round(templateGray.Height * scale));
            if (width > screenGray.Width || height > screenGray.Height)
                continue;

            Mat candidate = templateGray;
            Mat? resized = null;
            try
            {
                if (Math.Abs(scale - 1.0) > 0.001)
                {
                    resized = new Mat();
                    Cv2.Resize(
                        templateGray,
                        resized,
                        new OpenCvSharp.Size(width, height),
                        0,
                        0,
                        scale < 1.0 ? InterpolationFlags.Area : InterpolationFlags.Linear);
                    candidate = resized;
                }

                using var result = new Mat();
                Cv2.MatchTemplate(screenGray, candidate, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLocation);

                if (double.IsNaN(maxValue) || double.IsInfinity(maxValue))
                    maxValue = 0;

                if (maxValue > bestScore)
                {
                    bestScore = maxValue;
                    bestX = offsetX + maxLocation.X + candidate.Width / 2;
                    bestY = offsetY + maxLocation.Y + candidate.Height / 2;
                }
            }
            finally
            {
                resized?.Dispose();
            }
        }

        return (bestScore >= threshold, bestX, bestY, bestScore);
    }

    private static bool IsLowVarianceTemplate(Mat templateGray)
    {
        Cv2.MeanStdDev(templateGray, out _, out var stddev);
        return stddev.Val0 < 3.0;
    }

    private static Mat BitmapToMat(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            using var wrapped = Mat.FromPixelData(bitmap.Height, bitmap.Width, MatType.CV_8UC3, data.Scan0, data.Stride);
            return wrapped.Clone();
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static Bitmap CloneAs24Bpp(Bitmap source)
    {
        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(clone);
        graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        return clone;
    }

    private static (bool Found, int X, int Y, double Score) FindWithSampling(
        Bitmap screen,
        Bitmap template,
        double threshold,
        int offsetX,
        int offsetY)
    {
        if (template.Width > screen.Width || template.Height > screen.Height)
            return (false, 0, 0, 0);

        var samplePoints = BuildSamplePoints(template.Width, template.Height);
        var bestScore = 0d;
        var bestX = 0;
        var bestY = 0;
        var scanStep = template.Width * template.Height > 9000 ? 3 : 2;

        for (var y = 0; y <= screen.Height - template.Height; y += scanStep)
        {
            for (var x = 0; x <= screen.Width - template.Width; x += scanStep)
            {
                var score = ScoreAt(screen, template, x, y, samplePoints);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = offsetX + x + template.Width / 2;
                    bestY = offsetY + y + template.Height / 2;
                }

                if (score >= threshold)
                    return (true, offsetX + x + template.Width / 2, offsetY + y + template.Height / 2, score);
            }
        }

        return (false, bestX, bestY, bestScore);
    }

    private static List<DrawingPoint> BuildSamplePoints(int width, int height)
    {
        var points = new List<DrawingPoint>();
        var xSteps = Math.Min(12, Math.Max(3, width / 8));
        var ySteps = Math.Min(12, Math.Max(3, height / 8));

        for (var yi = 0; yi < ySteps; yi++)
        {
            var y = ySteps == 1 ? 0 : yi * (height - 1) / (ySteps - 1);
            for (var xi = 0; xi < xSteps; xi++)
            {
                var x = xSteps == 1 ? 0 : xi * (width - 1) / (xSteps - 1);
                points.Add(new DrawingPoint(x, y));
            }
        }

        return points;
    }

    private static double ScoreAt(Bitmap screen, Bitmap template, int originX, int originY, List<DrawingPoint> samplePoints)
    {
        double total = 0;
        foreach (var point in samplePoints)
        {
            var a = screen.GetPixel(originX + point.X, originY + point.Y);
            var b = template.GetPixel(point.X, point.Y);
            var distance = Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            total += 1.0 - distance / 765.0;
        }

        return total / samplePoints.Count;
    }

    private sealed record ScreenCapture(Bitmap Bitmap, int Left, int Top);
}
